namespace Ams2.OverlayHost.Widgets;

/// <summary>Cria widgets pelo id (usado por --widget). Os widgets não conhecem o tema: só pedem tokens ao canvas.</summary>
public static class WidgetRegistry
{
    public static readonly string[] Ids = ["relative", "standings", "fuel", "tyres", "weather", "inputs", "lapcounter", "drivercaption", "pitstops", "pittimer", "winner", "board"];

    public static IWidget Create(string? id) => (id ?? "relative").ToLowerInvariant() switch
    {
        "standings" => new StandingsWidget(),
        "fuel" => new FuelWidget(),
        "tyres" => new TyresWidget(),
        "weather" => new WeatherWidget(),
        "inputs" => new InputsWidget(),
        "lapcounter" => new LapCounterWidget(),
        "drivercaption" => new DriverCaptionWidget(),
        "pitstops" => new PitStopsWidget(),
        "pittimer" => new PitTimerWidget(),
        "winner" => new WinnerWidget(),
        "board" => new BoardWidget(),
        _ => new RelativeWidget(),
    };
}
