namespace Core;

// A player's power: tokens sitting in three bowls. Immutable, like everything in the state;
// every operation returns a new Power and leaves this one untouched.
//
// The rules of the three bowls:
//   Gain:  each point of power moves one token forward: from bowl 1 to bowl 2 while bowl 1 has any,
//          otherwise from bowl 2 to bowl 3. With nothing left to move, the rest of the gain is lost.
//   Spend: power is spent from bowl 3 only; spent tokens go back to bowl 1.
//   Burn:  remove one token from bowl 2 from the game, to move another from bowl 2 to bowl 3.
public readonly record struct Power(int Bowl1, int Bowl2, int Bowl3)
{
    public int Total => Bowl1 + Bowl2 + Bowl3;

    public bool CanSpend(int amount) => amount >= 0 && Bowl3 >= amount;
    public bool CanBurn(int times) => times >= 0 && Bowl2 >= 2 * times;

    // The most power that can still be gained before everything is in bowl 3.
    public int GainCapacity => 2 * Bowl1 + Bowl2;

    // Returns the new Power; "moved" says how many points actually did something (the rest were lost).
    public Power Gain(int amount, out int moved)
    {
        var (bowl1, bowl2, bowl3) = (Bowl1, Bowl2, Bowl3);
        moved = 0;
        for (var point = 0; point < amount; point++)
        {
            if (bowl1 > 0) { bowl1--; bowl2++; }
            else if (bowl2 > 0) { bowl2--; bowl3++; }
            else break; // everything is already in bowl 3
            moved++;
        }
        return new Power(bowl1, bowl2, bowl3);
    }

    public Power Gain(int amount) => Gain(amount, out _);

    public Power Spend(int amount)
    {
        if (!CanSpend(amount))
            throw new InvalidOperationException($"Cannot spend {amount} power with {Bowl3} in bowl 3.");
        return new Power(Bowl1 + amount, Bowl2, Bowl3 - amount);
    }

    // One burn: a token from bowl 2 leaves the game and another moves on to bowl 3; for the Children of the Wyrm
    // (doubleMove) up to two move on. Needs at least 2 in bowl 2.
    public Power BurnOnce(bool doubleMove)
    {
        if (Bowl2 < 2)
            throw new InvalidOperationException($"Cannot burn with {Bowl2} in bowl 2.");
        var moved = doubleMove ? Math.Min(2, Bowl2 - 1) : 1;
        return new Power(Bowl1, Bowl2 - 1 - moved, Bowl3 + moved);
    }

    // How many burns put at least this much in bowl 3 (0 if it is there already); null if burning can't.
    public int? BurnsFor(int cost, bool doubleMove)
    {
        var power = this;
        var burns = 0;
        while (power.Bowl3 < cost)
        {
            if (power.Bowl2 < 2)
                return null;
            power = power.BurnOnce(doubleMove);
            burns++;
        }
        return burns;
    }

    // BurnOnce, `burns` times over.
    public Power BurnTimes(int burns, bool doubleMove)
    {
        var power = this;
        for (var i = 0; i < burns; i++)
            power = power.BurnOnce(doubleMove);
        return power;
    }

    // Tokens leaving the game for good, from the lowest bowls first (Dragonlords, Shapeshifters). Fewer if there
    // aren't that many.
    public Power RemoveTokens(int count)
    {
        var (bowl1, bowl2, bowl3) = (Bowl1, Bowl2, Bowl3);
        var fromOne = Math.Min(count, bowl1);
        var fromTwo = Math.Min(count - fromOne, bowl2);
        var fromThree = Math.Min(count - fromOne - fromTwo, bowl3);
        return new Power(bowl1 - fromOne, bowl2 - fromTwo, bowl3 - fromThree);
    }

    // New tokens entering the game (Dragonlords' spades and stronghold, the Shapeshifters' bowl 3 token).
    public Power AddTokens(int bowl, int amount) => bowl switch
    {
        1 => this with { Bowl1 = Bowl1 + amount },
        2 => this with { Bowl2 = Bowl2 + amount },
        3 => this with { Bowl3 = Bowl3 + amount },
        _ => throw new ArgumentOutOfRangeException(nameof(bowl)),
    };

    // Every token's bowl, most valuable first: all the 3s, then the 2s, then the 1s.
    // The view lays tokens out in this order, so "the first token" and "the fifth token" have a fixed meaning.
    public ValueList<int> TokenBowls => new(
        Enumerable.Repeat(3, Bowl3).Concat(Enumerable.Repeat(2, Bowl2)).Concat(Enumerable.Repeat(1, Bowl1)));

    public override string ToString() => $"{Bowl1}/{Bowl2}/{Bowl3}";
}
