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
    // Janela de tela inteira (1920x1080). No ritmo de vblank so enquanto anima ou um cronometro corre, e com divisor (N = refresh/60:
    // 3 a 165 Hz) para nao disputar o vblank com Inputs/Radar; parada (IsIdle) so apresenta quando a assinatura muda e o DComp
    // mantem o ultimo quadro. A apresentacao avisa so a regiao alterada (Present1 com retangulos).
    public bool HighFrequency => true;
    public double VblankTargetHz => 60;
    public double LowRateHz => 30;
    const double ActiveHoldSeconds = 1.5;
    static readonly bool Debug = Environment.GetEnvironmentVariable("GPX_DEBUG") == "1";

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

    // Despacho em cache por modelo (as telas so dependem do modelo e das opcoes); _content muda quando o que se ve muda.
    readonly List<GpxScreen> _slots = [], _prevSlots = [];
    // Alfa de cada tela de _slots pela peca da Exibicao (1 = sem gatilho proprio); _show = null quando nenhuma peca esta configurada.
    readonly List<float> _alphas = [];
    string? _themeId;
    ShowPlan? _show;
    int _showVersion = -1;
    readonly List<int> _groups = [];
    readonly List<string> _gone = [];
    OverlayModel? _plannedFor;
    int _cfgVersion, _plannedVersion = -1;
    GpxContext? _ctx;
    bool _mapVisible;
    long _mapFrame = -1, _content;
    double _changedAt = double.NegativeInfinity;

    public IGpxMode Mode => _mode;

    /// <summary>Telas escolhidas para <paramref name="model"/> (uma por slot) e o alfa da peca de cada uma (testes/diagnostico).</summary>
    public IReadOnlyList<(GpxScreen Screen, float Alpha)> Screens(OverlayModel model)
    {
        Plan(model);
        return _slots.Select((s, i) => (s, _alphas[i])).ToArray();
    }

    /// <summary>Relogio da animacao: o do amostrador (tempo real, como o radar); sem ele, o do provider.</summary>
    static double Clock(OverlayModel m) => m.Inputs?.Now ?? m.Now;

    public void UseTheme(Theme.Theme theme)
    {
        if (_themeId != theme.Id) { _themeId = theme.Id; _cfgVersion++; }
        var next = GpxModeRegistry.Create(theme.Id);
        if (next.Id == _mode.Id) return;
        _mode = next; _anims.Clear(); _cfgVersion++;
    }

    public void Configure(WidgetSettings settings)
    {
        _cfgVersion++;
        _cfg = settings;
        _opt = GpxOptions.From(settings, _mode.Id == "classic") with { MixedCaseNames = _mode.Id is "digital2004" or "digital2010" };
        _sky.HoldSeconds = _opt.SkySeconds;
        _split.HoldSeconds = _opt.SplitSeconds;
    }

    /// <summary>Fora do ritmo de vblank: nada animando e nada mudou ha <see cref="ActiveHoldSeconds"/> (cronometro parado ou sem tela).</summary>
    public bool IsIdle(OverlayModel model)
    {
        if (!model.Connected) return true;
        Plan(model);
        double now = Clock(model);
        return !(now - _changedAt < ActiveHoldSeconds || Animating(now) || _alphas.Exists(a => a < 1f));
    }

    public long? FrameSignature(OverlayModel model)
    {
        Plan(model);
        double now = Clock(model);
        Sync(now);
        long sig = _content;
        foreach (var sc in _slots)
        {
            var a = _anims[sc.Slot];
            var phase = a.Logo(now, _opt.ShowLogo).Phase;
            long part = !a.AllSettled(now) || phase == GpxLogoPhase.Flip ? BitConverter.DoubleToInt64Bits(now) : (long)phase;
            sig = unchecked(sig * 1_000_003 + part);
        }
        foreach (float alpha in _alphas) if (alpha < 1f) sig = unchecked(sig * 1_000_003 + BitConverter.SingleToInt32Bits(alpha));
        return sig;
    }

    public bool DirtyRegion(List<Ams2.Core.Calc.PixelRect> rects)
    {
        _surface.Dirty.Collect(rects);
        return true;
    }

    bool Animating(double now)
    {
        foreach (var sc in _slots)
        {
            if (!_anims.TryGetValue(sc.Slot, out var a)) return true;
            if (!a.AllSettled(now) || a.Logo(now, _opt.ShowLogo).Phase == GpxLogoPhase.Flip) return true;
        }
        return false;
    }

    /// <summary>Despacha as telas do modelo (uma vez por modelo/configuracao): a de maior prioridade por slot, mapa, versao do conteudo.</summary>
    void Plan(OverlayModel m)
    {
        if (ReferenceEquals(m, _plannedFor) && _plannedVersion == _cfgVersion) return;
        _plannedFor = m; _plannedVersion = _cfgVersion;
        Observe(m);
        _prevSlots.Clear(); _prevSlots.AddRange(_slots);
        _slots.Clear(); _groups.Clear(); _alphas.Clear();
        bool wasMap = _mapVisible;
        _mapVisible = false; _ctx = null;
        if (m.Connected && m.Session is { } s)
        {
            var ctx = _ctx = new GpxContext
            {
                Model = m, Session = s, Options = _opt, Settings = _cfg, G = new GpxGeometry(_mode.Spec.FH), Sky = _sky, Split = _split,
            };
            var debug = Debug ? new System.Text.StringBuilder() : null;
            var show = Show();
            // Peca com gatilho proprio: janela do evento/ciclo no relogio do provider (previa = conteudo sempre).
            var gated = show is null || ctx.Previewing ? show : show.At(m.Now, id => m.Events?.Last(id), of => m.Events?.Laps(of) ?? 0);
            foreach (var sc in _mode.Dispatch(ctx)) Pick(sc, gated, ctx, debug);
            if (!ctx.Previewing) foreach (var sc in GpxSharedScreens.Dispatch(ctx)) Pick(sc, gated, ctx, debug);
            if (debug is not null) Console.Error.WriteLine($"[GPX] prev={_opt.PreviewScreen} now={m.Now} screens={debug}");
            string? map = _cfg.Option("trackMap");
            _mapVisible = !ctx.Previewing && _map.Ready && (map == "always" || (map is null or "auto") && s.FlagColour is 6 or 7);
            foreach (var sc in _slots) _groups.Add(Groups(sc));
        }
        _gone.Clear();
        foreach (var k in _anims.Keys) if (!HasSlot(k)) _gone.Add(k);
        foreach (var k in _gone) _anims.Remove(k);
        if (_mapVisible != wasMap || (_mapVisible && m.Frame != _mapFrame) || !SameScreens()) { _content++; _changedAt = Clock(m); }
        _mapFrame = _mapVisible ? m.Frame : -1;
    }

    void Pick(GpxScreen sc, ShowPlan? show, GpxContext ctx, System.Text.StringBuilder? debug)
    {
        if (debug is not null) debug.Append(debug.Length == 0 ? "" : ",").Append(sc.Slot).Append(':').Append(sc.Key).Append(':').Append(sc.Boxes.Count);
        // Peca desligada (ou fora da janela do gatilho, ou de outra janela de peca): a tela sai e o slot fica para a seguinte.
        float alpha = show is not null && GpxPieces.Of(sc, ctx.Focus?.Index) is { } piece ? show.PieceAlpha(piece) : 1f;
        if (alpha <= 0.01f) return;
        for (int i = 0; i < _slots.Count; i++)
        {
            if (_slots[i].Slot != sc.Slot) continue;
            if (sc.Priority > _slots[i].Priority) { _slots[i] = sc; _alphas[i] = alpha; }
            return;
        }
        _slots.Add(sc); _alphas.Add(alpha);
    }

    /// <summary>Exibicao das pecas (null = perfil sem nada configurado nas pecas: o despacho fica identico ao do GPxPatch).</summary>
    ShowPlan? Show()
    {
        if (_showVersion != _cfgVersion)
        {
            _showVersion = _cfgVersion;
            var plan = ShowPlan.For(_cfg, _themeId);
            bool configured = plan.Def.Pieces.Count > 0 && (_cfg.Show?.Pieces is { Count: > 0 } || plan.Solo is not null
                || plan.Def.Pieces.Any(p => plan.PieceTrigger(p.Id) != ShowTriggers.Native)
                || _cfg.Option(RenderSettings.PiecesDetachedOption) is { Length: > 0 });
            _show = configured ? plan : null;
        }
        return _show;
    }

    bool HasSlot(string slot)
    {
        foreach (var sc in _slots) if (sc.Slot == slot) return true;
        return false;
    }

    static int Groups(GpxScreen sc)
    {
        int max = -1;
        for (int i = 0; i < sc.Boxes.Count; i++) if (!sc.Boxes[i].Logo && sc.Boxes[i].Group > max) max = sc.Boxes[i].Group;
        return sc.Boxes.Count == 0 ? 0 : max + 1;
    }

    bool SameScreens()
    {
        if (_slots.Count != _prevSlots.Count) return false;
        for (int i = 0; i < _slots.Count; i++)
        {
            GpxScreen a = _slots[i], b = _prevSlots[i];
            if (ReferenceEquals(a, b)) continue;
            if (a.Slot != b.Slot || a.Key != b.Key || a.Page != b.Page || a.Anim != b.Anim || a.Boxes.Count != b.Boxes.Count) return false;
            for (int j = 0; j < a.Boxes.Count; j++) if (!a.Boxes[j].Equals(b.Boxes[j])) return false;
        }
        return true;
    }

    /// <summary>Animadores das telas atuais no relogio <paramref name="now"/> (Update e idempotente para a mesma tela).</summary>
    void Sync(double now)
    {
        if (_ctx is null) return;
        for (int i = 0; i < _slots.Count; i++)
        {
            var sc = _slots[i];
            if (!_anims.TryGetValue(sc.Slot, out var a)) _anims[sc.Slot] = a = new GpxAnimator();
            a.StepMs = _opt.StepMs;
            a.LogoMs = _mode.Id == "digital2010" ? 8000 : 6000;
            a.Update(sc.Key, sc.Page, _groups[i], sc.Anim, now);
            if (_ctx.Previewing) a.StartAt(now - (_opt.PreviewMs >= 0 ? _opt.PreviewMs / 1000 : a.SettleSeconds + 0.001));
        }
    }

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
        _surface.Dirty.NewFrame();
        Plan(m);
        if (_ctx is not { } ctx || (_slots.Count == 0 && !_mapVisible)) return;
        double now = Clock(m);
        Sync(now);
        _surface.Begin(canvas, DesignSize.Width, DesignSize.Height, _mode.Spec, _opt.Stretch, _opt.OriginalFonts, _cfg.TextScale ?? 1f);
        try
        {
            if (_mapVisible) _surface.TrackMap(_map, ctx.Session);
            for (int i = 0; i < _slots.Count; i++)
            {
                _surface.ScreenAlpha(_alphas[i]);
                DrawScreen(_slots[i], _anims[_slots[i].Slot], ctx.G, now);
            }
            _surface.ScreenAlpha(1f);
        }
        finally { _surface.End(canvas); }
    }

    void DrawScreen(GpxScreen sc, GpxAnimator a, GpxGeometry g, double now)
    {
        bool fadeDone = a.FadeDone(now);
        float fade = a.FadeFrac(now);
        var (logoPhase, logoRot) = a.LogoFrac(now, _opt.ShowLogo);
        for (int i = 0; i < sc.Boxes.Count; i++)
        {
            var b = sc.Boxes[i];
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
                // Fase 1: wipe branco da esquerda para a direita (largura x FADE%), continuo entre os passos.
                _surface.FillTexture(_surface.ToDevice(r with { W = r.W * fade }), GpxTextures.White);
                continue;
            }
            if (a.Settled(b.Group, now)) { DrawFinal(b, r, g, b.Alpha); continue; }
            float rot = a.RotFrac(b.Group, now);
            if (rot >= 1) { _surface.FillTexture(_surface.ToDevice(r), GpxTextures.White); continue; }   // grupo ainda nao comecou
            if (sc.Anim == GpxAnimKind.Columns)
                _surface.FillTexture(_surface.ToDevice(r with { H = r.H * rot }), GpxTextures.White, rot);
            else
                _surface.FillTexture(_surface.ToDevice(Flip(r, rot, g.FH)), GpxTextures.White);
        }
    }

    /// <summary>Flip vertical da linha: T += tri*FH, B -= tri*FH (rot e tri em fracao).</summary>
    static GpxRect Flip(GpxRect r, float rot, float fh)
    {
        float d = GpxAnimator.TriFrac(rot) * fh;
        return new GpxRect(r.L, r.T + d, r.W, Math.Max(0, r.H - 2 * d));
    }

    void DrawFinal(GpxBox b, GpxRect r, GpxGeometry g, float alpha)
    {
        RectF dev = _surface.ToDevice(r);
        bool artwork = _surface.Artwork(dev, b.Asset, b.SourceRect, alpha);
        if (!artwork) switch (b.Image)
        {
            case GpxImage.Flag: _surface.CheckeredFlag(dev, alpha); return;
            case GpxImage.CountryFlag: _surface.CountryFlag(dev, b.Text, alpha); return;
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
