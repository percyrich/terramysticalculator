# Core: the rules of the game, with no screen attached

Core knows nothing about Godot. It answers one question: given a position and a move, what is the new position?
The Godot project (`tm-calc`) draws positions and sends moves; the tests (`Core.Tests`) check the answers.

Folders only organise the files. Every file is in the same namespace (`Core`), so moving a file between folders
never breaks anything.

## The five folders

| Folder | What it holds | Changes when |
|---|---|---|
| `Data/` | What is printed on the components: numbers, faction boards, tiles, maps | a number or a component changes |
| `State/` | What a position is: everything on the table at one instant | something new has to be remembered |
| `Moves/` | What a player can do, and what the rules report back | a new kind of move or animation is needed |
| `Rules/` | What each move does to a position | a rule changes |
| `Record/` | The game as a whole: history, saving, the log, final scores | undo, save files or the log change |

## Data/

| File | Contents |
|---|---|
| `Config.cs` | Every fixed number in the rules (costs, limits, faction ability values) |
| `Enums.cs` | The lists of names: factions, terrains, phases, bonus actions, play modes |
| `Factions.cs` | Each faction's board: income, costs, starting values, stronghold action |
| `Buildings.cs` | Facts about building kinds: how many, what upgrades to what |
| `Favours.cs`, `KeyTiles.cs`, `PassTiles.cs`, `RoundTiles.cs`, `PowerActions.cs`, `BonusActions.cs` | One file per kind of tile or action space |
| `Landscapes.cs` | The special landscapes: names, prerequisites, numbers |
| `Terrains.cs` | The terrain wheel and spade distances |
| `Maps.cs`, `MapLayout.cs` | The maps, and the hex geometry (rows, neighbours) |

## State/

| File | Contents |
|---|---|
| `GameState.cs` | The whole position. Nothing else holds game data |
| `PlayerState.cs` | Everything belonging to one player |
| `GameStateParts.cs` | The small records a GameState is made of: bridges, placed keys, power offers, the neutral opponent |
| `GameOptions.cs` | What a game includes (expansions, starting mode) |
| `Power.cs` | The three power bowls |
| `Piece.cs`, `Cost.cs`, `Income.cs` | One piece; a price; an income |
| `ValueList.cs` | The list type used everywhere (two lists with the same contents are equal) |

## Moves/

| File | Contents |
|---|---|
| `Moves.cs` | Every move, grouped by when it can be made |
| `MoveResult.cs` | What comes back: the new position, events, and issues (notes, warnings, errors) |
| `GameEvents.cs` | What happened, for animations and the game log. The rules never read events |
| `Destinations.cs` | Places a piece can go, for the "where can this go?" questions |

## Rules/

`Rules` is one class written across these files, one file per topic. A method in one file can call a method in any
other directly.

| File | Contents |
|---|---|
| `Rules.cs` | Start here. Every move enters at `Apply`; `ApplyMove` is the table sending each move to its method |
| `Rules.Checks.cs` | The checks most moves begin with: whose turn, one action a turn, nothing left unfinished |
| `Rules.Queries.cs` | What the screen asks: where a piece can go, what something costs, what is in range |
| `Rules.GameSetup.cs` | Before round 1: game, factions, turn order, map, tiles, starting dwellings |
| `Rules.Building.cs` | Dwellings, upgrades, strongholds, shipping and spade upgrades |
| `Rules.Terrain.cs` | Transforming, spades, Volcano |
| `Rules.Cults.cs` | Priests, the four tracks, favours |
| `Rules.Power.cs` | Gaining power, power offered by neighbours' builds, power actions |
| `Rules.FreeActions.cs` | Conversions, burning, and choices a faction owes |
| `Rules.Towns.cs` | Adjacency, bridges, towns |
| `Rules.BonusActions.cs` | The octagons: stronghold, pass tile and favour actions |
| `Rules.Rounds.cs` | Ending a turn, passing, pass tiles, round end, round start and income |
| `Rules.Modes.cs` | Strict, Lenient and Free play; the turn-order step |
| `Rules.Calculator.cs` | Calculator: the neutral opponent and the +1 panel |
| `Rules.EditPosition.cs` | Setting up a game in progress: the Edit moves, which set values directly |
| `Rules.PositionChecks.cs` | What a set-up position must satisfy before play goes on |
| `Rules.Landscapes.cs` | The Special Landscapes expansion |
| `Rules.FanFactions.cs` | The fan factions, one section per faction |

## Record/

| File | Contents |
|---|---|
| `History.cs` | The tree of positions: undo, redo, bookmarks, side lines |
| `SaveGame.cs` | Save files (the moves are saved; positions are replayed from them) |
| `GameLog.cs` | One line of text per move |
| `EndGame.cs`, `FinalScoring.cs` | Final scores, and Fire & Ice's final scoring tiles |
| `GameStats.cs` | The end-of-game statistics tables |
| `MapFile.cs` | Custom map files (`.tmmap`) |

## How a move travels

1. The screen builds a move (`Moves.cs`) and calls `Rules.Apply(state, move)`.
2. `Apply` hands it to `ApplyMove`, which sends it to its own method, named `Apply` + the move's name
   (`BuildDwelling` goes to `ApplyBuildDwelling`).
3. That method checks the move, then returns a `MoveResult`: a new position, or the old one with an error.
4. Back in `Apply`, the rules that follow every move run: debt warnings, Strict mode, towns made formable.
5. `History` stores the result; the screen redraws from the new position.

## Where to look

| To change | Go to |
|---|---|
| A cost, limit or reward | `Data/Config.cs`, or `Data/Factions.cs` if it is on a faction board |
| What a move does | Find `Apply` + its name in `Rules/` |
| Why a move is refused | Search `Rules/` for the wording of the message |
| A faction's special ability | Search `Rules/` for the faction's name; fan factions have their own sections in `Rules.FanFactions.cs` |
| A line of the game log | `Record/GameLog.cs` |
| Something new to remember | Add it to `State/GameState.cs` or `State/PlayerState.cs` |

## Reading the comments

Where the printed rules leave something open, Core plays it as Board Game Arena does. Comments mark these "as on BGA",
often with the game that shows it: "(as on BGA: 608085870 #326)" is BGA table 608085870, move 326. Nearly every table cited
is in `BgaConvert/samples` (the file named after it), so the move can be replayed and checked; any table can also be
opened on BGA at `boardgamearena.com/table?table=<number>`.

## Known tangle

Published factions' abilities are written into the shared rules where they act, marked with the faction's name, not
gathered in one place per faction. Searching `Rules/` for a faction's name finds every place.
