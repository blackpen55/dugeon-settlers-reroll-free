# Changelog

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
