using Ams2.Core.Calc.GpxSat;

namespace Ams2.Core.Tests;

public class GpxSatTests
{
    [Fact]
    public void Animator_wipes_then_settles_groups_one_by_one_at_20ms_steps()
    {
        var a = new GpxAnimator { StepMs = 20 };
        Assert.Equal(1, a.Update("x", 0, 2, GpxAnimKind.Rows, 10));
        Assert.Equal(10, a.FadePct(10));
        Assert.False(a.FadeDone(10.199));
        Assert.True(a.FadeDone(10.200));                 // 10 passos de 20 ms
        Assert.Equal(0, a.RotPct(0, 10.200));
        Assert.Equal(100, a.RotPct(1, 10.200));          // grupo 1 ainda nao comecou
        Assert.False(a.Settled(0, 10.399));
        Assert.True(a.Settled(0, 10.400));
        Assert.Equal(1, a.ActiveGroup(10.400));
        Assert.True(a.AllSettled(10.600));
        Assert.Equal(0.8, a.SettleSeconds, 6);           // wipe + 2 grupos + giro do logo = 40 passos x 20 ms
    }

    [Fact]
    public void Animator_restarts_on_key_change_and_arms_logo_only_on_keyA()
    {
        var a = new GpxAnimator { StepMs = 20 };
        a.Update("st", 0, 3, GpxAnimKind.Columns, 0);
        Assert.Equal(0, a.Update("st", 0, 3, GpxAnimKind.Columns, 1));
        Assert.Equal(2, a.Update("st", 1, 3, GpxAnimKind.Columns, 2));   // pagina: reinicia sem logo
        Assert.False(a.LogoArmed);
        Assert.Equal(2, a.StartedAt);
        Assert.Equal(1, a.Update("win", 0, 1, GpxAnimKind.Rows, 3));
        Assert.True(a.LogoArmed);
        // Logo: gira depois do ultimo grupo (passos 20..29) e fica 6 s.
        Assert.Equal(GpxLogoPhase.Hidden, a.Logo(3.39, true).Phase);
        Assert.Equal(GpxLogoPhase.Flip, a.Logo(3.41, true).Phase);
        Assert.Equal(GpxLogoPhase.Shown, a.Logo(3.61, true).Phase);
        Assert.Equal(GpxLogoPhase.Hidden, a.Logo(3.61 + 6.1, true).Phase);
        Assert.Equal(GpxLogoPhase.Hidden, a.Logo(3.61, false).Phase);
    }

    [Fact]
    public void Animator_instant_is_settled_at_once_and_flip_shrink_is_triangular()
    {
        var a = new GpxAnimator();
        a.Update("p", 0, 4, GpxAnimKind.Instant, 5);
        Assert.True(a.FadeDone(5)); Assert.True(a.Settled(3, 5)); Assert.Equal(-1, a.ActiveGroup(5));
        Assert.Equal(0, GpxAnimator.Tri(0)); Assert.Equal(50, GpxAnimator.Tri(50)); Assert.Equal(10, GpxAnimator.Tri(90));
    }

    [Fact]
    public void DriverName_mixed_case_for_2004_and_2012_only()
    {
        Assert.Equal("V. COSTA", GpxFormat.DriverName("Vitor Costa"));
        Assert.Equal("V. Costa", GpxFormat.DriverName("vitor Costa", true));
    }

    [Theory]
    [InlineData(83.456, "1:23.456")]
    [InlineData(9.5, "9.500")]
    [InlineData(3725.001, "1:02:05.001")]
    [InlineData(null, "-:--.---")]
    [InlineData(0.0, "-:--.---")]
    public void LapTime_formats_like_gpxsat(double? s, string expected) => Assert.Equal(expected, GpxFormat.LapTime(s));

    [Fact]
    public void Clock_tenths_and_gaps()
    {
        Assert.Equal("4:05", GpxFormat.Clock(245.9));
        Assert.Equal("1:00:00", GpxFormat.Clock(3600));
        Assert.Equal("0:00", GpxFormat.Clock(-3));
        Assert.Equal("23.4", GpxFormat.Tenths(23.47));
        Assert.Equal("1:02.5", GpxFormat.Tenths(62.55));
        Assert.Equal("+ 1.842", GpxFormat.SignedGap(1.842));
        Assert.Equal("- 0.204", GpxFormat.SignedGap(-0.204));
        Assert.Equal("+ 1 Lap", GpxFormat.LapsBehind(1));
        Assert.Equal("2 Stops", GpxFormat.Stops(2));
        Assert.Equal("(-0.12)", GpxFormat.Change(-0.12));
    }

    [Fact]
    public void Geometry_uses_tpl2_layout_and_pillarbox_fit()
    {
        var g = new GpxGeometry(17);
        Assert.Equal(204.8f, g.L0, 3);
        Assert.Equal(4f, g.Indent);                       // FH/4 inteiro
        Assert.Equal(768 - 3 * 17, g.Row(3));
        var (sx, sy, ox, oy) = GpxGeometry.Fit(1920, 1080, stretch: false);
        Assert.Equal(1080f / 768, sx, 4); Assert.Equal(sx, sy);
        Assert.Equal(240f, ox, 3); Assert.Equal(0f, oy, 3);
        var st = GpxGeometry.Fit(1920, 1080, stretch: true);
        Assert.Equal(1.875f, st.Sx, 4); Assert.Equal(0f, st.Ox);
    }

    [Fact]
    public void Rotation_cycles_pages_and_slides_every_page_seconds()
    {
        GpxRotationPage[] pages = [new("str", 2), new("ahead"), new("trap")];
        var seq = Enumerable.Range(0, 5).Select(i => GpxRotation.Current(pages, i * 6 + 1, 6)).ToList();
        Assert.Equal([("str", 0), ("str", 1), ("ahead", 0), ("trap", 0), ("str", 0)], seq.Select(s => (s!.Value.Id, s.Value.Slide)));
        Assert.Equal("str", GpxRotation.Current(pages, -5, 6)!.Value.Id);
    }

    [Fact]
    public void Rotation_skips_empty_pages_and_returns_null_without_data()
    {
        GpxRotationPage[] pages = [new("str", 0), new("stp", 0), new("ahead"), new("trap", 0)];
        foreach (double now in new[] { 0.0, 7, 13, 100 }) Assert.Equal("ahead", GpxRotation.Current(pages, now, 6)!.Value.Id);
        Assert.Null(GpxRotation.Current([new("str", 0), new("trap", 0)], 10, 6));
        Assert.Null(GpxRotation.Current([], 10, 6));
        Assert.Equal(0, GpxRotation.PagesOf(0, 8));
        Assert.Equal(3, GpxRotation.PagesOf(17, 8));
    }
}
