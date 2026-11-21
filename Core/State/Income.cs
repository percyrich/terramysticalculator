namespace Core;

// What a player receives at the start of each round. VictoryPoints: only the Firewalkers' second temple.
public readonly record struct Income(int Coins, int Workers, int Priests, int Power, int VictoryPoints = 0)
{
    public static Income operator +(Income a, Income b) =>
        new(a.Coins + b.Coins, a.Workers + b.Workers, a.Priests + b.Priests, a.Power + b.Power, a.VictoryPoints + b.VictoryPoints);

    public static Income operator *(Income income, int times) =>
        new(income.Coins * times, income.Workers * times, income.Priests * times, income.Power * times, income.VictoryPoints * times);

    // e.g. "3 coins, 1 worker, 1 power"; "nothing" when every part is 0.
    public override string ToString()
    {
        var parts = new List<string>();
        if (Coins != 0) parts.Add($"{Coins} coin{(Coins == 1 ? "" : "s")}");
        if (Workers != 0) parts.Add($"{Workers} worker{(Workers == 1 ? "" : "s")}");
        if (Priests != 0) parts.Add($"{Priests} priest{(Priests == 1 ? "" : "s")}");
        if (Power != 0) parts.Add($"{Power} power");
        if (VictoryPoints != 0) parts.Add($"{VictoryPoints} VP");
        return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
    }
}
