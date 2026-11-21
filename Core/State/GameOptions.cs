namespace Core;

// What a game includes, chosen with the game (ChooseGame); everything by default.
//   VariableTurnOrder: the pass order becomes the next round's turn order. Without it the turn order stays, the first
//     player moving to the end each round (the second becomes first).
//   MiniExpansion: the shipping, 11 VP and 2 VP + 2 steps on every track key tokens, the last round tile (VP per
//     temple) and the last pass tile (VP per shipping level) (KeyTileDefinition, RoundTileDefinition and
//     PassTileDefinition.MiniExpansion).
//   FireAndIce: the ice, volcano and variable factions.
//   FinalScoring: Fire & Ice's final scoring tile scored at the end (FinalScoring), if one was chosen (Fire & Ice only).
//   StartingMode: the play mode the game starts in (GameState.Mode); later changes are SetMode moves.
//   SetUpInProgress: a game in progress is recorded (Phase.SettingUp) instead of starting from the beginning; the
//     volcano factions' starting terrain and the turn order are not chosen.
//   Calculator: one faction against a neutral opponent (GameState.Neutral): no turn order; round tiles may be left
//     out (those rounds blank); pass tiles: none chosen puts all in, else at least the usual number; always Free.
//     Without it a game needs at least two factions and exactly the usual number of pass tiles.
//   CalculatorStrict (with Calculator): debt and the Firewalkers' pyro rule are refused as in Strict mode, and the +1
//     panel gives only power; Free play's other freedoms stay (several actions a turn, no power offers).
public sealed record GameOptions(bool VariableTurnOrder = true, bool MiniExpansion = true, bool FireAndIce = true,
    FinalScoringTile? FinalScoring = null, PlayMode StartingMode = PlayMode.Lenient, bool SetUpInProgress = false,
    bool Calculator = false, bool Landscapes = true, bool FanFactions = false, bool CalculatorStrict = false)
{
    // Everything but the fan factions (they can't be played with the special landscapes: GameOptions.FanFactions).
    public static readonly GameOptions All = new();
}
