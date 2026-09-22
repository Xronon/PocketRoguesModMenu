# Pocket Rogues Cheats

An in-game cheat window for **Pocket Rogues** (Steam, Windows). A plugin for the BepInEx 5
mod loader: press **Insert** in the game and a window with three tabs opens. Everything takes
effect right away, without restarting the game.

Русское описание — в [README.ru.md](README.ru.md).

**For single-player only.** In an online game every cheat switches itself off.

## What it does

**Cheats tab**
- God mode (hotkey **F7**), infinite mana (**F8**), no debuffs (poison, burning, stun and the
  like do not stick, and what is already on is removed).
- Sliders: view distance up to ×2.5, run speed up to ×3, armor and damage up to ×100.
- Loot: gold and experience up to ×100. Spending gold never costs more.
- **Don't post scores to Steam** — on by default. After a run the game posts your score to
  public Steam leaderboards; this is the one place where cheats would be visible to other
  people.
- Scouting: map (minimap without fog, enemies shown), compass (hidden caches visible), no fog
  around the hero.
- Artifacts: the property of any of the game's 39 artifacts without owning the artifact.
  Hover over a line to see what it does.
- The yellow list of active cheats in the top left corner can be switched off.

**Editor tab** — changes that apply on the spot:
- gold, skill points and the four attributes of every hero (up to 50, the game's own limit);
- the gear of the hero you are playing: quality, effect tiers, add or remove effects (only
  those the game itself can roll on that item), remove curses. Rings are shown but not edited
  here: the game keeps their tiers in a way that does not survive a live edit.
- ⚠️ There is no undo on this tab.

**Items tab**
- The hero's nine slots (helmet, armor, weapon, off-hand, two rings, three artifacts) and the
  bag grouped by kind.
- Click a slot or a bag item — two side windows open: kinds of items, then the items of that
  kind from weakest to strongest. Pick one and it is equipped; the old one goes to the bag,
  or drops next to the hero if the bag is full.
- **Add an item** — any item of the game into the bag. Gear and rings come **legendary**,
  their effects are rolled by the game itself. Items are created by the game's own functions,
  the same ones it uses for loot, so they are no different from dropped ones.
- **Bag size** slider — up to 100 slots.
- **delete** next to every bag item, with **Restore deleted** while the window is open.

**Language.** The mod follows the game: Russian if the game is in Russian, English for any
other language. You can pick one by hand in *Cheats → Window → Mod language*. Names of heroes,
items, effects and artifacts always come from the game's own translation.

## Installation

Find the game folder: in Steam, right-click Pocket Rogues → *Manage* → *Browse local files*.
It is the folder with `Pocket Rogues.exe`.

**Full package** (`PocketRoguesCheats-<version>-full.zip`) — if you do not have BepInEx yet.
Extract everything into the game folder, so that `winhttp.dll` and the `BepInEx` folder sit
next to `Pocket Rogues.exe`. Start the game and press **Insert**.

**Mod only** (`PocketRoguesCheats-<version>-mod-only.zip`) — if BepInEx 5 (x64) is already
installed. Extract into the game folder: the plugin goes to `BepInEx\plugins\`.

The first start with BepInEx takes a little longer: the loader prepares its files once.

## Turning it off and removing it

- To play without mods for a while: rename `winhttp.dll` in the game folder (for example to
  `winhttp.dll.off`). Rename it back to turn mods on.
- To remove the mod only: delete `BepInEx\plugins\PocketRoguesCheats.dll`.
- To remove everything: delete `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`,
  `changelog.txt` and the `BepInEx` folder from the game folder. The game's own files are
  never changed.

## Good to know

- Settings are kept in `BepInEx\config\pocketrogues.cheats.cfg`: hotkeys, pause while the
  window is open, window size and scale, the values of every cheat.
- The game sends a few totals to its developer's server about once an hour: gold, crystals,
  bought crystals, the sums of skill points and attributes, number of runs, depth record and
  guild level. Gold earned with the multiplier and numbers changed on the Editor tab end up
  in those totals. The developers can ban an account and remove items. Use at your own risk.
- The mod never touches crystals.
- An off-hand item of another class (for example a shield on a wizard) cannot be taken off
  with the button. Drop it from the inventory — it leaves the hand — and pick it up again: it
  lies in the bag like any other item. Weapons of other classes in the main hand work
  normally.
- Some tier 2 and 3 items are not offered: the game saves them under another item's name, and
  after loading they would turn into a different item or vanish.
- A bag larger than the Warehouse allows: if you turn the mod off, the extra items are not
  lost, the game just does not show them until the mod is back.
- Made for game version 1.38.3.1 with BepInEx 5.4.23.5 (x64). After a game update some
  features may stop working; the log `BepInEx\LogOutput.log` says which part failed, the rest
  keeps working.

## Building from source

Needs Windows with .NET Framework 4 (its C# compiler ships with Windows), the game and
BepInEx 5 installed into the game folder. Run `build.cmd`: it builds `bin\PocketRoguesCheats.dll`
and copies it into `BepInEx\plugins`. If the game is not in the default Steam folder, create
`build.local.cmd` next to it with `set "GAME=..."` (and `set "LOADER=..."` if BepInEx is
elsewhere) — see the top of `build.cmd`.

`package.ps1` builds both release archives from the built plugin and the original BepInEx
archive (it checks the archive's SHA-256 first).

## License

The mod: MIT, see [LICENSE](LICENSE). The full package also contains BepInEx 5.4.23.5
(<https://github.com/BepInEx/BepInEx>), unmodified, with its own components; they are under
their own licenses.
