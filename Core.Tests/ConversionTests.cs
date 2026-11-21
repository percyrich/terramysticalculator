using Core;
using static Core.Tests.TestHelpers;

namespace Core.Tests;

// Power to worker, priest to worker, worker to coin. All free actions.
public class ConversionTests
{
    [Fact]
    public void Three_power_from_bowl_3_buys_a_worker()
    {
        var result = Rules.Apply(Charged(Play(SoloStart)), new ConvertPowerToWorker(G)); // 0/0/12, 6 workers

        Assert.True(result.Accepted);
        Assert.Equal(7, result.State[G].Workers);
        Assert.Equal(new Power(3, 0, 9), result.State[G].Power);
    }

    [Fact]
    public void A_worker_needs_three_in_bowl_3()
    {
        var s = Play(SoloStart);
        s = s.WithPlayer(s[G] with { Power = new Power(6, 4, 2) });
        Assert.False(Rules.Apply(s, new ConvertPowerToWorker(G)).Accepted);
    }

    [Fact]
    public void A_ready_priest_becomes_a_worker_and_goes_back_to_the_supply()
    {
        var s = PlayCharged(SoloStart, GetPriest(G));
        var result = Rules.Apply(s, new ConvertPriestToWorker(G));

        Assert.True(result.Accepted);
        var after = result.State[G];
        Assert.Empty(after.Priests);
        Assert.Equal(new Piece(G, PieceKind.Priest, 0), after.PriestSupply[0]);
        Assert.Equal(Config.PriestsPerPlayer, after.PriestSupply.Count);
        Assert.Equal(7, after.Workers);
    }

    [Fact]
    public void Without_a_ready_priest_there_is_nothing_to_convert()
    {
        var result = Rules.Apply(Play(SoloStart), new ConvertPriestToWorker(G));
        Assert.False(result.Accepted);
    }

    [Fact]
    public void A_worker_becomes_a_coin()
    {
        var result = Rules.Apply(Play(SoloStart), new ConvertWorkerToCoin(G)); // 2 coins, 6 workers

        Assert.True(result.Accepted);
        Assert.Empty(result.Issues);
        Assert.Equal(5, result.State[G].Workers);
        Assert.Equal(16, result.State[G].Coins);
    }

    [Fact]
    public void Converting_a_worker_you_do_not_have_goes_into_debt()
    {
        var s = Play(SoloStart);
        s = s.WithPlayer(s[G] with { Workers = 0 });
        var result = Rules.Apply(s, new ConvertWorkerToCoin(G));

        Assert.True(result.Accepted);
        Assert.Equal(-1, result.State[G].Workers);
        Assert.Equal("The Giants are in debt: -1 workers.", Assert.Single(result.Issues).Message);
    }

    [Fact]
    public void Conversions_are_free_actions_on_your_own_turn_only()
    {
        var s = PlayCharged(SoloStart, GetPriest(G),
            new ConvertPowerToWorker(G), new ConvertPriestToWorker(G), new ConvertWorkerToCoin(G));
        Assert.False(s.ActionTaken);

        var witchesTurn = Charged(Play(TwoPlayerStart));
        Assert.False(Rules.Apply(witchesTurn, new ConvertWorkerToCoin(G)).Accepted);
        Assert.False(Rules.Apply(witchesTurn, new ConvertPowerToWorker(G)).Accepted);
    }
}
