using System.Collections.Generic;
using UnityEngine;

namespace Portaler
{
    /// <summary>One portal as the server describes it to clients.</summary>
    public class PortalInfo
    {
        public ZDOID Id;
        public string Name;
        public Vector3 Position;
        public Quaternion Rotation;
        public string BiomeCode;
        /// <summary>Server time (ticks) when Portaler saw the portal being built; 0 = built before Portaler.</summary>
        public long BuiltAt;
        /// <summary>Category number (see <see cref="Categories"/>), 0 = none.</summary>
        public int Category;

        public string DisplayName => string.IsNullOrEmpty(Name) ? "(unnamed)" : Name;

        public static void WriteList(ZPackage pkg, List<PortalInfo> list)
        {
            pkg.Write(list.Count);
            foreach (var p in list)
            {
                pkg.Write(p.Id);
                pkg.Write(p.Name ?? "");
                pkg.Write(p.Position);
                pkg.Write(p.Rotation);
                pkg.Write(p.BiomeCode ?? "");
                pkg.Write(p.BuiltAt);
                pkg.Write(p.Category);
            }
        }

        public static List<PortalInfo> ReadList(ZPackage pkg)
        {
            int count = pkg.ReadInt();
            var list = new List<PortalInfo>(count);
            for (int i = 0; i < count; i++)
            {
                list.Add(new PortalInfo
                {
                    Id = pkg.ReadZDOID(),
                    Name = pkg.ReadString(),
                    Position = pkg.ReadVector3(),
                    Rotation = pkg.ReadQuaternion(),
                    BiomeCode = pkg.ReadString(),
                    BuiltAt = pkg.ReadLong(),
                    Category = Categories.Clamp(pkg.ReadInt()),
                });
            }
            return list;
        }
    }
}
