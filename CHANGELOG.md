# Changelog

## 0.4.12-1.5.0

- Fixes the retry-cost patch for the current Steam build by locating the native branch by instruction signature instead of a stale hard-coded address.
- Keeps the first prayer cost unchanged and makes only retry prayers free.
- Binds `T` to `Rare + legendary` -> `Rare only` -> `Legendary only`.
- Binds `Y` to replace the inscription under the mouse with another entry from the selected rarity mode.
- Reads the generated IL2CPP panel field as a fallback if the panel initialization hook is missed.

## 0.4.12-1.4.0

- Raises the safety limit to 300 extra rolls only for `Legendary only` mode.
- Keeps the 50-roll limit for `Rare only` and `Rare + legendary` modes.
- Stops immediately when a matching inscription appears.

## 0.4.12-1.3.0

- Shows the selected rarity mode on screen for two seconds after pressing `T`.
- Displays `희귀만`, `전설만`, or `희귀, 전설` to make the active filter visible.

## 0.4.12-1.2.0

- Adds an in-game `T` hotkey to cycle between both rarities, rare only, and legendary only.
- Logs the active rarity filter in `BepInEx/LogOutput.log`.
- Keeps the default behavior set to rare + legendary.

## 0.4.12-1.1.0

- Adds automatic retry until a rare or legendary inscription appears.
- Applies auto-reroll only after the first prayer result exists.
- Preserves the game's inscription/stat lock flags.
- Stops after 50 extra rolls as a safety limit.

## 0.4.12-1.0.0

- Initial release.
- Makes prayer retries cost 0 mana stones.
- Leaves the first prayer cost unchanged.
- Applies in memory without modifying game files or saves.
