using Ams2.Core.Calc;
using Ams2.Core.Raw;
using Ams2.Core.Reading;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Gfx;
using Ams2.OverlayHost.Theme;
using Ams2.OverlayHost.Widgets;
using Ams2.Shared.Profiles;

namespace Ams2.Integration.Tests;

public class FastWidgetRenderingTests
{
    sealed class Source : IRawMemorySource
    {
        public RawSharedMemory Raw;
        public bool TryRead(out RawSharedMemory raw) { raw = Raw; return true; }
        public void Dispose() { }
    }

    [Fact]
    public void Repeated_game_write_preserves_radar_pose_time_until_new_data_arrives()
    {
        double now = 3;
        using var fake = new FakeRawSource(() => now, radar: true);
        Assert.True(fake.TryRead(out var raw)); raw.SequenceNumber = 2;
        var source = new Source { Raw = raw };
        using var provider = new OverlayDataProvider(source, () => now);
        var first = provider.Tick().Radar!;
        Assert.True(first.Valid); Assert.True(first.AnyNear);
        now += .01;
        var repeated = provider.Tick().Radar!;
        Assert.Same(first, repeated); Assert.Equal(3, repeated.Time);
        source.Raw.SequenceNumber = 4;
        Assert.Equal(now, provider.Tick().Radar!.Time);
        source.Raw.GameState = 4;
        Assert.False(provider.Tick().Radar!.Valid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Solo_qualifying_clears_existing_radar_in_both_styles(bool native)
    {
        double now = 3;
        using var fake = new FakeRawSource(() => now, radar: true);
        Assert.True(fake.TryRead(out var raw));
        var source = new Source { Raw = raw };
        using var provider = new OverlayDataProvider(source, () => now);
        var model = provider.Tick();
        var widget = new RadarWidget();
        widget.Configure(new WidgetSettings { Id = "radar", Columns = native ? ["native"] : ["always"] });
        using var gfx = DeviceResources.CreateOffscreen(300, 200);
        using var canvas = new ThemeCanvas(gfx, Themes.F1_2004);
        byte[] Render(OverlayModel m)
        {
            gfx.BeginFrame(); canvas.Begin(); widget.Draw(canvas, m); canvas.End(); gfx.EndFrame();
            return gfx.ReadPixelsBgra();
        }
        Assert.Contains(Render(model), value => value != 0);
        source.Raw.SessionState = 3; source.Raw.SequenceNumber += 2;
        for (int i = 0; i < source.Raw.NumParticipants; i++)
            if (i != source.Raw.ViewedParticipantIndex)
            { source.Raw.Participants[i].WorldPosition = default; source.Raw.Orientations[i * 3 + 1] = 0; }
        now += .02;
        model = provider.Tick();
        Assert.False(model.Radar!.Valid); Assert.True(widget.IsIdle(model));
        Assert.All(Render(model), value => Assert.Equal(0, value));
    }

    [Fact]
    public void Cached_text_survives_eviction_and_updates_size_weight_and_width_independently()
    {
        using var gfx = DeviceResources.CreateOffscreen(400, 100);
        using var canvas = new ThemeCanvas(gfx, Themes.F1_2004);
        var font = Themes.F1_2004.Text with { Size = 30, Tracking = .5f };
        byte[] Render()
        {
            gfx.BeginFrame(); canvas.Begin();
            canvas.Text("SCHUMACHER", font, 0, 0, 390, 90, new(1, 1, 1, 1), HAlign.Center);
            canvas.End(); gfx.EndFrame(); return gfx.ReadPixelsBgra();
        }
        var original = Render();
        Assert.Equal(original, Render());
        for (int i = 0; i < 700; i++) canvas.Measure(i.ToString(), font);
        Assert.Equal(original, Render());
        canvas.ConfigureTypography(new WidgetSettings { Id = "inputs", TextScale = .6f, FontWeight = 300 });
        Assert.False(original.SequenceEqual(Render()));
        canvas.ConfigureTypography(new WidgetSettings { Id = "inputs" });
        Assert.Equal(original, Render());
    }
}
