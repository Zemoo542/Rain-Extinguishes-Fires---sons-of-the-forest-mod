# Rain Extinguishes Fires

RedLoader mod source for Sons of the Forest.

## Build

Install the game's RedLoader/BepInEx interop files, then set `SOTF_GAME_DIR` to the game installation directory or pass `-p:GameDir=<game-directory>` to `dotnet build`.

The project targets `net11.0`, matching the bundled Modstoke runtime. Release builds omit debug symbols so the generated DLL does not carry a local PDB path.

## Mod files

The build places `RainExtinguishesFires.dll` at the output root and copies `manifest.json` and `extinguish.wav` into the `RainExtinguishesFires` subfolder for the RedLoader package layout.
