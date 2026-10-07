using Ams2.Shared.Profiles;
using Ams2.Core.Calc;

namespace Ams2.OverlayHost.Data;

/// <summary>Dados isolados para editar sem pilotar; nunca leem nem alteram a memória do jogo.</summary>
public static class LayoutPreview
{
    static readonly Lazy<OverlayModel> Race = new(() => Create(false));
    static readonly Lazy<OverlayModel> Qualify = new(() => Create(true));

    public static OverlayModel ForWidget(string id)
        => id.StartsWith("quali", StringComparison.Ordinal) ? Qualify.Value : id == "racecontrol"
            ? Race.Value with { Session = Race.Value.Session! with { FlagColour = 6 } }
            : id == "board"
            ? Race.Value with { Broadcast = Race.Value.Broadcast! with { Winner = null, PlayerStopEndT = double.NegativeInfinity } }
            : Race.Value;

    static OverlayModel Create(bool quali)
    {
        double now = 0;
        using var provider = new OverlayDataProvider(new FakeRawSource(() => now, board: !quali, radar: true, quali: quali), () => now);
        for (int i = 0; i < 1200; i++) { now += 1.0 / 60; provider.Tick(); }
        var model = provider.Current;
        if (quali) return model;
        var leader = model.Standings[0].Car;
        return model with
        {
            Broadcast = model.Broadcast! with
            {
                PlayerStopSeconds = 3.4,
                PlayerStopEndT = now,
                Winner = new WinnerInfo(leader, 1800, 100, 200, now),
            },
            Launch = new LaunchState(new LaunchResult("preview", 2.3, 4.6, now, now, 2.4, 4.8), 2.3, 4.6),
        };
    }

    public static WidgetSettings Settings(WidgetSettings settings)
    {
        var options = settings.Options is null ? new Dictionary<string, string>() : new Dictionary<string, string>(settings.Options);
        options["always"] = "true";
        options["layoutPreview"] = "true";
        var columns = settings.Columns ?? WidgetCatalog.Find(settings.Id)?.Columns.Select(c => c.Id).ToArray() ?? [];
        return settings with { Columns = columns.Append("always").Distinct().ToArray(), Options = options };
    }
}
