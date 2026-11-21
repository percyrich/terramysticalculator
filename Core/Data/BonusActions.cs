namespace Core;

// Facts about the bonus actions (the BonusAction enum, in Enums.cs).
public static class BonusActions
{
    // Free spades a bonus action gives; 0 for the others.
    public static int Spades(BonusAction action) => action switch
    {
        BonusAction.OneSpade => 1,
        BonusAction.TwoSpades => 2,
        BonusAction.SelkiesSpade => 1,
        _ => 0,
    };

    public static string Describe(BonusAction action) => action switch
    {
        BonusAction.CultStep           => "+1 on any track",
        BonusAction.OneSpade           => "1 free spade",
        BonusAction.TwoSpades          => "2 free spades",
        BonusAction.BuildOnHomeTerrain => "free dwelling on any home terrain",
        BonusAction.FreeTradingPost    => "free trading post",
        _ => action.ToString(),
    };
}
