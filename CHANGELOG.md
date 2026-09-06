# Changelog

## 0.4.12-1.5.0

- Adds `U` replacement for an inscription under the mouse cursor in the unit status panel.
- Replaces only the selected inscription with a random entry from the active rarity group.
- Uses the game's affecter application path and refreshes the visible trait panel.
- Changes the rarity-filter hotkey from `T` to `Y`.
- Cycles `Rare only` → `Legendary only` → `Rare + legendary`.

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
