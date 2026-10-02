namespace Ams2.OverlayHost.Widgets;

/// <summary>Cria widgets pelo id (usado por --widget). Os widgets não conhecem o tema: só pedem tokens ao canvas.</summary>
public static class WidgetRegistry
{
    public static readonly string[] Ids = ["relative", "standings", "fuel", "tyres", "weather", "inputs"];

    public static IWidget Create(string? id) => (id ?? "relative").ToLowerInvariant() switch
    {
        "standings" => new StandingsWidget(),
        "fuel" => new FuelWidget(),
        _ => new RelativeWidget(),
    };
}
