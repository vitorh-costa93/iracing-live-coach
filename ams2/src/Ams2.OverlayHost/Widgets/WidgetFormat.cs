using Ams2.Core;
using Ams2.Shared.Profiles;

namespace Ams2.OverlayHost.Widgets;

/// <summary>Atalhos de formato do perfil para os widgets (os formatadores puros ficam em <see cref="DisplayFormat"/>).</summary>
public static class WidgetFormat
{
    /// <summary>
    /// Nome do piloto no formato do perfil; sem formato escolhido, <paramref name="widgetDefault"/> (o que o widget/tema ja mostrava).
    /// "Numero do carro" prefixa "#N" (indice + 1: o AMS2 nao expoe o numero real).
    /// </summary>
    public static string Name(this WidgetSettings s, string fullName, int carIndex, string widgetDefault)
    {
        var f = s.Fmt;
        string n = f.Name is { } style ? DisplayFormat.Name(fullName, style) : widgetDefault;
        return f.CarNumber == true ? "#" + (carIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + n : n;
    }

    public static string Name(this WidgetSettings s, CarSnapshot car, string widgetDefault) => s.Name(car.Name, car.Index, widgetDefault);
}
