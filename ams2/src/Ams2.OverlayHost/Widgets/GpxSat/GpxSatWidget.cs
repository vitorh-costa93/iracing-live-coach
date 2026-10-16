using Ams2.Core.Calc.GpxSat;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;
using Vortice.Win32.Numerics;

namespace Ams2.OverlayHost.Widgets.GpxSat;

/// <summary>Widget que precisa ver todo quadro do provider (historico de passagens), mesmo quando nao desenha (previa --png).</summary>
public interface IGpxFrameObserver
{
    void Observe(OverlayModel model);
}

/// <summary>
/// Widget unico do GPxSAT ("gpxsat"): camada de tela inteira (1920x1080 de projeto) onde o modo do tema desenha as telas do GPxPatch no
/// espaco 1024x768 (pillarbox 4:3 por padrao). Um animador por slot ("top" = time left/lap counter, "main" = telas de baixo).
/// </summary>
public sealed class GpxSatWidget : IWidget, IGpxFrameObserver, IDisposable
{
    public string Id => "gpxsat";
    public (float Width, float Height) DesignSize => (1920, 1080);
    public bool HighFrequency => true;   // animacao a 20 ms por passo

    IGpxMode _mode = GpxModeRegistry.Create(null);
    WidgetSettings _cfg = new() { Id = "gpxsat" };
    GpxOptions _opt = new();
    readonly GpxSkyTracker _sky = new();
    readonly GpxSplitTracker _split = new();
    readonly GpxTrackMap _map = new();
    readonly Dictionary<string, GpxAnimator> _anims = new(StringComparer.Ordinal);
    readonly GpxSurface _surface = new();
    long _lastFrame = -1;
    double _sessionSeen = double.NaN;
    uint _sequence;
    bool _seenSequence;

    public IGpxMode Mode => _mode;

    public void UseTheme(Theme.Theme theme)
    {
        var next = GpxModeRegistry.Create(theme.Id);
        if (next.Id == _mode.Id) return;
        _mode = next; _anims.Clear();
    }

    public void Configure(WidgetSettings settings)
    {
        _cfg = settings;
        _opt = GpxOptions.From(settings, _mode.Id == "classic");
        _sky.HoldSeconds = _opt.SkySeconds;
        _split.HoldSeconds = _opt.SplitSeconds;
    }

    public bool IsIdle(OverlayModel model) => !model.Connected || _anims.Count == 0;

    public void Observe(OverlayModel m)
    {
        if (!m.Connected || m.Session is not { } s)
        {
            _sky.Reset(); _split.Reset(); _map.Reset(); _anims.Clear(); _lastFrame = -1; _seenSequence = false; _sessionSeen = double.NaN;
            return;
        }
        if (m.Frame == _lastFrame) return;
        _lastFrame = m.Frame;
        double seen = m.Broadcast?.SessionSeenT ?? double.NegativeInfinity;
        if ((_seenSequence && s.Sequence < _sequence) || (!double.IsNaN(_sessionSeen) && seen != _sessionSeen))
        { _sky.Reset(); _split.Reset(); _anims.Clear(); }
        _sessionSeen = seen; _sequence = s.Sequence; _seenSequence = true;
        if (!s.InSession) return;
        _sky.Update(m.Now, s);
        _split.Update(m.Now, s);
        _map.Observe(s);
    }

    public void Draw(ThemeCanvas canvas, OverlayModel m)
    {
        Observe(m);
        if (!m.Connected || m.Session is not { } s) return;
        var ctx = new GpxContext
        {
            Model = m, Session = s, Options = _opt, Settings = _cfg, G = new GpxGeometry(_mode.Spec.FH), Sky = _sky, Split = _split,
        };
        var screens = _mode.Dispatch(ctx).Concat(ctx.Previewing ? [] : GpxSharedScreens.Dispatch(ctx)).ToArray();
        if (Environment.GetEnvironmentVariable("GPX_DEBUG") == "1") Console.Error.WriteLine($"[GPX] prev={_opt.PreviewScreen} now={m.Now} screens={string.Join(",", screens.Select(x => x.Slot + ":" + x.Key + ":" + x.Boxes.Count))}");
        var bySlot = screens.GroupBy(x => x.Slot).Select(g => g.OrderByDescending(x => x.Priority).First()).ToList();
        foreach (var gone in _anims.Keys.Where(k => bySlot.All(x => x.Slot != k)).ToList()) _anims.Remove(gone);
        bool mapVisible = !ctx.Previewing && _map.Ready && (_cfg.Option("trackMap") == "always" || (_cfg.Option("trackMap") is null or "auto") && s.FlagColour is 6 or 7);
        if (bySlot.Count == 0 && !mapVisible) return;

        _surface.Begin(canvas, DesignSize.Width, DesignSize.Height, _mode.Spec, _opt.Stretch, _opt.OriginalFonts, _cfg.TextScale ?? 1f);
        try
        {
            if (mapVisible) _surface.TrackMap(_map, s);
            foreach (var sc in bySlot)
            {
                if (!_anims.TryGetValue(sc.Slot, out var a)) _anims[sc.Slot] = a = new GpxAnimator();
                a.StepMs = _opt.StepMs;
                a.LogoMs = _mode.Id == "digital2010" ? 8000 : 6000;
                a.Update(sc.Key, sc.Page, sc.Groups, sc.Anim, m.Now);
                if (ctx.Previewing) a.StartAt(m.Now - (_opt.PreviewMs >= 0 ? _opt.PreviewMs / 1000 : a.SettleSeconds + 0.001));
                DrawScreen(sc, a, ctx.G, m.Now);
            }
        }
        finally { _surface.End(canvas); }
    }

    void DrawScreen(GpxScreen sc, GpxAnimator a, GpxGeometry g, double now)
    {
        bool fadeDone = a.FadeDone(now);
        float fade = a.FadePct(now) / 100f;
        var (logoPhase, logoRot) = a.Logo(now, _opt.ShowLogo);
        foreach (var b in sc.Boxes)
        {
            var r = b.Rect;
            if (b.Logo)
            {
                if (logoPhase == GpxLogoPhase.Hidden) continue;
                if (logoPhase == GpxLogoPhase.Flip) { _surface.FillTexture(_surface.ToDevice(Flip(r, logoRot, g.FH)), GpxTextures.White); continue; }
                DrawFinal(b, r, g, 1f);
                continue;
            }
            if (!fadeDone)
            {
                // Fase 1: wipe branco da esquerda para a direita (largura x FADE%).
                _surface.FillTexture(_surface.ToDevice(r with { W = r.W * fade }), GpxTextures.White);
                continue;
            }
            int rot = a.RotPct(b.Group, now);
            if (a.Settled(b.Group, now)) { DrawFinal(b, r, g, b.Alpha); continue; }
            if (rot >= 100) { _surface.FillTexture(_surface.ToDevice(r), GpxTextures.White); continue; }   // grupo ainda nao comecou
            if (sc.Anim == GpxAnimKind.Columns)
                _surface.FillTexture(_surface.ToDevice(r with { H = r.H * rot / 100f }), GpxTextures.White, rot / 100f);
            else
                _surface.FillTexture(_surface.ToDevice(Flip(r, rot, g.FH)), GpxTextures.White);
        }
    }

    /// <summary>Flip vertical da linha: T += tri*FH/100, B -= tri*FH/100.</summary>
    static GpxRect Flip(GpxRect r, int rot, float fh)
    {
        float d = GpxAnimator.Tri(rot) * fh / 100f;
        return new GpxRect(r.L, r.T + d, r.W, Math.Max(0, r.H - 2 * d));
    }

    void DrawFinal(GpxBox b, GpxRect r, GpxGeometry g, float alpha)
    {
        RectF dev = _surface.ToDevice(r);
        bool artwork = _surface.Artwork(dev, b.Asset, b.SourceRect, alpha);
        if (!artwork) switch (b.Image)
        {
            case GpxImage.Flag: _surface.CheckeredFlag(dev, alpha); return;
            case GpxImage.TyreM: _surface.TyreBadge(dev, 'M', alpha); return;
            case GpxImage.TyreB: _surface.TyreBadge(dev, 'B', alpha); return;
            case GpxImage.CarNumber:
            case GpxImage.Position:
            case GpxImage.Winner:
            case GpxImage.BigOne: _surface.Badge(dev, b.Image, b.Text, b.Argb, alpha); return;
        }
        if (!artwork) _surface.FillTexture(dev, b.Texture, alpha, b.Horizontal, b.Slant);
        if (b.Stroke != 0) _surface.Stroke(dev, b.Stroke);
        if (artwork && b.Image != GpxImage.None) return; // original atlas already contains its numeral/emblem
        if (!string.IsNullOrEmpty(b.Text))
            _surface.Text(dev, b.Text, b.Align, b.Argb, float.IsNaN(b.Indent) ? g.Indent : b.Indent, b.Font, shadow: b.Shadow, topAligned: b.TopAligned);
    }

    public void Dispose() => _surface.Dispose();
}
