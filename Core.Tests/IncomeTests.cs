using Core;
using static Core.Tests.TestHelpers;

namespace Core.Tests;

public class IncomeTests
{
    // Worker income: 1 from the faction board, plus 1 under every emptied dwelling slot except the last.
    // The Swarmlings' board gives 2; the Engineers' gives none, their third and sixth slots nothing, but their last 1.
    [Theory]
    [InlineData(Faction.Giants,     new[] { 1, 2, 3, 4, 5, 6, 7, 8, 8 })]
    [InlineData(Faction.Mermaids,   new[] { 1, 2, 3, 4, 5, 6, 7, 8, 8 })]
    [InlineData(Faction.Swarmlings, new[] { 2, 3, 4, 5, 6, 7, 8, 9, 9 })]
    [InlineData(Faction.Engineers,  new[] { 0, 1, 2, 2, 3, 4, 4, 5, 6 })]
    public void Worker_income_after_building_each_dwelling(Faction faction, int[] expectedAfterBuilds)
    {
        var player = PlayerState.New(faction);
        for (var built = 0; built <= Config.DwellingsPerPlayer; built++)
        {
            Assert.Equal(expectedAfterBuilds[built], player.Income.Workers);
            Assert.Equal(0, player.Income.Coins); // neither the board nor dwellings give coins
            if (built < Config.DwellingsPerPlayer)
                player = player with { DwellingBank = player.DwellingBank.SetItem(built, null) };
        }
    }

    // Coins and power after building 0, 1, 2, 3 and 4 trading posts (on top of the base income).
    [Theory]
    [InlineData(Faction.Giants,     new[] { 0, 2, 4, 6, 8 },  new[] { 0, 1, 2, 4, 6 })]
    [InlineData(Faction.Nomads,     new[] { 0, 2, 4, 7, 11 }, new[] { 0, 1, 2, 3, 4 })]
    [InlineData(Faction.Alchemists, new[] { 0, 2, 4, 7, 11 }, new[] { 0, 1, 2, 3, 4 })]
    [InlineData(Faction.Dwarves,    new[] { 0, 3, 5, 7, 10 }, new[] { 0, 1, 2, 4, 6 })]
    [InlineData(Faction.Swarmlings, new[] { 0, 2, 4, 6, 9 },  new[] { 0, 2, 4, 6, 8 })]
    public void Trading_post_slots_give_coins_and_power(Faction faction, int[] coins, int[] power)
    {
        var player = PlayerState.New(faction);
        var workers = Factions.BaseIncome(faction).Workers;
        for (var built = 0; built <= Config.TradingPostsPerPlayer; built++)
        {
            Assert.Equal(new Income(coins[built], workers, 0, power[built]), player.Income);
            if (built < Config.TradingPostsPerPlayer)
                player = player with { TradingPostBank = player.TradingPostBank.SetItem(built, null) };
        }
    }

    [Fact]
    public void The_Engineers_second_temple_gives_five_power()
    {
        Assert.Equal(new Income(0, 0, 1, 0), Factions.TempleSlotIncome(Faction.Engineers)[0]);
        Assert.Equal(new Income(0, 0, 0, 5), Factions.TempleSlotIncome(Faction.Engineers)[1]);
        Assert.Equal(new Income(0, 0, 1, 0), Factions.TempleSlotIncome(Faction.Engineers)[2]);
    }

    // What emptying each faction's stronghold slot gives every round.
    [Theory]
    [InlineData(Faction.Auren, 0, 0, 0, 2)]
    [InlineData(Faction.Witches, 0, 0, 0, 2)]
    [InlineData(Faction.Giants, 0, 0, 0, 4)]
    [InlineData(Faction.ChaosMagicians, 0, 2, 0, 0)]
    [InlineData(Faction.Nomads, 0, 0, 0, 2)]
    [InlineData(Faction.Fakirs, 0, 0, 1, 0)]
    [InlineData(Faction.Halflings, 0, 0, 0, 2)]
    [InlineData(Faction.Cultists, 0, 0, 0, 2)]
    [InlineData(Faction.Alchemists, 6, 0, 0, 0)]
    [InlineData(Faction.Darklings, 0, 0, 0, 2)]
    [InlineData(Faction.Mermaids, 0, 0, 0, 4)]
    [InlineData(Faction.Swarmlings, 0, 0, 0, 4)]
    [InlineData(Faction.Dwarves, 0, 0, 0, 2)]
    [InlineData(Faction.Engineers, 0, 0, 0, 2)]
    public void Stronghold_income_varies_by_faction(Faction faction, int coins, int workers, int priests, int power)
    {
        Assert.Equal(new Income(coins, workers, priests, power), Assert.Single(Factions.StrongholdSlotIncome(faction)));
    }

    [Theory]
    [InlineData(Faction.Giants, 1)]
    [InlineData(Faction.Darklings, 2)]
    [InlineData(Faction.Swarmlings, 2)]
    public void Sanctuaries_give_priests(Faction faction, int priests)
    {
        Assert.Equal(new Income(0, 0, priests, 0), Assert.Single(Factions.SanctuarySlotIncome(faction)));
    }

    [Fact]
    public void Base_income_is_one_worker()
    {
        Assert.Equal(new Income(0, 1, 0, 0), PlayerState.New(Faction.Witches).Income);
    }

    [Fact]
    public void Income_is_paid_at_the_start_of_each_round()
    {
        // Round 1: 15 coins, 6 workers. Build next to hex 5 (2 coins; 1 worker + 2 spades of 3): 13 coins, -1 workers.
        // Worker income is now 4 (1 base + 3 slots). End, pass, end: round 2 pays 4 workers.
        var s = Play(Concat(SoloStart, new[] { Build(G, 2, 4), End(G) }, PassAndEnd(G)));
        Assert.Equal(2, s.Round);
        Assert.Equal(13, s[G].Coins);
        Assert.Equal(3, s[G].Workers);
    }
}
