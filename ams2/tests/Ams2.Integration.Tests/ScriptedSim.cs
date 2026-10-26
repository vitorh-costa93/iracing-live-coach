using Ams2.Core.Calc;
using Ams2.Core.Reading;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Gfx;
using Ams2.OverlayHost.Theme;
using Ams2.OverlayHost.Widgets.GpxSat;
using Ams2.Shared.Profiles;

namespace Ams2.Integration.Tests;

/// <summary>
/// Simulacao roteirizada: o provider real (trackers reais) alimentado pelo escritor falso com relogio simulado, e um GpxSatWidget real por tema.
/// Avanca o tempo em passos fixos, registra o que cada tema mostra no slot principal e, se pedido, renderiza quadros PNG (recorte do slot).
/// </summary>
public sealed class ScriptedSim : IDisposable
{
    public static readonly string[] GpxThemes = ["f1-1998", "f1-1999-digital", "f1-2004", "f1-2012"];

    public double T;
    readonly OverlayDataProvider _provider;
    public readonly Dictionary<string, GpxSatWidget> Widgets = [];
    public OverlayModel Model => _provider.Current;

    public ScriptedSim(bool quali, Func<IRawMemorySource, Func<double>, IRawMemorySource>? decorate = null, string[]? themes = null, bool board = true)
    {
        IRawMemorySource src = new FakeRawSource(() => T, board: board && !quali, quali: quali);
        if (decorate is not null) src = decorate(src, () => T);
        _provider = new OverlayDataProvider(src, () => T, names: Ams2.Shared.PlayerNames.PlayerNameStore.InMemory(), launch: LaunchStore.InMemory());
        foreach (var id in themes ?? GpxThemes)
        {
            var theme = Themes.Get(id);
            var w = new GpxSatWidget();
            w.UseTheme(theme);
            w.Configure(ProfileFactory.CreateDefault("sim", id, 1920, 1080).Get("gpxsat")!.Normalized(id));
            Widgets[id] = w;
        }
    }

    /// <summary>Liga opcoes do gpxsat (ex.: showTimeLeft) num tema, como o Control Center faria.</summary>
    public void Option(string theme, string key, string value)
    {
        var cfg = ProfileFactory.CreateDefault("sim", theme, 1920, 1080).Get("gpxsat")!.Normalized(theme);
        Widgets[theme].Configure(cfg with { Options = new Dictionary<string, string>(cfg.Options ?? [], StringComparer.OrdinalIgnoreCase) { [key] = value } });
    }

    /// <summary>Avanca ate <paramref name="until"/> em passos de <paramref name="dt"/>; <paramref name="each"/> roda a cada passo (depois do Tick e do Observe).</summary>
    public void Run(double until, double dt, Action<double>? each = null)
    {
        while (T < until - 1e-9)
        {
            T += dt;
            var m = _provider.Tick();
            foreach (var w in Widgets.Values) w.Observe(m);
            each?.Invoke(T);
        }
    }

    public record Shown(double T, string Key, string? Piece, float Alpha, string Texts, int Page = 0);

    /// <summary>Tela do slot <paramref name="slot"/> (maior prioridade ja escolhida pelo widget) no modelo atual; null = vazio.</summary>
    public Shown? Main(string theme, string slot = "main")
    {
        var m = Model;
        var hit = Widgets[theme].Screens(m).Where(s => s.Screen.Slot == slot).Select(s => ((GpxScreen Screen, float Alpha)?)s).FirstOrDefault();
        if (hit is not { } h) return null;
        return new Shown(m.Now, h.Screen.Key, GpxPieces.Of(h.Screen, m.Session?.PlayerCar?.Index), h.Alpha,
            string.Join("|", h.Screen.Boxes.Select(b => b.Text).Where(x => !string.IsNullOrEmpty(x))), h.Screen.Page);
    }

    /// <summary>Renderiza o widget do tema no modelo atual (1920x1080); devolve BGRA premultiplicado.</summary>
    public byte[] Render(string theme)
    {
        var w = Widgets[theme];
        using var gfx = DeviceResources.CreateOffscreen(1920, 1080);
        using var canvas = new ThemeCanvas(gfx, Themes.Get(theme));
        gfx.BeginFrame(); canvas.Begin(); w.Draw(canvas, Model); canvas.End(); gfx.EndFrame();
        return gfx.ReadPixelsBgra();
    }

    /// <summary>Pixels nao transparentes na faixa de baixo do slot principal (y 860..1080).</summary>
    public static int Painted(byte[] bgra, int y0 = 860, int y1 = 1080)
    {
        int n = 0;
        for (int y = y0; y < y1; y++) for (int x = 0; x < 1920; x++) if (bgra[(y * 1920 + x) * 4 + 3] > 8) n++;
        return n;
    }

    public static string Hash(byte[] bgra, int y0 = 860, int y1 = 1080)
        => Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(bgra.AsSpan(y0 * 1920 * 4, (y1 - y0) * 1920 * 4)))[..10];

    /// <summary>Salva o recorte (x 240..1680, y0..y1) em <c>AMS2_SIM_OUT</c> (so se a variavel existir).</summary>
    public static void Save(string name, byte[] bgra, int y0 = 860, int y1 = 1080)
    {
        string? dir = Environment.GetEnvironmentVariable("AMS2_SIM_OUT");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        const int x0 = 240, w = 1440;
        int h = y1 - y0;
        var crop = new byte[w * h * 4];
        for (int y = 0; y < h; y++) Buffer.BlockCopy(bgra, ((y0 + y) * 1920 + x0) * 4, crop, y * w * 4, w * 4);
        PngWriter.SaveFromPremultipliedBgra(Path.Combine(dir, name + ".png"), crop, w, h, (0x5A, 0x60, 0x55));
    }

    public void Dispose()
    {
        foreach (var w in Widgets.Values) w.Dispose();
        _provider.Dispose();
    }
}
