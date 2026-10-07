using System.Collections.Concurrent;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Native;
using Ams2.OverlayHost.Widgets;
using Ams2.Shared.Ipc;
using Ams2.Shared.Profiles;
using HostTheme = Ams2.OverlayHost.Theme.Theme;
using Themes = Ams2.OverlayHost.Theme.Themes;

namespace Ams2.OverlayHost.Host;

/// <summary>
/// Dono das janelas dos widgets, do perfil ativo e do canal IPC. O IPC chega em threads de fundo: os comandos entram numa fila e
/// são executados na thread principal (a que bombeia as mensagens das janelas), que responde ao cliente. Assim nenhuma janela é
/// tocada fora da sua thread. Posições/escala mudadas pelo usuário (modo de edição) e por comandos são salvas com atraso curto.
/// </summary>
internal sealed class HostController : IDisposable
{
    const int SnapGrid = 10, SnapEdge = 10;
    static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(400);

    readonly OverlayDataProvider _provider;
    readonly ProfileStore _store;
    readonly bool _fake;
    readonly bool _persist;
    readonly Dictionary<string, WidgetWindow> _windows = [];
    readonly ConcurrentQueue<Action> _queue = new();
    readonly HashSet<string>? _only;
    IpcServer? _ipc;
    Profile _profile;
    HostTheme _theme;
    bool _edit;
    bool _gate = true; // espelha o estado das janelas (WidgetWindow nasce aberta)
    string? _session;  // grupo da sessao atual (filtro WidgetSettings.Sessions); null = nao filtra
    DateTime? _saveAt;

    public HostController(OverlayDataProvider provider, ProfileStore store, bool fake, string? themeId, string? profileName, string[]? onlyWidgets, bool persist)
    {
        _provider = provider; _store = store; _fake = fake; _persist = persist;
        _only = onlyWidgets is { Length: > 0 } ? new HashSet<string>(onlyWidgets, StringComparer.OrdinalIgnoreCase) : null;
        int sw = Win32.GetSystemMetrics(0), sh = Win32.GetSystemMetrics(1);
        string themeWanted = themeId ?? store.GetActiveTheme();
        _theme = Themes.Get(themeWanted);
        string name = profileName ?? store.GetActiveProfile(_theme.Id, sw, sh);
        _profile = store.Load(_theme.Id, name, sw, sh) ?? ProfileFactory.CreateDefault(name, _theme.Id, sw, sh).Normalized(sw, sh);
        BuildWindows();
        ApplyRadarOptions();
    }

    /// <summary>Alcance e sensibilidade do radar (perfil) vao para o tracker do provider; vale no proximo passo de 60 Hz.</summary>
    void ApplyRadarOptions()
    {
        if (_profile.Get("radar") is { } s) _provider.Radar.Options = RadarWidget.OptionsFor(s);
    }

    public Profile Profile => _profile;
    internal IEnumerable<(string Id, Ams2.Core.Calc.RateStats Stats)> RenderStats => _windows.Select(kv => (kv.Key, kv.Value.RenderStats));
    internal IEnumerable<(string Id, string Timing)> RenderTimings => _windows.Select(kv => (kv.Key, kv.Value.RenderTiming));

    void BuildWindows()
    {
        foreach (var s in _profile.Ordered)
        {
            if (_only is not null && !_only.Contains(s.Id)) continue;
            var w = new WidgetWindow(s.Id, s, _theme) { Snap = SnapToOthers };
            w.UserChanged += OnUserChanged;
            _windows[s.Id] = w;
        }
        RestackByOrder();
    }

    void RestackByOrder()
    {
        foreach (var s in _profile.Ordered)
            if (_windows.TryGetValue(s.Id, out var w)) w.BringToTop();
    }

    /// <summary>Aplica uma alteracao local (linha de comando) sem passar pelo IPC.</summary>
    public void ApplyPatch(string widgetId, WidgetPatch patch)
    {
        var cur = _profile.Get(widgetId);
        if (cur is not null) UpdateWidget(patch.ApplyTo(cur, _theme.Id), orderChanged: false);
    }

    public void SetEditModeDirect(bool edit) => SetEditMode(edit);

    public void StartIpc(string pipeName)
    {
        _ipc = new IpcServer(pipeName, HandleOnIpcThread);
        // Lista de modelos/nomes mudou (novo carro detectado, edicao do arquivo, comando): avisa o Control Center.
        if (_provider.Names is { } names) names.Changed += () => _queue.Enqueue(() => _ipc?.Broadcast(new IpcMessage { Event = IpcEvents.StateChanged, State = BuildState() }));
    }

    // ---- IPC ----

    IpcMessage HandleOnIpcThread(IpcMessage req)
    {
        var done = new ManualResetEventSlim();
        IpcMessage? result = null;
        _queue.Enqueue(() =>
        {
            try { result = Execute(req); }
            catch (Exception ex) { result = new IpcMessage { Ok = false, Error = ex.Message }; }
            finally { done.Set(); }
        });
        if (!done.Wait(3000)) return new IpcMessage { Ok = false, Error = "Host ocupado." };
        return result!;
    }

    IpcMessage Execute(IpcMessage req)
    {
        switch (req.Cmd)
        {
            case IpcCommands.GetState:
                break;
            case IpcCommands.ApplyProfile:
                ApplyProfileCommand(req);
                break;
            case IpcCommands.SetWidget:
            {
                if (req.Widget is null || req.Patch is null) return Fail("setWidget exige Widget e Patch.");
                var cur = _profile.Get(req.Widget);
                if (cur is null) return Fail($"Widget desconhecido: {req.Widget}.");
                var next = req.Patch.ApplyTo(cur, _theme.Id);
                UpdateWidget(next, orderChanged: next.Order != cur.Order);
                break;
            }
            case IpcCommands.SetTheme:
            {
                SaveNow(); // grava o que estiver pendente antes de trocar de perfil
                var def = ThemeCatalog.Find(req.Theme);
                if (def is null || !def.Available || !Themes.All.Any(t => t.Id == def.Id)) return Fail($"Tema indisponivel: {req.Theme}.");
                int sw = Win32.GetSystemMetrics(0), sh = Win32.GetSystemMetrics(1);
                string name = _store.GetActiveProfile(def.Id, sw, sh);
                SwitchProfile(_store.Load(def.Id, name, sw, sh) ?? ProfileFactory.CreateDefault(name, def.Id, sw, sh));
                break;
            }
            case IpcCommands.SetEditMode:
                SetEditMode(req.Edit ?? false);
                break;
            case IpcCommands.GetPlayerNames:
                break;
            case IpcCommands.SetPlayerName:
            {
                if (_provider.Names is not { } names) return Fail("Nomes de exibicao indisponiveis.");
                if (string.IsNullOrWhiteSpace(req.Model)) return Fail("setPlayerName exige Model.");
                names.Set(req.Model, req.Name);
                break;
            }
            case IpcCommands.ClearPlayerName:
            {
                if (_provider.Names is not { } names) return Fail("Nomes de exibicao indisponiveis.");
                if (string.IsNullOrWhiteSpace(req.Model)) return Fail("clearPlayerName exige Model.");
                names.Clear(req.Model);
                break;
            }
            case IpcCommands.ApplySuggestedNames:
                _provider.Names?.ApplySuggestedToUnnamed();
                break;
            default:
                return Fail($"Comando desconhecido: {req.Cmd}.");
        }
        return new IpcMessage { State = BuildState() };
    }

    static IpcMessage Fail(string error) => new() { Ok = false, Error = error };

    void ApplyProfileCommand(IpcMessage req)
    {
        SaveNow();
        int sw = Win32.GetSystemMetrics(0), sh = Win32.GetSystemMetrics(1);
        Profile p;
        if (req.Data is not null)
        {
            p = req.Data.Normalized(sw, sh);
            if (!Themes.All.Any(t => t.Id == p.ThemeId)) throw new InvalidOperationException($"Tema indisponivel: {p.ThemeId}.");
            if (_persist) _store.Save(p);
        }
        else
        {
            string themeId = req.Theme ?? _theme.Id;
            if (req.Profile is null) throw new InvalidOperationException("applyProfile exige Profile ou Data.");
            p = _store.Load(themeId, req.Profile, sw, sh) ?? throw new InvalidOperationException($"Perfil nao encontrado: {req.Profile}.");
        }
        SwitchProfile(p);
    }

    void SwitchProfile(Profile p)
    {
        _profile = p;
        _theme = Themes.Get(p.ThemeId);
        foreach (var s in p.Ordered)
        {
            if (_only is not null && !_only.Contains(s.Id)) continue;
            if (!_windows.TryGetValue(s.Id, out var w))
            {
                w = new WidgetWindow(s.Id, s, _theme) { Snap = SnapToOthers };
                w.UserChanged += OnUserChanged;
                w.SetGate(_gate);
                w.SetSession(_session);
                w.SetEditMode(_edit);
                _windows[s.Id] = w;
            }
            else w.Apply(s, _theme);
        }
        foreach (var id in _windows.Keys.Where(id => p.Get(id) is null).ToArray())
        {
            _windows[id].Dispose();
            _windows.Remove(id);
        }
        RestackByOrder();
        ApplyRadarOptions();
        if (_persist)
        {
            _store.SetActiveTheme(p.ThemeId);
            _store.SetActiveProfile(p.ThemeId, p.Name);
        }
        _saveAt = null;
    }

    void UpdateWidget(WidgetSettings next, bool orderChanged)
    {
        if (orderChanged)
        {
            var cur = _profile.Get(next.Id)!;
            _profile = _profile.WithWidget(next with { Order = cur.Order }).MoveTo(next.Id, next.Order);
        }
        else _profile = _profile.WithWidget(next);
        foreach (var o in _profile.Widgets)
            if ((o.Id == next.Id || orderChanged) && _windows.TryGetValue(o.Id, out var w)) w.Apply(o, _theme);
        if (orderChanged) RestackByOrder();
        if (next.Id == "radar") ApplyRadarOptions();
        ScheduleSave();
    }

    void SetEditMode(bool edit)
    {
        _edit = edit;
        foreach (var w in _windows.Values) w.SetEditMode(edit);
    }

    HostState BuildState()
    {
        var m = _provider.Current;
        return new HostState
        {
            Fake = _fake,
            GameConnected = m.Connected,
            GameStatus = m.Status.ToString(),
            Theme = _theme.Id,
            ActiveProfile = _profile.Name,
            EditMode = _edit,
            Widgets = _profile.Ordered.ToList(),
            Themes = ThemeCatalog.All.Select(t => t with { Available = t.Available && Themes.All.Any(x => x.Id == t.Id) }).ToList(),
            PlayerNames = _provider.Names?.State() ?? new(),
            Session = _session,
            HiddenBySession = _windows.Values.Where(w => !w.SessionAllowed).Select(w => w.Id).ToList(),
        };
    }

    // ---- Edição pelo usuário ----

    void OnUserChanged(WidgetWindow w)
    {
        _profile = _profile.WithWidget(w.Settings.Normalized(_theme.Id));
        ScheduleSave();
        _ipc?.Broadcast(new IpcMessage { Event = IpcEvents.StateChanged, State = BuildState() });
    }

    void ScheduleSave() { if (_persist) _saveAt = DateTime.UtcNow + SaveDelay; }

    /// <summary>Encaixa à grade, às bordas da tela e às bordas das outras janelas visíveis.</summary>
    (int X, int Y) SnapToOthers(WidgetWindow self, int x, int y, int w, int h)
    {
        int nx = (int)Math.Round(x / (double)SnapGrid) * SnapGrid, ny = (int)Math.Round(y / (double)SnapGrid) * SnapGrid;
        var xs = new List<int>(); var ys = new List<int>();
        int vx = Win32.GetSystemMetrics(76), vy = Win32.GetSystemMetrics(77), vw = Win32.GetSystemMetrics(78), vh = Win32.GetSystemMetrics(79);
        xs.Add(vx); xs.Add(vx + vw - w); ys.Add(vy); ys.Add(vy + vh - h);
        foreach (var o in _windows.Values)
        {
            if (o == self || !o.Visible) continue;
            var b = o.Bounds;
            xs.Add(b.X); xs.Add(b.X + b.W); xs.Add(b.X - w); xs.Add(b.X + b.W - w);
            ys.Add(b.Y); ys.Add(b.Y + b.H); ys.Add(b.Y - h); ys.Add(b.Y + b.H - h);
        }
        int bestX = x, bestY = y; int dx = SnapEdge + 1, dy = SnapEdge + 1;
        foreach (var c in xs) if (Math.Abs(c - x) < dx) { dx = Math.Abs(c - x); bestX = c; }
        foreach (var c in ys) if (Math.Abs(c - y) < dy) { dy = Math.Abs(c - y); bestY = c; }
        return (dx <= SnapEdge ? bestX : nx, dy <= SnapEdge ? bestY : ny);
    }

    // ---- Laço principal ----

    /// <summary>Taxa dos widgets de baixo custo (todos menos os marcados <see cref="IWidget.HighFrequency"/>).</summary>
    const double LowPeriod = 1.0 / 60, LowSlack = 0.002;

    /// <summary>
    /// Roda até Ctrl+Alt+Q, fechamento ou <paramref name="seconds"/> (0 = sem limite). Thread principal.
    /// Dois ritmos num só laço (mesma thread: nenhuma janela é tocada fora da sua thread):
    ///  - widgets de alta frequência seguem os prazos do monitor, sem esperar a composicao de todas as janelas;
    ///    Present(0, DoNotWait) evita que uma fila cheia bloqueie os demais instrumentos;
    ///  - os demais seguem a 60 Hz por relógio (acumulador, sem deriva), como antes;
    ///  - sem nenhum widget de alta frequência visível o laço volta a dormir só até o próximo passo de 60 Hz.
    /// </summary>
    public void Run(double seconds)
    {
        Win32.BeginHighResTimer();
        var prio = Thread.CurrentThread.Priority;
        Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
        try { RunLoop(seconds); }
        finally { Thread.CurrentThread.Priority = prio; Win32.EndHighResTimer(); }
        SaveNow();
    }

    void RunLoop(double seconds)
    {
        var run = System.Diagnostics.Stopwatch.StartNew();
        double nextLow = 0;
        double refreshPeriod = 1.0 / Win32.PrimaryRefreshHz();
        double nextHigh = 0;
        while (OverlayWindow.Pump(() => ToggleEditFromHotkey()))
        {
            double now = run.Elapsed.TotalSeconds;
            if (seconds > 0 && now >= seconds) break;
            while (_queue.TryDequeue(out var a)) a();
            if (_saveAt is { } at && DateTime.UtcNow >= at) SaveNow();

            var model = _provider.Current;
            // Regra unica: widgets so aparecem com o jogador no carro (ou editando o layout). Oculto = janelas escondidas, sem Render e sem vblank.
            bool gate = _edit || model.PlayerDriving;
            if (gate != _gate) { _gate = gate; foreach (var w in _windows.Values) w.SetGate(gate); }
            // Filtro por tipo de sessao (Treino/Classificacao/Corrida) escolhido no Control Center; sem sessao nao filtra.
            var session = model.SessionGroup;
            if (session != _session)
            {
                _session = session;
                foreach (var w in _windows.Values) w.SetSession(session);
                _ipc?.Broadcast(new IpcMessage { Event = IpcEvents.StateChanged, State = BuildState() });
            }
            bool lowDue = now + LowSlack >= nextLow;
            if (lowDue) { nextLow += LowPeriod; if (nextLow < now - LowPeriod) nextLow = now + LowPeriod; }
            bool high = false;
            foreach (var w in _windows.Values)
            {
                if (w.HighFrequency && !w.IsIdle(model)) { w.Render(model); high = true; }
                else if (lowDue) w.Render(model);
            }

            if (high)
            {
                // DwmFlush espera toda a composicao; em desktop remoto pode limitar todas as janelas a 60 Hz.
                // O swapchain faz a entrega ao compositor. Cadencia por prazo absoluto, sem somar espera ao desenho.
                nextHigh += refreshPeriod;
                double completed = run.Elapsed.TotalSeconds;
                if (nextHigh <= completed) nextHigh = completed;
                else SleepUntil(run, nextHigh);
            }
            else { nextHigh = run.Elapsed.TotalSeconds; SleepUntil(run, nextLow); }
        }
    }

    static void SleepUntil(System.Diagnostics.Stopwatch run, double target)
    {
        double wait = target - run.Elapsed.TotalSeconds;
        if (wait > 0.0015) Thread.Sleep(Math.Max(1, (int)(wait * 1000) - 1)); // timer de 1 ms: acorda ate ~1 ms antes
        else Thread.Sleep(1);
    }

    void ToggleEditFromHotkey()
    {
        SetEditMode(!_edit);
        _ipc?.Broadcast(new IpcMessage { Event = IpcEvents.StateChanged, State = BuildState() });
    }

    void SaveNow()
    {
        if (!_persist || _saveAt is null) return;
        _saveAt = null;
        try { _store.Save(_profile); }
        catch (Exception ex) { Console.Error.WriteLine($"[Host] salvar perfil: {ex.Message}"); }
    }

    public void Dispose()
    {
        _ipc?.Dispose();
        foreach (var w in _windows.Values) w.Dispose();
        _windows.Clear();
    }
}
