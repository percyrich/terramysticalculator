using Core;

namespace Core.Tests;

// The Power value on its own, with no game around it.
public class PowerTests
{
    [Fact]
    public void Gain_moves_tokens_out_of_bowl_1_first()
    {
        Assert.Equal(new Power(3, 9, 0), new Power(6, 6, 0).Gain(3));
    }

    [Fact]
    public void Once_bowl_1_is_empty_gain_moves_bowl_2_into_bowl_3()
    {
        // 2 points empty bowl 1; the other 3 move tokens from bowl 2 to bowl 3.
        Assert.Equal(new Power(0, 5, 3), new Power(2, 6, 0).Gain(5));
    }

    [Fact]
    public void Gain_beyond_what_can_move_is_lost()
    {
        var after = new Power(0, 2, 10).Gain(5, out var moved);
        Assert.Equal(new Power(0, 0, 12), after);
        Assert.Equal(2, moved);
    }

    [Fact]
    public void Spending_returns_tokens_from_bowl_3_to_bowl_1()
    {
        Assert.Equal(new Power(9, 6, 0), new Power(6, 6, 3).Spend(3));
    }

    [Fact]
    public void Spending_more_than_bowl_3_holds_is_impossible()
    {
        var power = new Power(6, 6, 2);
        Assert.False(power.CanSpend(3));
        Assert.Throws<InvalidOperationException>(() => power.Spend(3));
    }

    [Fact]
    public void Burning_removes_one_token_to_move_another_to_bowl_3()
    {
        var after = new Power(0, 5, 0).BurnOnce(doubleMove: false);
        Assert.Equal(new Power(0, 3, 1), after);
        Assert.Equal(4, after.Total);
    }

    [Fact]
    public void Burning_needs_two_tokens_in_bowl_2()
    {
        Assert.True(new Power(0, 2, 0).CanBurn(1));
        Assert.False(new Power(5, 1, 6).CanBurn(1));
        Assert.Throws<InvalidOperationException>(() => new Power(5, 1, 6).BurnOnce(doubleMove: false));
    }

    [Fact]
    public void Tokens_are_listed_most_valuable_first()
    {
        Assert.Equal(ValueList.Of(3, 3, 3, 2, 2, 1), new Power(1, 2, 3).TokenBowls);
    }

    [Fact]
    public void New_tokens_can_be_added_to_any_bowl()
    {
        Assert.Equal(new Power(8, 6, 0), new Power(6, 6, 0).AddTokens(1, 2));
        Assert.Equal(new Power(6, 6, 1), new Power(6, 6, 0).AddTokens(3, 1));
    }

    [Fact]
    public void Players_start_with_five_in_bowl_1_and_seven_in_bowl_2_unless_their_board_says_otherwise()
    {
        Assert.Equal(new Power(5, 7, 0), PlayerState.New(Faction.Giants).Power);
        Assert.Equal(new Power(7, 5, 0), PlayerState.New(Faction.Fakirs).Power);
        Assert.Equal(new Power(3, 9, 0), PlayerState.New(Faction.Engineers).Power);
    }
}
