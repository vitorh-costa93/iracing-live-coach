using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;

namespace Ams2.OverlayHost.Widgets;

/// <summary>Cria widgets pelo id (usado por --widget). Os widgets não conhecem o tema: só pedem tokens ao canvas.</summary>
public static class WidgetRegistry
{
    public static readonly string[] Ids = ["relative", "standings", "fuel", "tyres", "weather", "inputs", "lapcounter", "drivercaption", "pitstops", "pittimer", "winner", "board", "radar"];

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
        "radar" => new RadarWidget(),
        "relative" => new RelativeWidget(),
        // Id do catalogo (p.ex. widget exclusivo de um tema) ainda sem desenho: janela vazia em vez de cair no Relative.
        var other when WidgetCatalog.Find(other) is not null => new PlaceholderWidget(WidgetCatalog.Find(other)!.Id),
        _ => new RelativeWidget(),
    };
}

/// <summary>Widget do catalogo ainda nao implementado no host: nao desenha nada.</summary>
public sealed class PlaceholderWidget(string id) : IWidget
{
    public string Id { get; } = id;
    public (float Width, float Height) DesignSize => (1, 1);
    public void Configure(WidgetSettings settings) { }
    public void Draw(ThemeCanvas canvas, OverlayModel model) { }
}
