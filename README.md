# Dungeon Settlers · Reroll Free

Make prayer rerolls free while keeping the first prayer cost unchanged.

> **Target:** `DS_B.0.4.12` · **Platform:** Windows x64 · **Engine:** Unity IL2CPP

## Download

**[Download the latest release](https://github.com/blackpen55/dugeon-settlers-reroll-free/releases/latest)**

The release ZIP contains only this mod and its instructions. It does **not** contain or replace `GameAssembly.dll`.

## What it changes

| Action | Cost |
| --- | --- |
| First prayer | Unchanged / normal |
| Retry prayer roll | **0 mana stones** |

The mod changes only the retry cost. The prayer result generation itself is left intact.

## Requirements

- Dungeon Settlers Steam version `DS_B.0.4.12`
- Windows x64
- [BepInEx Unity IL2CPP Windows x64](https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html)

The tested loader is `BepInEx 6.0.0-be.788 Unity.IL2CPP-win-x64`. Builds are available from the [official BepInEx build page](https://builds.bepinex.dev/projects/bepinex_be).

## Installation

1. Install BepInEx IL2CPP x64 into the folder containing `DungeonSettlers.exe`.
2. Extract the release ZIP into that same folder and merge the `BepInEx` folder.
3. Start the game. The first BepInEx launch may take a little longer.
4. Check `BepInEx/LogOutput.log` for:

   `Reroll-free patch active in memory only`

## Disable / uninstall

Close the game, then rename or remove:

`BepInEx/plugins/DungeonSettlers.RerollFree.dll`

The plugin patches the running process memory only. It does not edit the game DLL or save files, so no game-file restore is needed.

## Compatibility and safety

The plugin verifies the expected `GameAssembly.dll` code pattern before applying. If a game update changes it, the plugin refuses to patch and the game keeps its normal behavior.

Back up saves before loading an older save in a newer game build. A save-version warning comes from the game/save version mismatch, not from this plugin.

## Source

The plugin source is in [`src/Plugin.cs`](src/Plugin.cs). The native runtime patch checks `LevelUpHelper.HasLevelUpResult`: false keeps the original first-prayer path; true sets only the calculated retry amount to zero and follows the game's existing zero-cost branch.

## Disclaimer

This is an unofficial community mod and is not affiliated with the Dungeon Settlers developer or publisher. The game and BepInEx are not included in this repository.
