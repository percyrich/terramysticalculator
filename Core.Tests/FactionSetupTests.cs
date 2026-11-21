using Core;
using static Core.Tests.TestHelpers;

namespace Core.Tests;

// The factions: what is printed on their boards, and how they start (Fire & Ice's: FireAndIceTests).
public class FactionSetupTests
{
    private const Faction CM = Faction.ChaosMagicians;
    private const Faction N = Faction.Nomads;

    [Fact]
    public void Two_base_factions_share_each_basic_terrain_and_its_colour()
    {
        Assert.Equal(40, Factions.All.Count);
        foreach (var fan in new[] { false, true }) // the published factions, then the fan factions: the same shape
        {
            var set = Factions.All.Where(pair => Factions.IsFanFaction(pair.Key) == fan).Select(pair => pair.Value).ToList();
            Assert.Equal(20, set.Count);
            Assert.Equal(14, set.Count(faction => faction.Kind == FactionKind.Base));
            Assert.Equal(2, set.Count(faction => faction.Kind == FactionKind.Ice));
            Assert.Equal(2, set.Count(faction => faction.Kind == FactionKind.Volcano));
            Assert.Equal(2, set.Count(faction => faction.Kind == FactionKind.Variable));
            foreach (var terrain in Terrains.Wheel)
            {
                var factions = set.Where(faction => faction.Kind == FactionKind.Base && faction.Home == terrain).ToList();
                Assert.Equal(2, factions.Count);
                Assert.All(factions, faction => Assert.Equal(Terrains.Wheel.IndexOf(terrain), (int)faction.Colour));
            }
        }
    }

    [Fact]
    public void Factions_have_names_for_messages()
    {
        Assert.Equal("Chaos Magicians", Factions.Name(CM));
        var s = Play(ChooseTM, new AddPlayer(CM));
        Assert.Equal("The Chaos Magicians are already in the game.", Assert.Single(Rules.Apply(s, new AddPlayer(CM)).Issues).Message);
    }

    [Fact]
    public void At_most_five_factions_can_play()
    {
        var five = Play(ChooseTM, new AddPlayer(G), new AddPlayer(W), new AddPlayer(M), new AddPlayer(N), new AddPlayer(Faction.Halflings));
        Assert.Equal(5, five.Players.Count);
        Assert.Equal("At most 5 factions can play.",
            Assert.Single(Rules.Apply(five, new AddPlayer(Faction.Alchemists)).Issues).Message);
    }

    [Fact]
    public void Only_one_faction_of_each_colour_can_play()
    {
        var s = Play(ChooseTM, new AddPlayer(G));
        Assert.Equal("The Giants already have Wasteland as their home terrain: only one faction of each colour can play.",
            Assert.Single(Rules.Apply(s, new AddPlayer(CM)).Issues).Message);
        Assert.True(Rules.Apply(PlayFrom(s, new RemovePlayer(G)), new AddPlayer(CM)).Accepted);
    }

    [Theory]
    [InlineData(Faction.Auren, 15, 3, 0)]
    [InlineData(Faction.ChaosMagicians, 15, 4, 0)]
    [InlineData(Faction.Nomads, 15, 2, 0)]
    [InlineData(Faction.Darklings, 15, 1, 1)]
    [InlineData(Faction.Swarmlings, 20, 8, 0)]
    [InlineData(Faction.Engineers, 10, 2, 0)]
    public void Starting_resources_vary_by_faction(Faction faction, int coins, int workers, int priests)
    {
        var s = Play(Placing(faction));
        Assert.Equal(coins, s[faction].Coins);
        Assert.Equal(workers, s[faction].Workers);
        Assert.Equal(priests, s[faction].Priests.Count);
        Assert.Equal(20, s[faction].VictoryPoints);
    }

    [Theory]
    [InlineData(Faction.Auren, 5, 7, new[] { 0, 1, 0, 1 })]
    [InlineData(Faction.Witches, 5, 7, new[] { 0, 0, 0, 2 })]
    [InlineData(Faction.Giants, 5, 7, new[] { 1, 0, 0, 1 })]
    [InlineData(Faction.ChaosMagicians, 5, 7, new[] { 2, 0, 0, 0 })]
    [InlineData(Faction.Nomads, 5, 7, new[] { 1, 0, 1, 0 })]
    [InlineData(Faction.Fakirs, 7, 5, new[] { 1, 0, 0, 1 })]
    [InlineData(Faction.Halflings, 3, 9, new[] { 0, 0, 1, 1 })]
    [InlineData(Faction.Cultists, 5, 7, new[] { 1, 0, 1, 0 })]
    [InlineData(Faction.Alchemists, 5, 7, new[] { 1, 1, 0, 0 })]
    [InlineData(Faction.Darklings, 5, 7, new[] { 0, 1, 1, 0 })]
    [InlineData(Faction.Mermaids, 3, 9, new[] { 0, 2, 0, 0 })]
    [InlineData(Faction.Swarmlings, 3, 9, new[] { 1, 1, 1, 1 })]
    [InlineData(Faction.Dwarves, 5, 7, new[] { 0, 0, 2, 0 })]
    [InlineData(Faction.Engineers, 3, 9, new[] { 0, 0, 0, 0 })]
    [InlineData(Faction.IceMaidens, 6, 6, new[] { 0, 1, 0, 1 })]
    [InlineData(Faction.Yetis, 0, 12, new[] { 0, 0, 1, 1 })]
    [InlineData(Faction.Dragonlords, 4, 4, new[] { 2, 0, 0, 0 })]
    [InlineData(Faction.Acolytes, 6, 6, new[] { 3, 3, 3, 3 })]
    [InlineData(Faction.Riverwalkers, 10, 2, new[] { 1, 0, 0, 1 })]
    [InlineData(Faction.Shapeshifters, 4, 4, new[] { 1, 1, 0, 0 })]
    public void Starting_power_and_tracks_vary_by_faction(Faction faction, int bowl1, int bowl2, int[] steps)
    {
        var player = PlayerState.New(faction);
        Assert.Equal(new Power(bowl1, bowl2, 0), player.Power);
        Assert.Equal(new ValueList<int>(steps), player.TrackSteps);
    }

    // ---------- Starting dwellings ----------

    [Fact]
    public void The_Nomads_place_a_third_dwelling_and_the_Chaos_Magicians_theirs_last()
    {
        var s = Play(Placing(CM, W, N));
        Assert.Equal(ValueList.Of(W, N, N, W, N, CM), s.PlacementQueue);
        Assert.Equal(W, s.ActivePlayer);
    }

    [Fact]
    public void After_the_last_starting_dwelling_pass_tiles_are_picked_in_reverse_turn_order()
    {
        var placed = Play(Concat(Placing(CM, W, N), new[]
        {
            StartAt(W, 0, WitchesStartA), StartAt(N, 0, 4), StartAt(N, 1, 88), StartAt(W, 1, WitchesStartB),
            StartAt(N, 2, 106), StartAt(CM, 0, 5),
        }));

        Assert.Equal(Phase.PickingPassTiles, placed.Phase);
        Assert.Equal(ValueList.Of(N, W, CM), placed.PlacementQueue);
        Assert.Equal(5, placed[N].DwellingBank.Count(slot => slot is not null));
        Assert.Equal(7, placed[CM].DwellingBank.Count(slot => slot is not null));
    }

    [Fact]
    public void A_solo_Chaos_Magician_places_one_dwelling()
    {
        var s = Play(Solo(CM, 5));
        Assert.Equal(1, s.Round);
        Assert.Equal(CM, s.Map[5]?.Owner);
        Assert.Equal(6, s[CM].Workers); // 4 starting + 1 base + 1 under the emptied slot
    }

    [Fact]
    public void Solo_Nomads_place_three()
    {
        var s = Play(Solo(N, 4, 88, 106));
        Assert.Equal(1, s.Round);
        Assert.Equal(6, s[N].Workers); // 2 starting + 1 base + 3
    }
}
