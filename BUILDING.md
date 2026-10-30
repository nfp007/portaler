# Building Portaler

Needs the free [.NET SDK 8](https://dotnet.microsoft.com/download) (Windows, Linux or macOS).

1. Create `lib/core/` and copy the BepInEx core DLLs into it (`BepInEx.dll`, `0Harmony.dll` from `BepInEx/core` of any BepInEx Valheim install).
2. Create `lib/valheim/` and copy the whole contents of `Valheim/valheim_Data/Managed/` into it.
3. In the repository folder run:

   ```
   dotnet build -c Release
   ```

   The plugin is written to `bin/Release/Portaler.dll`.

`lib/` is in `.gitignore`: game and BepInEx DLLs must never be committed.

## Making a release package

The folder `package/Portaler/` holds the Thunderstore layout:

```
manifest.json
README.md
CHANGELOG.md
icon.png
plugins/Portaler.dll
```

Copy the new `Portaler.dll` into `plugins/`, raise the version (`src/Plugin.cs`, `Portaler.csproj`, `manifest.json`), then zip the *contents* of `package/Portaler/` as `Portaler-<version>.zip`.
The same zip works for Gale ("Import local mod"), Thunderstore and Hexium. `manifest.json` has no `author` field: on Thunderstore the team name is the author.
