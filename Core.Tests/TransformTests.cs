using Core;
using static Core.Tests.TestHelpers;

namespace Core.Tests;

// The usual spades: the Witches (TwoPlayerStart: on 9 and 60, 15 coins, 7 workers, to act first). Next to 9:
// 10 Lake (1 spade to Forest), 21 Swamp (2), 8 Plains (3). Wheel: Wasteland, Desert, Plains, Swamp, Lake, Forest, Mountain.
// The Giants (solo, on 5 and 112, 15 coins, 6 workers): every transform takes 2 spades. Next to 5: 4 Desert, 6 Plains, 17 Swamp.
public class TransformTests
{
    [Fact]
    public void The_wheel_counts_steps_either_way_round()
    {
        Assert.Equal(1, Terrains.Spades(Terrain.Wasteland, Terrain.Mountain)); // wraps round
        Assert.Equal(3, Terrains.Spades(Terrain.Plains, Terrain.Forest));
        Assert.Equal(3, Terrains.Spades(Terrain.Wasteland, Terrain.Swamp));
        Assert.Equal(0, Terrains.Spades(Terrain.Desert, Terrain.Desert));
        Assert.Null(Terrains.Spades(Terrain.River, Terrain.Plains));
    }

    [Fact]
    public void Transforming_costs_three_workers_per_spade_and_uses_the_action()
    {
        var result = Rules.Apply(Play(TwoPlayerStart), new TransformTerrain(W, 10, Terrain.Forest));

        Assert.True(result.Accepted);
        var s = result.State;
        Assert.Equal(Terrain.Forest, s.HexTerrain[10]);
        Assert.Null(s.Map[10]);        // nothing built
        Assert.Equal(15, s[W].Coins);
        Assert.Equal(4, s[W].Workers); // 7 - 1 spade
        Assert.True(s.ActionTaken);
        Assert.Equal(W, s.ActivePlayer); // turn continues until End turn
    }

    [Fact]
    public void A_hex_can_be_transformed_into_any_basic_terrain()
    {
        var result = Rules.Apply(Play(TwoPlayerStart), new TransformTerrain(W, 10, Terrain.Wasteland)); // Lake -> Wasteland: 3 spades

        Assert.True(result.Accepted);
        Assert.Equal(Terrain.Wasteland, result.State.HexTerrain[10]);
        Assert.Equal(-2, result.State[W].Workers);
        Assert.Equal(Severity.Warning, Assert.Single(result.Issues).Severity); // debt
    }

    [Fact]
    public void The_Giants_always_need_two_spades()
    {
        Assert.Equal(2, Factions.SpadesNeeded(G, Terrain.Desert, Terrain.Wasteland));
        Assert.Equal(2, Factions.SpadesNeeded(G, Terrain.Swamp, Terrain.Mountain));
        Assert.Equal(0, Factions.SpadesNeeded(G, Terrain.Desert, Terrain.Desert));
        Assert.Equal(1, Factions.SpadesNeeded(W, Terrain.Desert, Terrain.Wasteland));

        var nextDoor = Rules.Apply(Play(SoloStart), new TransformTerrain(G, 4, Terrain.Wasteland)); // 1 step round the wheel
        Assert.Equal(0, nextDoor.State[G].Workers); // 6 - 2 spades
        var farAway = Rules.Apply(Play(SoloStart), new TransformTerrain(G, 4, Terrain.Lake));     // 3 steps round the wheel
        Assert.Equal(0, farAway.State[G].Workers);
        Assert.Empty(farAway.Issues);
    }

    [Fact]
    public void After_transforming_the_same_turn_cannot_build_elsewhere()
    {
        var s = Ruled(Play(Concat(SoloStart, new Move[] { new TransformTerrain(G, 4, Terrain.Wasteland) })));
        Assert.False(Rules.Apply(s, Build(G, 2, 4)).Accepted); // a second action
        Assert.False(Rules.Apply(PlayFrom(s, new FinishSpades(G)), Build(G, 2, 4)).Accepted); // nor after giving up the dwelling
    }

    [Fact]
    public void Transforming_to_home_terrain_offers_a_dwelling_there()
    {
        var s = Rules.Apply(Play(TwoPlayerStart), new TransformTerrain(W, 10, Terrain.Forest)).State; // Lake: 1 spade
        Assert.True(s.SpadesPending);
        Assert.Equal(new[] { 10 }, Rules.SpadedBuildTargets(s, W));
        Assert.False(Rules.Apply(s, End(W)).Accepted); // build, or Done, first

        var dwelling = new Piece(W, PieceKind.Dwelling, 2);
        var move = Rules.MoveTo(s, dwelling, new HexDestination(10));
        Assert.Equal(new BuildOnSpadedHex(dwelling, 10), move);
        Assert.Contains((Destination)new HexDestination(10), Rules.LegalDestinations(s, dwelling).Destinations);
        var built = Rules.Apply(s, move);
        Assert.True(built.Accepted);
        Assert.Equal(dwelling, built.State.Map[10]);
        Assert.Equal(13, built.State[W].Coins);  // 15 - dwelling 2
        Assert.Equal(3, built.State[W].Workers); // 7 - 1 spade (3) - dwelling 1
        Assert.False(built.State.SpadesPending);
        Assert.True(Rules.Apply(built.State, End(W)).Accepted);

        var declined = PlayFrom(s, new FinishSpades(W));
        Assert.False(declined.SpadesPending);
        Assert.Null(declined.Map[10]);
        Assert.True(Rules.Apply(declined, End(W)).Accepted);

        var otherTerrain = Rules.Apply(Play(TwoPlayerStart), new TransformTerrain(W, 10, Terrain.Mountain)).State;
        Assert.False(otherTerrain.SpadesPending); // not home terrain: nothing to build
    }

    [Fact]
    public void A_hex_cannot_be_transformed_into_what_it_already_is()
    {
        var result = Rules.Apply(Play(SoloStart), new TransformTerrain(G, 4, Terrain.Desert));
        Assert.Equal("That hex is already Desert.", Assert.Single(result.Issues).Message);
    }

    [Fact]
    public void Transform_targets_follow_the_same_hex_rules_as_building()
    {
        var s = Play(SoloStart);
        Assert.False(Rules.Apply(s, new TransformTerrain(G, 0, Terrain.Wasteland)).Accepted);            // out of range
        Assert.False(Rules.Apply(s, new TransformTerrain(G, 18, Terrain.Wasteland)).Accepted);           // river
        Assert.False(Rules.Apply(s, new TransformTerrain(G, GiantsStartA, Terrain.Desert)).Accepted);    // occupied
    }

    [Fact]
    public void Costs_count_spades_to_home_terrain()
    {
        var s = Play(TwoPlayerStart);
        Assert.Equal(new Cost(2, 4), Rules.BuildCost(s, W, 10));  // Lake: 1 spade + dwelling
        Assert.Equal(new Cost(2, 7), Rules.BuildCost(s, W, 8));   // Wasteland: 2 spades + dwelling
        Assert.Equal(new Cost(0, 6), Rules.TransformCost(s, W, 21, Terrain.Forest)); // Swamp: 2 spades

        var giants = Play(SoloStart);
        Assert.Equal(new Cost(2, 7), Rules.BuildCost(giants, G, 4));  // 2 spades, as always
        Assert.Equal(new Cost(2, 7), Rules.BuildCost(giants, G, 17));
    }

    // ---------- Spade upgrades ----------

    [Fact]
    public void Upgrading_spades_costs_a_priest_two_workers_five_coins_and_scores_six()
    {
        var s = PlayCharged(SoloStart, GetPriest(G));
        var result = Rules.Apply(s, new UpgradeSpades(G));

        Assert.True(result.Accepted);
        var after = result.State[G];
        Assert.Equal(1, after.SpadeUpgrades);
        Assert.Equal(2, after.WorkersPerSpade);
        Assert.Equal(10, after.Coins);
        Assert.Equal(4, after.Workers);
        Assert.Equal(26, after.VictoryPoints);
        Assert.Empty(after.Priests);
        Assert.Equal(Config.PriestsPerPlayer, after.PriestSupply.Count); // the priest is back in the supply
        Assert.True(result.State.ActionTaken);
        Assert.Equal(new Cost(2, 5), Rules.BuildCost(result.State, G, 4)); // cheaper spades from now on: 2 x 2 + 1
    }

    [Fact]
    public void Spades_can_be_upgraded_twice()
    {
        var s = Play(SoloStart);
        var once = s.WithPlayer(s[G] with { SpadeUpgrades = 1 });
        Assert.Equal(1, Rules.Apply(once, new UpgradeSpades(G)).State[G].WorkersPerSpade);

        var twice = s.WithPlayer(s[G] with { SpadeUpgrades = 2 });
        Assert.Equal("The Giants cannot upgrade their spades any further.",
            Assert.Single(Rules.Apply(twice, new UpgradeSpades(G)).Issues).Message);
    }

    [Fact]
    public void Without_a_ready_priest_an_upgrade_still_happens_and_the_priest_is_owed()
    {
        var result = Rules.Apply(Play(SoloStart), new UpgradeSpades(G));

        Assert.True(result.Accepted);
        Assert.Equal(1, result.State[G].SpadeUpgrades);
        Assert.Equal(1, result.State[G].PriestDebt);
        Assert.Contains(result.Issues, issue => issue.Severity == Severity.Warning && issue.Message.Contains("1 priest"));
    }
}
