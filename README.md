# Dungeon Settlers - Reroll Helper

Make prayer rerolls free and automatically continue retrying until a rare or legendary inscription appears.

> **Target:** `DS_B.0.4.12` - **Platform:** Windows x64 - **Engine:** Unity IL2CPP

## Download

**[Download the latest release](https://github.com/blackpen55/dungeon-settlers-reroll-helper/releases/latest)**

The release ZIP contains only this mod and its instructions. It does **not** contain or replace `GameAssembly.dll`.

## What it changes

| Action | Cost |
| --- | --- |
| First prayer | Unchanged / normal |
| Retry prayer roll | **0 mana stones** |

When a retry result has no matching inscription, the mod immediately performs another retry. It stops as soon as one appears, or at a safety limit of 50 extra rolls (300 in `Legendary only` mode). The first prayer is never auto-rerolled, and the game's inscription/stat lock flags are preserved.

Press `T` to cycle the target filter during play:

- `Rare only` (blue)
- `Legendary only` (yellow)
- `Rare + legendary` (blue + yellow) - default

The filter resets to `Rare + legendary` when the game starts. Each press is recorded in `BepInEx/LogOutput.log`.
After pressing `T`, the selected mode also appears on screen for two seconds as `희귀만`, `전설만`, or `희귀, 전설`.

To replace an existing inscription, move the mouse over its icon in the unit status panel and press `Y`. This replaces only that inscription with a random entry from the selected rarity group, then refreshes the panel. It does not open the prayer screen or spend a resource. The replacement uses the game's normal affecter application path, and the plugin keeps the change in memory until the game saves it normally.

The target list follows the [inscriptions wiki](https://dungeonsettlers.wiki/ko/inscriptions): `Rare` (`희귀`) and `Legendary` (`전설`) entries. The mod checks the game's inscription keys; it does not change rarity weights or create an inscription.

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

   `Auto-reroll active for rare/legendary inscriptions`

   `Rarity filter toggle is bound to T`

   `Inscription replacement is bound to Y`

## Disable / uninstall

Close the game, then rename or remove:

`BepInEx/plugins/DungeonSettlers.RerollFree.dll`

The plugin patches the running process memory only. It does not edit the game DLL or save files, so no game-file restore is needed.

## Compatibility and safety

The native cost patch verifies the expected `GameAssembly.dll` code pattern before applying. If a game update changes it, that patch refuses to apply and the game keeps its normal cost behavior. The auto-reroll patch also depends on the current `LevelUpHelper.PrayLevelUp(OfferingType, bool, bool)` method; if it is unavailable, the plugin logs the error and leaves auto-reroll disabled. The Y replacement feature additionally depends on the current status-panel and affecter APIs; if those hooks fail, only inscription replacement is disabled.

Back up saves before loading an older save in a newer game build. A save-version warning comes from the game/save version mismatch, not from this plugin.

## Source

The plugin source is in [`src/Plugin.cs`](src/Plugin.cs). The native runtime patch checks `LevelUpHelper.HasLevelUpResult`: false keeps the original first-prayer path; true sets only the calculated retry amount to zero and follows the game's existing zero-cost branch. The Harmony postfix checks `LevelUpHelper.LevelUpInscriptions` after a retry and calls the same three-argument prayer method again until it finds a key matching the current `T`-selected rarity mode. For Y replacement, Harmony records the status-panel's inscription slots and uses the game's `DataApplier`/`AffecterApplyData` path to remove the old key, add the new key, and refresh the panel.

## Disclaimer

This is an unofficial community mod and is not affiliated with the Dungeon Settlers developer or publisher. The game and BepInEx are not included in this repository.
