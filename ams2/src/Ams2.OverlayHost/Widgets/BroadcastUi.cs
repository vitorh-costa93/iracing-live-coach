using System.Globalization;
using Ams2.Core;
using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;

namespace Ams2.OverlayHost.Widgets;

/// <summary>Regras de exibicao e formatacao compartilhadas pelos widgets de "transmissao" (legenda, paradas, cronometro, vencedor).</summary>
public static class BroadcastUi
{
    public const double CaptionHold = 6, PitListHold = 8, PitTimerHold = 4, WinnerHold = 10;

    /// <summary>Alfa (0..1) de um elemento que aparece com fade, fica <paramref name="hold"/> s e some com fade. age &lt; 0 ou infinito = oculto.</summary>
    public static float Fade(double age, double hold, double fadeIn = 0.3, double fadeOut = 0.6)
    {
        if (double.IsNaN(age) || age < 0 || age >= hold) return 0f;
        if (age < fadeIn) return (float)(age / fadeIn);
        if (age > hold - fadeOut) return (float)((hold - age) / fadeOut);
        return 1f;
    }

    /// <summary>Executa o desenho com a opacidade do canvas multiplicada por <paramref name="alpha"/>; alfa ~0 nao desenha.</summary>
    public static void WithAlpha(ThemeCanvas c, float alpha, Action draw)
    {
        if (alpha <= 0.01f) return;
        float prev = c.Opacity;
        c.Opacity = prev * alpha;
        try { draw(); } finally { c.Opacity = prev; }
    }

    public static BroadcastState State(OverlayModel m) => m.Broadcast ?? BroadcastState.Empty;

    /// <summary>Sobrenome do piloto ("Alonso"); com sobrenome repetido no grid, inicial + sobrenome ("M Schumacher").</summary>
    public static string ShortName(CarSnapshot car, IEnumerable<CarSnapshot> field)
    {
        var parts = car.Name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "";
        if (parts.Length == 1) return parts[0];
        string last = string.Join(' ', parts[1..]);
        bool dup = field.Any(o => o.Index != car.Index && o.Name.Trim().EndsWith(last, StringComparison.OrdinalIgnoreCase));
        return dup ? parts[0][0] + " " + last : last;
    }

    /// <summary>Siglas de 3 letras únicas no campo (<see cref="Ams2.Shared.Profiles.DisplayFormat.UniqueCodes"/>), por índice do carro.
    /// Ordem estável = índice do carro (a sigla não troca quando as posições mudam).</summary>
    public static Dictionary<int, string> Codes(IEnumerable<CarSnapshot> field)
    {
        var cars = field.GroupBy(c => c.Index).Select(g => g.First()).OrderBy(c => c.Index).ToArray();
        var codes = Ams2.Shared.Profiles.DisplayFormat.UniqueCodes(cars.Select(c => c.Name).ToArray());
        var map = new Dictionary<int, string>(cars.Length);
        for (int i = 0; i < cars.Length; i++) map[cars[i].Index] = codes[i];
        return map;
    }

    /// <summary>Equipe deduzida do nome do carro: sem o sufixo de fornecedor "(M)"/"(B)" e sem o nome da classe.</summary>
    public static string Team(CarSnapshot car)
    {
        string t = car.CarName.Trim();
        int p = t.LastIndexOf('(');
        if (p > 0 && t.EndsWith(')')) t = t[..p].Trim();
        if (car.ClassName.Length > 0 && t.Length > car.ClassName.Length && t.StartsWith(car.ClassName, StringComparison.OrdinalIgnoreCase)) t = t[car.ClassName.Length..].Trim();
        else if (car.ClassName.Length > 0 && t.Length > car.ClassName.Length && t.EndsWith(car.ClassName, StringComparison.OrdinalIgnoreCase)) t = t[..^car.ClassName.Length].Trim();
        return t.Length > 0 ? t : car.CarName.Trim();
    }

    /// <summary>Reduz o tamanho da fonte ate o texto caber em <paramref name="maxWidth"/> (minimo 14).</summary>
    public static FontToken Fit(ThemeCanvas c, string text, FontToken font, float maxWidth)
    {
        while (font.Size > 14 && c.Measure(text, font) > maxWidth) font = font with { Size = font.Size - 1 };
        return font;
    }

    public static string Stops(int n) => n.ToString(CultureInfo.InvariantCulture) + (n == 1 ? " Stop" : " Stops");

    public static string StopTime(double seconds) => seconds.ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>"1:37:32.747" (h:mm:ss.mmm) ou "37:32.747" quando menos de 1 h; sem dado: "--:--.---".</summary>
    public static string RaceTime(double seconds)
    {
        if (seconds <= 0) return "--:--.---";
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}.{ts.Milliseconds:000}"
            : $"{ts.Minutes}:{ts.Seconds:00}.{ts.Milliseconds:000}";
    }
}

/// <summary>
/// Cache por widget das siglas únicas do campo (<see cref="BroadcastUi.Codes"/>): só recalcula quando os carros (índice/nome) mudam.
/// <see cref="Update"/> uma vez por quadro; <see cref="Code"/> devolve a sigla única (ou a sigla base para carro fora do campo).
/// </summary>
public sealed class FieldCodes
{
    (int Index, string Name)[] _key = [];
    Dictionary<int, string> _map = [];

    public void Update(IEnumerable<CarSnapshot> field)
    {
        var key = field.Select(c => (c.Index, c.Name)).ToArray();
        if (key.AsSpan().SequenceEqual(_key)) return;
        _key = key;
        _map = BroadcastUi.Codes(field);
    }

    public string Code(CarSnapshot car) => _map.TryGetValue(car.Index, out var k) ? k : RelativeWidget.Code(car.Name);
}
