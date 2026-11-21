namespace Core;

// What something costs. Paying may leave a player in debt (negative coins or workers): that is allowed
// and flagged with a warning, never refused. Priests are pieces, so they are paid one at a time by the
// rules (Rules.PayPriest), never by PlayerState.Pay; only the Darklings' spades cost priests.
public readonly record struct Cost(int Coins, int Workers, int Priests = 0)
{
    public static Cost operator +(Cost a, Cost b) => new(a.Coins + b.Coins, a.Workers + b.Workers, a.Priests + b.Priests);

    public static Cost operator *(Cost cost, int times) => new(cost.Coins * times, cost.Workers * times, cost.Priests * times);

    // e.g. "3 coins, 2 workers"; "0 coins" when it is free.
    public override string ToString()
    {
        var parts = new List<string>();
        if (Coins != 0) parts.Add($"{Coins} coin{(Coins == 1 ? "" : "s")}");
        if (Workers != 0) parts.Add($"{Workers} worker{(Workers == 1 ? "" : "s")}");
        if (Priests != 0) parts.Add($"{Priests} priest{(Priests == 1 ? "" : "s")}");
        return parts.Count == 0 ? "0 coins" : string.Join(", ", parts);
    }
}
