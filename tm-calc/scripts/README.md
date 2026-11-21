# The screen: everything Godot draws and every click

These scripts draw a position from Core and turn clicks into moves. No rule of the game is decided here: a click
builds a move, `Rules.Apply` (Core) says what happens, and the screen redraws from the new position.

All files are in the namespace `TMCalc`. Folders only organise them.

| Folder | What it holds |
|---|---|
| `Main/` | The one script on the main scene, written across many files: start-up, the draw loop, every click |
| `Board/` | The map and what sits on the shared board: hexes, glows, the colour wheel, cult tracks, priest zones |
| `PlayerPanel/` | One player's panel: building board, power bowls, the player button, the score badge |
| `Tiles/` | Favour, key, pass and round tiles, power actions, and the action token on them |
| `TopPanel/` | The bar across the top: message line, log, keys, the rules slider, the bookmark map, the notes window, end-of-game scores |
| `Parts/` | Building blocks used everywhere: icon-and-number bundles, highlights, the gathering light, layout helpers |
| `Art/` | Pictures drawn in code: icons, pieces, hexes, faction boards, special landscapes |

## Main/

`Main` is one class written across these files. `Main.cs` is the place to start.

| File | Holds |
|---|---|
| `Main.cs` | Fields, start-up (`_Ready`), sending a move (`Submit`), the draw loop (`Render`), the top-line instruction |
| `MainSettings.cs` | Every setting shown in the Inspector, in the order and groups shown there |
| `MainInput.cs` | Mouse presses, hovering, dragging pieces and workers, inspecting another player's panel |
| `MainPieces.cs` | Where panels and pieces go, and sliding them there |
| `MainHexes.cs` | Clicking a hex: the colour wheel, transform costs, the build offer after spades |
| `MainTiles.cs` | Favour, round and pass tiles: setup screens, drawing, clicking |
| `MainBonusActions.cs`, `MainUpgrades.cs`, `MainPower.cs`, `MainTownsAndPower.cs`, `MainCults.cs` | One part of play each |
| `MainGameSelect.cs`, `MainFactions.cs`, `MainMaps.cs`, `MainCustomMap.cs` | The setup screens, and the custom map editor |
| `MainTopPanel.cs`, `MainStepButton.cs`, `MainHistory.cs`, `MainSaveLoad.cs` | The top bar, the game button, undo and bookmarks, saving |
| `MainNotes.cs` | The notes window: every line of play, with bookmarks and jumps |
| `MainBgaImport.cs` | Loading a game from Board Game Arena, and the bookmarklet's message box |
| `MainEndgame.cs` | The end-of-game scores and statistics |
| `MainModes.cs`, `MainNeutral.cs`, `MainCalculatorGain.cs` | Play modes and setting up a position; Calculator's neutral opponent and +1 bar |
| `MainSafeEditor.cs` | Setting up a game in progress: filling the Treasurers' Safe |
| `MainPoints.cs`, `MainScorePreview.cs`, `MainBackground.cs` | Victory point lights, the score preview, the background |
| `MainFireAndIce.cs`, `MainLandscapes.cs` | The two expansions |
| `MainFanFactions.cs` | Fan factions: books and stronghold octagons |
| `MainFanFactionsOnMap.cs` | Fan factions that act on the map: lava, the Wyrm's tokens, Selkies, Snow Shamans, pyro, Atlanteans |
| `MainFanFactionsEmberGeologistsChangelings.cs` | The lava flow, the Geologists' wheel and rocks, the Changelings |

## Settings shown in the Inspector

Names follow one rule per kind of value: a duration ends in `Seconds`, a colour in `Colour`, a transparency in
`Opacity`, a multiplier in `Scale`, a nudge from an anchor in `Offset`, a draw order in `ZIndex`. Every setting is in
a group, and each class declares its settings together at the top.

Godot saves Inspector values in the scene files by name. Renaming a setting in code without renaming it in the
scenes resets it to its default, silently.

## Moving or renaming a script

Move its `.uid` file with it, and update its `res://scripts/...` path in any scene that uses it.
