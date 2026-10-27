using System.Globalization;

namespace Ams2.Core.Calc.GpxSat;

/// <summary>
/// Formatadores de texto do GPxSAT (spec_digital2004.md secao 5): tempo de volta 0x9c28ec9, relogio 0x9c28f97 e decimos 0x9c2905f,
/// mais os textos fixos (strings do GetString). Puros e invariantes de cultura.
/// </summary>
public static class GpxFormat
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public const string InvalidLap = "-:--.---";

    /// <summary>"%lu:%02lu:%02lu.%03lu" / "%lu:%02lu.%03lu" / "%lu.%03lu"; invalido = "-:--.---".</summary>
    public static string LapTime(double? seconds)
    {
        if (seconds is not { } s || !double.IsFinite(s) || s <= 0) return InvalidLap;
        long ms = (long)Math.Round(s * 1000, MidpointRounding.AwayFromZero);
        long h = ms / 3_600_000, m = ms / 60_000 % 60, sec = ms / 1000 % 60, f = ms % 1000;
        if (h > 0) return string.Create(Inv, $"{h}:{m:00}:{sec:00}.{f:000}");
        if (m > 0) return string.Create(Inv, $"{m}:{sec:00}.{f:000}");
        return string.Create(Inv, $"{sec}.{f:000}");
    }

    /// <summary>Relogio do time left: "h:mm:ss" com hora, senao "m:ss" (segundos truncados; negativo = 0:00).</summary>
    public static string Clock(double? seconds)
    {
        long t = seconds is { } s && double.IsFinite(s) && s > 0 ? (long)Math.Floor(s) : 0;
        long h = t / 3600, m = t / 60 % 60, sec = t % 60;
        return h > 0 ? string.Create(Inv, $"{h}:{m:00}:{sec:00}") : string.Create(Inv, $"{m}:{sec:00}");
    }

    /// <summary>Decimos (timer de boxes, gap ao vivo): "m:ss.t" acima de um minuto, senao "s.t"; vazio/invalido = "0.0".</summary>
    public static string Tenths(double? seconds)
    {
        if (seconds is not { } s || !double.IsFinite(s) || s <= 0) return "0.0";
        long d = (long)Math.Floor(s * 10 + 1e-9);
        long m = d / 600, sec = d / 10 % 60, t = d % 10;
        return m > 0 ? string.Create(Inv, $"{m}:{sec:00}.{t}") : string.Create(Inv, $"{d / 10}.{t}");
    }

    /// <summary>Gap em tempo com sinal e espaco ("+ 1.842", "- 0.204"), como no tpl2 aprovado; acima de um minuto usa m:ss.fff.</summary>
    public static string SignedGap(double seconds, bool forcePlus = true)
    {
        string sign = seconds < 0 ? "- " : forcePlus ? "+ " : "";
        return sign + LapTime(Math.Max(Math.Abs(seconds), 0.0005));
    }

    /// <summary>"+ 1 Lap" / "+ 2 Laps".</summary>
    public static string LapsBehind(int laps) => string.Create(Inv, $"+ {laps} {(laps == 1 ? "Lap" : "Laps")}");

    /// <summary>Variacao entre duas passagens: "(+0.05)" / "(-0.12)" (centesimos).</summary>
    /// <summary>Segunda linha do gap: o gap medido no setor anterior, entre parenteses.</summary>
    public static string Previous(double gap) => "(" + SignedGap(gap) + ")";
    public static string Change(double delta) => "(" + (delta < 0 ? "-" : "+") + Math.Abs(delta).ToString("0.00", Inv) + ")";

    /// <summary>"%d Stop" / "%d Stops" (strings 4/5).</summary>
    public static string Stops(int n) => string.Create(Inv, $"{n} {(n == 1 ? "Stop" : "Stops")}");

    /// <summary>"%.1f km/h" (speed trap).</summary>
    public static string Speed(double mps, bool mph = false)
        => (mph ? mps * 2.2369362921 : mps * 3.6).ToString("0.0", Inv) + (mph ? " mph" : " km/h");

    /// <summary>"%.3f km" / "%.3f mi" e media "%.3f km/h".</summary>
    public static string Distance(double km, bool miles = false) => (miles ? km / 1.609344 : km).ToString("0.000", Inv) + (miles ? " mi" : " km");
    public static string AvgSpeed(double kmh, bool miles = false) => (miles ? kmh / 1.609344 : kmh).ToString("0.000", Inv) + (miles ? " mph" : " km/h");

    // Strings fixas do GPxSAT (GetString n).
    public const string FastestLap = "Fastest Lap", OutOfRace = "Out of the race", InThePits = "In the pits", Winner = "Winner",
        TopSpeeds = "Top Speeds", CurrentSpeed = "Current Speed", DriveThrough = "Drive Through", Penalty = "Penalty", Out = "  Out",
        FinishLine = "Finish Line";

    public static string Lap(int lap) => string.Create(Inv, $"Lap {lap}");
    public static string Intermediate(int n) => string.Create(Inv, $"Intermediate {n}");

    /// <summary>Nome do piloto como no GPx ("A. SENNA"): inicial do primeiro nome + sobrenome em maiusculas.</summary>
    public static string DriverName(string name, bool mixedCase = false)
    {
        var parts = (name ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "";
        if (parts.Length == 1) return mixedCase ? parts[0] : parts[0].ToUpperInvariant();
        string s = char.ToUpperInvariant(parts[0][0]) + ". " + string.Join(' ', parts[1..]);
        return mixedCase ? s : s.ToUpperInvariant();
    }

    /// <summary>Nome curto da sessao na caixa preta do time left.</summary>
    public static string SessionShort(SessionKind kind) => kind switch
    {
        SessionKind.Race => "RACE",
        SessionKind.Qualify => "QUAL",
        SessionKind.Practice => "PRAC",
        SessionKind.Test => "TEST",
        SessionKind.FormationLap => "FORM",
        SessionKind.TimeAttack => "TIME",
        _ => "",
    };
}
