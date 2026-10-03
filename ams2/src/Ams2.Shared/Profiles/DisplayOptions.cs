using System.Globalization;
using System.Text.Json.Serialization;

namespace Ams2.Shared.Profiles;

/// <summary>Formato do nome do piloto. Null no perfil = o padrao do widget/tema (sigla, sobrenome...).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<NameStyle>))]
public enum NameStyle { Code3, Initials, InitialLastName, LastName, FullName }

/// <summary>Tempo de volta: "1:23.456" ou so segundos "83.456".</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LapTimeStyle>))]
public enum LapTimeStyle { MinSec, Seconds }

[JsonConverter(typeof(JsonStringEnumConverter<SpeedUnit>))]
public enum SpeedUnit { Kph, Mph }

[JsonConverter(typeof(JsonStringEnumConverter<TempUnit>))]
public enum TempUnit { Celsius, Fahrenheit }

[JsonConverter(typeof(JsonStringEnumConverter<FuelUnit>))]
public enum FuelUnit { Liters, Gallons }

/// <summary>Quais grupos de formato um widget usa (o Control Center so mostra os relevantes).</summary>
[Flags]
public enum DisplayCaps { None = 0, Name = 1, Gap = 2, LapTime = 4, Speed = 8, Temp = 16, Fuel = 32 }

/// <summary>
/// Formato dos textos de um widget (persistido no perfil). Todo campo nulo = comportamento padrao do widget/tema, entao perfis antigos
/// (sem este bloco) desenham exatamente como antes.
/// </summary>
public sealed record DisplayOptions
{
    public const int MaxDecimals = 3;

    public NameStyle? Name { get; init; }
    /// <summary>Prefixa o numero do carro ("#12 ALO").</summary>
    public bool? CarNumber { get; init; }
    /// <summary>Casas decimais do gap (0-3). Padrao 3.</summary>
    public int? GapDecimals { get; init; }
    /// <summary>Mostra o sinal +/- do gap. Padrao = o do widget (o 1998 nao usa "+" na lista).</summary>
    public bool? GapSign { get; init; }
    /// <summary>Sufixo "s" no gap em segundos.</summary>
    public bool? GapSuffix { get; init; }
    public LapTimeStyle? LapTime { get; init; }
    /// <summary>Casas decimais do tempo de volta (0-3). Padrao 3.</summary>
    public int? LapDecimals { get; init; }
    public SpeedUnit? Speed { get; init; }
    public TempUnit? Temp { get; init; }
    public FuelUnit? Fuel { get; init; }

    [JsonIgnore] public bool IsEmpty => this == Empty;
    public static readonly DisplayOptions Empty = new();

    /// <summary>Limita as casas decimais; devolve null quando tudo e padrao (o JSON nao ganha um bloco vazio).</summary>
    public DisplayOptions? Normalized()
    {
        var n = this with
        {
            GapDecimals = GapDecimals is { } g ? Math.Clamp(g, 0, MaxDecimals) : null,
            LapDecimals = LapDecimals is { } l ? Math.Clamp(l, 0, MaxDecimals) : null,
        };
        return n.IsEmpty ? null : n;
    }

    // ---- atalhos usados pelos widgets: padrao do widget quando o campo e nulo ----

    public string FormatName(string fullName, int carNumber, NameStyle widgetDefault)
        => DisplayFormat.Name(fullName, Name ?? widgetDefault, CarNumber == true ? carNumber : null);

    public string FormatGap(double seconds, bool defaultSign = true)
        => DisplayFormat.Gap(seconds, GapDecimals ?? MaxDecimals, GapSign ?? defaultSign, GapSuffix ?? false);

    public string FormatLaps(int laps, bool defaultSign = true) => DisplayFormat.Laps(laps, GapSign ?? defaultSign);

    public string NoGap => DisplayFormat.NoTime(GapDecimals ?? MaxDecimals);

    public string FormatLapTime(double seconds) => DisplayFormat.LapTime(seconds, LapTime ?? LapTimeStyle.MinSec, LapDecimals ?? MaxDecimals);

    [JsonIgnore] public SpeedUnit SpeedOrDefault => Speed ?? SpeedUnit.Kph;
    [JsonIgnore] public TempUnit TempOrDefault => Temp ?? TempUnit.Celsius;
    [JsonIgnore] public FuelUnit FuelOrDefault => Fuel ?? FuelUnit.Liters;
}

/// <summary>Formatadores puros (sem UI, cultura invariante) de nomes, gaps, tempos e unidades.</summary>
public static class DisplayFormat
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public const double MpsToKph = 3.6, MpsToMph = 2.2369362920544, LitersPerGallon = 3.785411784, KmPerMile = 1.609344;

    static string[] Words(string name) => name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Sobrenome em caixa alta no AMS2 ("Vitor COSTA") vira "Costa"; nomes mistos ficam como estao.</summary>
    static string FixCase(string word)
        => word.Length > 1 && word.All(ch => !char.IsLetter(ch) || char.IsUpper(ch)) ? char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant() : word;

    /// <summary>
    /// "Fernando Alonso" -> Code3 "ALO", Initials "FA", InitialLastName "F. Alonso", LastName "Alonso", FullName "Fernando Alonso".
    /// Sobrenome = tudo depois da primeira palavra. <paramref name="carNumber"/> prefixa "#N ".
    /// </summary>
    public static string Name(string fullName, NameStyle style, int? carNumber = null)
    {
        var w = Words(fullName);
        string s;
        if (w.Length == 0) s = "";
        else
        {
            string last = w.Length > 1 ? string.Join(' ', w[1..].Select(FixCase)) : FixCase(w[0]);
            s = style switch
            {
                NameStyle.Code3 => Code3(w[^1]),
                NameStyle.Initials => Initials(w),
                NameStyle.InitialLastName => w.Length > 1 ? char.ToUpperInvariant(w[0][0]) + ". " + last : last,
                NameStyle.LastName => last,
                _ => string.Join(' ', w.Take(1).Concat(w.Skip(1).Select(FixCase))),
            };
        }
        return carNumber is { } n ? "#" + n.ToString(Inv) + " " + s : s;
    }

    static string Initials(string[] words)
        => string.Concat(words.Select(x => x.FirstOrDefault(char.IsLetterOrDigit)).Where(ch => ch != default).Select(char.ToUpperInvariant));

    /// <summary>Sigla de 3 letras: inicio da ultima palavra, so letras, em maiusculas.</summary>
    public static string Code3(string lastWord)
    {
        var letters = new string(lastWord.Where(char.IsLetter).ToArray());
        return (letters.Length >= 3 ? letters[..3] : letters).ToUpperInvariant();
    }

    /// <summary>"0.000" com N casas (0 = inteiro, sem ponto).</summary>
    static string Fixed(double v, int decimals) => v.ToString(decimals <= 0 ? "0" : "0." + new string('0', decimals), Inv);

    /// <summary>
    /// Gap em segundos: "+1.234" (3 casas, com sinal). A partir de 60 s "+1:02.345". Sem sinal: valor absoluto. Sufixo "s" so abaixo de 60 s.
    /// Arredonda antes de decidir o formato (59.9996 com 3 casas vira "1:00.000").
    /// </summary>
    public static string Gap(double seconds, int decimals = 3, bool sign = true, bool suffix = false)
    {
        decimals = Math.Clamp(decimals, 0, 3);
        double scale = Math.Pow(10, decimals);
        double a = Math.Round(Math.Abs(seconds) * scale, MidpointRounding.AwayFromZero) / scale;
        string sg = !sign ? "" : seconds < 0 && a > 0 ? "-" : "+";
        if (a >= 60)
        {
            int m = (int)(a / 60);
            double rest = Math.Round((a - m * 60) * scale) / scale;
            string sec = rest.ToString(decimals <= 0 ? "00" : "00." + new string('0', decimals), Inv);
            return sg + m.ToString(Inv) + ":" + sec;
        }
        return sg + Fixed(a, decimals) + (suffix ? "s" : "");
    }

    /// <summary>Voltas de diferenca: "+1L" (ou "-1L"); sem sinal "1L".</summary>
    public static string Laps(int laps, bool sign = true)
        => (sign ? laps < 0 ? "-" : "+" : "") + Math.Abs(laps).ToString(Inv) + "L";

    /// <summary>Marcador sem tempo com o mesmo numero de casas: "--.---", "--.-", "--".</summary>
    public static string NoTime(int decimals) => decimals <= 0 ? "--" : "--." + new string('-', Math.Clamp(decimals, 1, 3));

    /// <summary>
    /// Tempo de volta: MinSec "1:23.456" (acima de 1 h "1:02:03.456"), Seconds "83.456". Sem tempo (&lt;= 0): "-:--.---" / "--.---".
    /// </summary>
    public static string LapTime(double seconds, LapTimeStyle style = LapTimeStyle.MinSec, int decimals = 3)
    {
        decimals = Math.Clamp(decimals, 0, 3);
        string frac = decimals > 0 ? "." + new string('-', decimals) : "";
        if (!(seconds > 0) || double.IsInfinity(seconds)) return style == LapTimeStyle.Seconds ? "--" + frac : "-:--" + frac;
        double scale = Math.Pow(10, decimals);
        long units = (long)Math.Round(seconds * scale, MidpointRounding.AwayFromZero);
        long whole = units / (long)scale, part = units % (long)scale;
        string fs = decimals > 0 ? "." + part.ToString(new string('0', decimals), Inv) : "";
        if (style == LapTimeStyle.Seconds) return whole.ToString(Inv) + fs;
        long h = whole / 3600, m = whole / 60 % 60, s = whole % 60;
        return h > 0 ? $"{h.ToString(Inv)}:{m.ToString("00", Inv)}:{s.ToString("00", Inv)}{fs}" : $"{m.ToString(Inv)}:{s.ToString("00", Inv)}{fs}";
    }

    // ---- unidades ----

    public static double Speed(double mps, SpeedUnit unit) => unit == SpeedUnit.Mph ? mps * MpsToMph : mps * MpsToKph;
    public static double SpeedFromKph(double kph, SpeedUnit unit) => unit == SpeedUnit.Mph ? kph / KmPerMile : kph;
    public static string SpeedLabel(SpeedUnit unit) => unit == SpeedUnit.Mph ? "MPH" : "KPH";
    public static string SpeedLabelShort(SpeedUnit unit) => unit == SpeedUnit.Mph ? "mph" : "km/h";
    public static double Distance(double km, SpeedUnit unit) => unit == SpeedUnit.Mph ? km / KmPerMile : km;
    public static string DistanceLabel(SpeedUnit unit) => unit == SpeedUnit.Mph ? "mi" : "Km";

    public static double Temp(double celsius, TempUnit unit) => unit == TempUnit.Fahrenheit ? celsius * 9 / 5 + 32 : celsius;
    public static string TempLabel(TempUnit unit) => unit == TempUnit.Fahrenheit ? "°F" : "°C";

    public static double Fuel(double liters, FuelUnit unit) => unit == FuelUnit.Gallons ? liters / LitersPerGallon : liters;
    public static string FuelLabel(FuelUnit unit) => unit == FuelUnit.Gallons ? "GAL" : "L";
}
