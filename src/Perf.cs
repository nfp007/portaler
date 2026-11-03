using System.Diagnostics;
using UnityEngine;

namespace Portaler
{
    /// <summary>
    /// Developer measurements (config [Debug] PerfLog): how much time and memory Portaler itself uses per frame,
    /// and how long each teleport takes from click to arrival. Writes one summary line to the BepInEx log
    /// every <see cref="Interval"/> seconds. Costs nothing when switched off.
    /// </summary>
    public static class Perf
    {
        public enum S { ServerTick, ClientTick, PinScan, PinRefresh, Hide, Scale, Gui, Line, Travel, MapClose, TpStart, Count }

        private static readonly string[] Names = { "srvTick", "cliTick", "pinScan", "pinRefresh", "hide", "scale", "gui", "line", "click", "mapClose", "tpStart" };
        private const float Interval = 10f;

        // measured inside cliTick, so not added to the total a second time
        private static bool Nested(int i) => i == (int)S.PinScan || i == (int)S.PinRefresh || i == (int)S.Line
                                              || i == (int)S.Travel || i == (int)S.MapClose || i == (int)S.TpStart;

#if PERF
        public const bool DevBuild = true;   // developer build: always measures
#else
        public const bool DevBuild = false;
#endif
        public static bool On => DevBuild || (Plugin.PerfLog != null && Plugin.PerfLog.Value);

        // per frame
        private static readonly double[] FrameMs = new double[(int)S.Count];
        private static long _frameAlloc;
        private static int _guiCalls;
        private static int _frame = -1;

        // per interval
        private static readonly double[] SumMs = new double[(int)S.Count];
        private static readonly double[] MaxMs = new double[(int)S.Count];
        private static readonly int[] Hits = new int[(int)S.Count];
        private static double _sumTotal, _maxTotal, _sumFrame, _maxFrame;
        private static long _sumAlloc, _maxAlloc;
        private static int _frames, _framesOpen, _sumGuiCalls;
        private static float _since;

        private static readonly double MsPerTick = 1000.0 / Stopwatch.Frequency;

        public struct Token { public long T; public long M; public bool Valid; }

        public static Token Begin()
        {
            if (!On) return default;
            return new Token { T = Stopwatch.GetTimestamp(), M = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong(), Valid = true };
        }

        public static void End(S s, Token t)
        {
            if (!t.Valid) return;
            double ms = (Stopwatch.GetTimestamp() - t.T) * MsPerTick;
            long alloc = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() - t.M;
            NewFrameIfNeeded();
            FrameMs[(int)s] += ms;
            Hits[(int)s]++;
            if (alloc > 0) _frameAlloc += alloc; // negative = a GC ran meanwhile, not ours to count
            if (s == S.Gui) _guiCalls++;
        }

        /// <summary>Called once per frame (LateUpdate): closes the previous frame and prints the summary when due.</summary>
        public static void Tick()
        {
            if (!On) { _frame = -1; return; }
            NewFrameIfNeeded();
            _since += Time.unscaledDeltaTime;
            if (_since < Interval || _frames == 0) return;
            Report();
        }

        private static void NewFrameIfNeeded()
        {
            int f = Time.frameCount;
            if (f == _frame) return;
            if (_frame >= 0) CloseFrame();
            _frame = f;
        }

        private static void CloseFrame()
        {
            double total = 0;
            for (int i = 0; i < FrameMs.Length; i++)
            {
                if (!Nested(i)) total += FrameMs[i];
                SumMs[i] += FrameMs[i];
                if (FrameMs[i] > MaxMs[i]) MaxMs[i] = FrameMs[i];
                FrameMs[i] = 0;
            }
            _sumTotal += total;
            if (total > _maxTotal) _maxTotal = total;
            double frameMs = Time.unscaledDeltaTime * 1000.0;
            _sumFrame += frameMs;
            if (frameMs > _maxFrame) _maxFrame = frameMs;
            _sumAlloc += _frameAlloc;
            if (_frameAlloc > _maxAlloc) _maxAlloc = _frameAlloc;
            _frameAlloc = 0;
            _sumGuiCalls += _guiCalls;
            _guiCalls = 0;
            _frames++;
            if (PortalClient.Active) _framesOpen++;
        }

        private static void Report()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"[Perf] {_since:0}s, {_frames} frames (game frame avg {_sumFrame / _frames:0.0} ms, max {_maxFrame:0} ms), list open {100 * _framesOpen / _frames}%");
            sb.Append($" | Portaler avg {_sumTotal / _frames:0.000} ms, max {_maxTotal:0.00} ms per frame");
            sb.Append($" | alloc avg {_sumAlloc / _frames / 1024.0:0.0} KB, max {_maxAlloc / 1024.0:0} KB per frame");
            if (_framesOpen > 0) sb.Append($" | OnGUI calls {(double)_sumGuiCalls / _frames:0.0}/frame");
            sb.Append(" | ms avg/max per frame (* = part of cliTick or gui):");
            for (int i = 0; i < Names.Length; i++)
                if (Hits[i] > 0) sb.Append($" {Names[i]}{(Nested(i) ? "*" : "")} {SumMs[i] / _frames:0.000}/{MaxMs[i]:0.00}");
            int pins = 0;
            try { if (Minimap.instance != null) pins = PinCount(); } catch { }
            sb.Append($" | map pins {pins}, portals {PortalClient.Portals.Count}");
            Plugin.Log.LogInfo(sb.ToString());

            for (int i = 0; i < SumMs.Length; i++) { SumMs[i] = 0; MaxMs[i] = 0; Hits[i] = 0; }
            _sumTotal = _maxTotal = _sumFrame = _maxFrame = 0;
            _sumAlloc = _maxAlloc = 0;
            _frames = _framesOpen = _sumGuiCalls = 0;
            _since = 0;
        }

        private static readonly HarmonyLib.AccessTools.FieldRef<Minimap, System.Collections.Generic.List<Minimap.PinData>> PinsRef =
            HarmonyLib.AccessTools.FieldRefAccess<Minimap, System.Collections.Generic.List<Minimap.PinData>>("m_pins");

        private static int PinCount() => PinsRef(Minimap.instance)?.Count ?? 0;

        // ---------- teleport timing ----------

        private static bool _tracking;
        private static float _tpStart, _tpMoved, _tpReady;
        private static Vector3 _tpTarget;
        private static string _tpName;
        private static float _tpDistance;
        private static float _tpWorstFrame;

        public static void TeleportStarted(PortalInfo target, Vector3 exit, float distance)
        {
            if (!On) return;
            _tracking = true;
            _tpStart = Time.realtimeSinceStartup;
            _tpMoved = _tpReady = -1f;
            _tpTarget = exit;
            _tpName = target.DisplayName;
            _tpDistance = distance;
            _tpWorstFrame = 0f;
        }

        /// <summary>Follows a running teleport: when the player was moved, when the target area was ready, when it ended.</summary>
        public static void TeleportTick()
        {
            if (!_tracking) return;
            var player = Player.m_localPlayer;
            if (player == null) { _tracking = false; return; }
            float t = Time.realtimeSinceStartup - _tpStart;
            _tpWorstFrame = Mathf.Max(_tpWorstFrame, Time.unscaledDeltaTime);
            if (_tpMoved < 0f && Utils.DistanceXZ(player.transform.position, _tpTarget) < 10f) _tpMoved = t;
            if (_tpReady < 0f && _tpMoved >= 0f && ZNetScene.instance != null && ZNetScene.instance.IsAreaReady(_tpTarget)) _tpReady = t;
            if (!player.IsTeleporting() && t > 0.5f)
            {
                _tracking = false;
                string moved = _tpMoved >= 0f ? $"{_tpMoved:0.0} s" : "?";
                string ready = _tpReady >= 0f ? $"{_tpReady:0.0} s" : "?";
                Plugin.Log.LogInfo($"[Perf] Teleport to '{_tpName}' ({_tpDistance / 1000f:0.0} km): moved after {moved}, " +
                                   $"area ready after {ready}, arrived after {t:0.0} s (worst frame {_tpWorstFrame * 1000f:0} ms, " +
                                   $"quick arrival {(QuickArrival.Enabled ? "on" : "off")})");
            }
            else if (t > 60f) { _tracking = false; Plugin.Log.LogWarning($"[Perf] Teleport to '{_tpName}' still running after 60 s"); }
        }
    }
}
