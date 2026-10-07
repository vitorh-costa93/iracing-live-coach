using Ams2.Core.Calc;

namespace Ams2.Core.Tests;

public class StandingsSelectorTests
{
    static int[] Idx(IReadOnlyList<StandingsPick> p) => p.Select(x => x.Index).ToArray();
    static int[] Pos(IReadOnlyList<StandingsPick> p) => p.Select(x => x.Index + 1).ToArray();

    [Fact]
    public void Player_inside_top_shows_contiguous_rows_without_separator()
    {
        var p = StandingsSelector.Select(20, 1, 5, 3); // P2
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], Pos(p));
        Assert.All(p, x => Assert.False(x.GapBefore));
    }

    [Fact]
    public void Player_far_back_shows_top_plus_window_with_a_separator()
    {
        var p = StandingsSelector.Select(20, 9, 5, 3); // P10: P1-5, P9-11
        Assert.Equal([1, 2, 3, 4, 5, 9, 10, 11], Pos(p));
        Assert.Equal(new[] { false, false, false, false, false, true, false, false }, p.Select(x => x.GapBefore).ToArray());
    }

    [Fact]
    public void Player_last_keeps_the_window_inside_the_field()
    {
        var p = StandingsSelector.Select(20, 19, 5, 3);
        Assert.Equal([1, 2, 3, 4, 5, 18, 19, 20], Pos(p));
        Assert.True(p[5].GapBefore);
    }

    [Fact]
    public void Window_touching_the_top_merges_and_is_filled_in_rank_order()
    {
        // P7 com top 5 / near 3: janela P6-P8 encosta no topo -> sem salto.
        var p = StandingsSelector.Select(20, 6, 5, 3);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], Pos(p));
        Assert.DoesNotContain(p, x => x.GapBefore);
        // P6 com top 5 / near 3: janela P5-P7, P5 repetido -> total continua 8 (P8 preenche).
        var q = StandingsSelector.Select(20, 5, 5, 3);
        Assert.Equal(8, q.Count);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], Pos(q));
    }

    [Fact]
    public void Near_zero_still_shows_the_player()
    {
        var p = StandingsSelector.Select(20, 11, 3, 0);
        Assert.Equal([1, 2, 3, 12], Pos(p));
        Assert.True(p[3].GapBefore);
        // dentro do topo, nenhuma linha extra
        Assert.Equal([1, 2, 3], Pos(StandingsSelector.Select(20, 1, 3, 0)));
    }

    [Fact]
    public void Top_zero_shows_only_the_window_around_the_player()
    {
        var p = StandingsSelector.Select(20, 9, 0, 3);
        Assert.Equal([9, 10, 11], Pos(p));
        Assert.False(p[0].GapBefore); // nada antes: o primeiro item nunca tem separador
    }

    [Fact]
    public void Small_fields_and_missing_player()
    {
        Assert.Equal([1, 2, 3], Pos(StandingsSelector.Select(3, 2, 5, 3)));
        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7], Idx(StandingsSelector.Select(20, -1, 5, 3))); // sem jogador: topo + preenchimento
        Assert.Empty(StandingsSelector.Select(0, -1, 5, 3));
    }

    [Theory]
    [InlineData(20, 0, 5, 3)]
    [InlineData(20, 19, 8, 6)]
    [InlineData(12, 7, 2, 1)]
    [InlineData(30, 15, 0, 5)]
    public void Always_contains_the_player_and_is_ordered_and_distinct(int count, int me, int top, int near)
    {
        var p = StandingsSelector.Select(count, me, top, near);
        Assert.Contains(me, Idx(p));
        Assert.Equal(Idx(p).Distinct().Order(), Idx(p));
        Assert.True(p.Count <= top + Math.Max(near, 1));
    }
}
