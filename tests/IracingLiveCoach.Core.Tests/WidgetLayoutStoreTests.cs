using System;
using System.IO;
using IracingLiveCoach.Core;
using Xunit;

namespace IracingLiveCoach.Core.Tests;

public class WidgetLayoutStoreTests : IDisposable
{
    private readonly string _originalAppData;
    private readonly string _tempAppData = Path.Combine(Path.GetTempPath(), "widget-layout-tests-" + Guid.NewGuid());

    public WidgetLayoutStoreTests()
    {
        _originalAppData = Environment.GetEnvironmentVariable("APPDATA") ?? "";
        Directory.CreateDirectory(_tempAppData);
        Environment.SetEnvironmentVariable("APPDATA", _tempAppData);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("APPDATA", _originalAppData);
        if (Directory.Exists(_tempAppData)) Directory.Delete(_tempAppData, recursive: true);
    }

    [Fact]
    public void Get_returns_the_given_defaults_for_a_never_seen_key()
    {
        var store = WidgetLayoutStore.Load();
        var layout = store.Get("standings", 320, 360);
        Assert.Equal(320, layout.Width);
        Assert.Equal(360, layout.Height);
        Assert.Null(layout.Left);
        Assert.Null(layout.Top);
        Assert.True(layout.Visible);
    }

    [Fact]
    public void Save_then_Load_round_trips_a_modified_layout()
    {
        var store = WidgetLayoutStore.Load();
        var layout = store.Get("relative", 260, 240);
        layout.Left = 100; layout.Top = 50; layout.Width = 300; layout.Visible = false;
        store.Save();

        var reloaded = WidgetLayoutStore.Load();
        var reloadedLayout = reloaded.Get("relative", 260, 240); // defaults ignored -- a saved value exists
        Assert.Equal(100, reloadedLayout.Left);
        Assert.Equal(50, reloadedLayout.Top);
        Assert.Equal(300, reloadedLayout.Width);
        Assert.False(reloadedLayout.Visible);
    }

    [Fact]
    public void Get_is_idempotent_for_the_same_key_within_one_store_instance()
    {
        var store = WidgetLayoutStore.Load();
        var first = store.Get("coach", 320, 200);
        first.Left = 42;
        var second = store.Get("coach", 999, 999); // different defaults, same key -- ignored, same instance returned
        Assert.Equal(42, second.Left);
        Assert.Equal(320, second.Width); // NOT 999 -- the already-created layout wins, defaults only apply once
    }

    [Fact]
    public void All_reflects_every_key_ever_requested_via_Get()
    {
        var store = WidgetLayoutStore.Load();
        store.Get("coach", 320, 200);
        store.Get("standings", 320, 360);
        Assert.Contains("coach", store.All.Keys);
        Assert.Contains("standings", store.All.Keys);
    }

    [Fact]
    public void Load_migrates_the_old_flat_AppSettings_shape_for_the_coach_widget_only()
    {
        var dir = Path.Combine(_tempAppData, "iracing-live-coach");
        Directory.CreateDirectory(dir);
        // Old shape (pre-this-task): flat Left/Top/Width/Height fields alongside ImportKey, no
        // "Widgets" dictionary at all. A driver upgrading from before this plan has this file on
        // disk and must not lose their already-positioned Coach card.
        File.WriteAllText(Path.Combine(dir, "settings.json"), """{"Left":111,"Top":222,"Width":333,"Height":444,"ImportKey":"abc"}""");

        var store = WidgetLayoutStore.Load();
        var coach = store.Get("coach", 320, 200);
        Assert.Equal(111, coach.Left);
        Assert.Equal(222, coach.Top);
        Assert.Equal(333, coach.Width);
        Assert.Equal(444, coach.Height);
    }
}
