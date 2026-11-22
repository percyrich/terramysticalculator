# BgaImport: a Board Game Arena game → a .tmcalc save

The command-line converter. It reads a Terra Mystica game exported from Board Game Arena (BGA) and replays it through
Core's rules, one BGA notification at a time, writing a save the app can load: the played moves marked as the game
line, in Strict mode, at the log's last move.

The converter itself is the library [`../BgaConvert`](../BgaConvert), shared with the app (bookmark map: **BGA
Bookmarklet** and **Load BGA JSON**). This project is only the console program around it (`Program.cs`), plus the
bookmarklet's source.

## Running it

From `godot\`:

| Command | Converts |
|---|---|
| `run-bga-import.bat` | The sample game, `BgaConvert\samples\EP1-333098289.json` |
| `run-bga-import.bat bga-<table>.json` | One game exported with the bookmarklet |
| `run-bga-import.bat log.json gamedatas.json` | One game given as two separate files |
| `run-bga-import-all.bat <folder>` | Every `.json` file in a folder (`run-bga-import-all.bat BgaConvert\samples` runs the whole test set) |

The scripts write to `BgaImport\out\`, and copy the console output to `godot\bga-import-results.txt`.

By hand:

```
dotnet run --project BgaImport -- <game.json | log.json | folder> [<gamedatas.json> | ""] [<output folder>]
```

Without an output folder, the files go beside the input.

## Output

| File | What it is |
|---|---|
| `bga-<table>.tmcalc` | The save. Load it in the app (Ctrl+O). |
| `bga-<table>-report.txt` | The setup read from BGA, every problem and every difference from BGA's counts, faction versions that differ from Core's, and the final scores side by side. |
| `bga-<table>-trace.txt` | Every Core move made, with its log line, marked with BGA's move number. |
| `summary.txt` | Folder runs only: one line a game (`OK` or `STOP`, with its summary), then how many converted to the end. |

Each save is loaded back straight after it is written. A save that doesn't load cleanly is reported.

Exit codes:

| Code | Meaning |
|---|---|
| 0 | The whole game converted (folder: every game) |
| 2 | The files couldn't be read |
| 3 | Stopped part-way (folder: at least one game). The report says where and why; the save holds everything up to there. |
| 4 | The converter crashed |

## Inputs

**One file from the bookmarklet** (the usual way): `{ "format": "bga-tm", "table": …, "logs": [ … ], "gamedatas": { … } }`.
See the main [README](../README.md#importing-games-from-board-game-arena) for installing and using it.

The bookmarklet's source is [`bookmarklet.js`](bookmarklet.js). The one-line version to paste as a bookmark's URL is
[`bookmarklet.min.txt`](bookmarklet.min.txt); the app embeds the same file.

**Or two files** from the logged-in replay page of one table:

1. **The move log:** the `logs` array of `logs.html?table=<id>&translated=true`, or the whole response.
2. **The game data:** `gameui.gamedatas` at the start of the replay (round tiles, bonus cards, the board, BGA's type
   tables).

Both finished games and games in progress work. A game in progress converts to its latest move, and the summary says
whose turn it is.

## How it converts

1. **Setup** from the game data and the first packets:
   - the options;
   - the factions and turn order;
   - auction bids, as starting VP;
   - the map, compared hex by hex with Core's maps; a board matching none of them becomes a custom map;
   - the round tiles and pass tiles;
   - the faction-specific setup: the Ice Maidens' favour, starting terrains, the Changelings' three terrains, the
     Atlanteans' starting town, the Djinni's starting steps, the Archivists' extra bonus card.
2. **Then each notification in order** becomes the equivalent Core move or moves:
   - builds, transforms, upgrades, priests, favours, towns, bridges;
   - power actions (Chash Dallah's paid in coins too);
   - passing and bonus cards;
   - the bonus cards' and favours' actions;
   - every faction's stronghold action and book action, the fan factions' included;
   - conversions and burns;
   - the special landscapes and what they offer.

   Turns end (`EndTurn`) when BGA moves on to someone else; spades left unused are given up.
3. **Conversions:** BGA gives totals, which are split into Core's single conversions. A conversion BGA logs in the
   middle of an action is made before the action instead: the action is undone, the conversion made, and the action
   replayed.
4. **Power offered to neighbours** is answered with what BGA recorded for that player and hex, a few packets further
   on: BGA asks everyone at once, Core one at a time.
5. **What Core does by itself** (income, cult rewards, VP, town bonuses, final scoring) is not copied but checked.
   After every BGA packet, each player's coins, workers, priests in hand, power bowls, free spades, VP and cult steps
   are compared with BGA's counts. A difference is reported when it appears, and again whenever it changes.
6. **Faction versions:** BGA's material for each faction in the game is compared with Core's, value by value
   (`MaterialCheck.cs`). Differences are listed in the report, not applied.
7. **A move Core refuses** stops the conversion there.

BGA's numbering of round tiles, bonus cards, favours, town tiles, power actions and priest spaces is translated in
[`../BgaConvert/Tables.cs`](../BgaConvert/Tables.cs).

## Where it stands

The test set is the 188 games in [`../BgaConvert/samples`](../BgaConvert/samples), covering every faction and
expansion. At the last full run:

- **179** converted to the end;
- **176** of those finished with every final score equal to BGA's.

The rest are known and understood:

| Cause | Effect |
|---|---|
| Older versions of the Goblins, Enlightened and Chash Dallah, which BGA has since changed | Reported by the faction-version check; Core plays the current version, so these games stop or differ |
| Older tables that count the Mermaids' Coral Reef as an ordinary structure in towns | Stop: the Coral Reef ruling is deliberately kept |
| One BGA scoring quirk (Fakirs, table 250486323) | Final score differs by 2 VP |
| Alchemists converting while neighbours are still answering their stronghold's power offers (table 241070597) | Stop, accepted: Core waits for the offers first |

Not yet seen in any BGA log, so how BGA records them is unknown: the Edges final scoring tile,
the Yetis' Frozen Peak, and the Witches' Raven's Blunder taken as three cult steps. A notification type the converter
doesn't know is reported as "not converted yet" the first time it appears.

## Citations in the code

Comments in Core, BgaConvert and the tests cite BGA games where a rule follows what BGA does, as a table number and
BGA's move number in it: "as on BGA (649833286 #342)". Nearly all of those tables are in
[`../BgaConvert/samples`](../BgaConvert/samples) (the file named after it); the move number is the `move_id` of its log
packets. Any table can also be opened on BGA at `boardgamearena.com/table?table=<number>`.
