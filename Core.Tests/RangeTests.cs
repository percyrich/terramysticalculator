using Core;
using static Core.Tests.TestHelpers;

namespace Core.Tests;

// Solo Giants with dwellings on hexes 5 and 112. Around hex 5:
//   4 Desert, 6 Plains, 17 Swamp: land next to it.  18: a river next to it.
//   31 Forest: across river 18.  7 Swamp: across rivers 18 and 19.  0 Plains: nowhere near.
public class RangeTests
{
    private static GameState WithShipping(int shipping)
    {
        var s = Play(SoloStart);
        return s.WithPlayer(s[G] with { Shipping = shipping });
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(17)]
    public void Land_next_to_a_building_is_in_range(int hex)
    {
        Assert.True(Rules.IsInRange(WithShipping(0), G, hex));
    }

    [Fact]
    public void Distant_land_is_out_of_range()
    {
        var s = WithShipping(0);
        Assert.False(Rules.IsInRange(s, G, 0));
        Assert.False(Rules.IsInRange(s, G, 31)); // across a river, with no shipping
    }

    [Fact]
    public void Rivers_are_never_in_range()
    {
        Assert.False(Rules.IsInRange(WithShipping(3), G, 18));
    }

    [Fact]
    public void Shipping_one_crosses_one_river()
    {
        var s = WithShipping(1);
        Assert.True(Rules.IsInRange(s, G, 31));
        Assert.False(Rules.IsInRange(s, G, 7));  // needs two rivers
        Assert.False(Rules.IsInRange(s, G, 0));  // shipping never helps across land
    }

    [Fact]
    public void Shipping_two_crosses_two_rivers()
    {
        Assert.True(Rules.IsInRange(WithShipping(2), G, 7));
    }
}
