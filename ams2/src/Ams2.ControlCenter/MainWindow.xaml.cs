using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Ams2.Shared.Ipc;
using Ams2.Shared.PlayerNames;
using Ams2.Shared.Profiles;

namespace Ams2.ControlCenter;

/// <summary>
/// Control Center: edita o perfil em disco (ProfileStore compartilhado com o host) e, quando o perfil editado é o ativo no host,
/// envia cada alteração ao vivo por IPC (comandos agrupados a cada ~50 ms). O que o host muda (arrastar no modo de edição)
/// volta por evento e é refletido aqui.
/// </summary>
public partial class MainWindow : Window
{
    static readonly TimeSpan LocalEditQuietTime = TimeSpan.FromMilliseconds(1200);

    readonly ProfileStore _store;
    readonly IpcClient _client;
    readonly ObservableCollection<WidgetVm> _widgets = [];
    readonly ObservableCollection<ProfileItem> _profiles = [];
    readonly PlayerNameStore _names;
    readonly ObservableCollection<PlayerNameVm> _nameRows = [];
    readonly Dictionary<string, WidgetPatch> _pending = [];
    readonly DispatcherTimer _flushTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    readonly DispatcherTimer _pollTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    int _previewSeq;
    string? _previewDir;

    string _themeId = ThemeCatalog.Default;
    string _profileName = "";
    Profile _profile = new();
    HostState? _host;
    bool _loading, _adopted, _saveDirty;
    DateTime _lastLocalEdit = DateTime.MinValue;
    Point _dragStart;
    WidgetVm? _dragItem;
    WidgetVm? _dropMarked;

    public MainWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);

        var args = Environment.GetCommandLineArgs();
        string? Val(string n) { int i = Array.IndexOf(args, n); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        _store = new ProfileStore(Val("--profiles-dir"));
        _names = new PlayerNameStore(Val("--profiles-dir")); // so usado com o host fechado; com ele aberto os nomes vao por IPC
        _client = new IpcClient(Val("--pipe") ?? IpcProtocol.DefaultPipeName);

        FontCombo.ItemsSource = new[] { WidgetVm.FontDefault }.Concat(Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)).ToList();
        WidgetsList.ItemsSource = _widgets;
        ProfilesList.ItemsSource = _profiles;
        NamesList.ItemsSource = _nameRows;
        ApplyNames(_names.State());

        _flushTimer.Tick += async (_, _) => { _flushTimer.Stop(); await FlushPendingAsync(); };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };
        _pollTimer.Tick += async (_, _) => await PollAsync();
        Closing += (_, _) => { SaveNow(); _client.Dispose(); try { if (_previewDir is not null) Directory.Delete(_previewDir, true); } catch { } };

        _previewTimer.Tick += async (_, _) => { _previewTimer.Stop(); await RefreshPreviewAsync(); };
        _client.ConnectionChanged += c => Dispatcher.BeginInvoke(async () => await OnConnectionChanged(c));
        _client.EventReceived += e => Dispatcher.BeginInvoke(() => { if (e.State is { } s) ApplyHostState(s); });

        InitThemes();
        _themeId = ThemeCatalog.Find(_store.GetActiveTheme()) is { Available: true } t ? t.Id : ThemeCatalog.Default;
        SelectThemeInCombo(_themeId);
        LoadTheme(_themeId, null);

        _client.Start();
        _pollTimer.Start();
        RefreshIndicators();

        // As janelas do overlay sao topmost (e se reempilham ao trocar perfil/modo de edicao): o CC e topmost tambem e se reafirma
        // acima delas enquanto estiver aberto. SWP_NOACTIVATE = nao rouba o foco do jogo. Em modo de edicao nao reafirma, para o
        // overlay (que sobe ao topo nesse modo) ficar manipulavel; clicar no CC o traz de volta a frente.
        _keepAboveTimer.Tick += (_, _) => KeepAboveOverlay();
        _keepAboveTimer.Start();
    }

    readonly DispatcherTimer _keepAboveTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);

    void KeepAboveOverlay()
    {
        if (!IsVisible || WindowState == WindowState.Minimized || _host?.EditMode == true) return;
        var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (h != 0) SetWindowPos(h, -1 /*HWND_TOPMOST*/, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 /*NOSIZE|NOMOVE|NOACTIVATE*/);
    }

    // ---------------------------------------------------------------- temas e perfis

    void InitThemes()
    {
        _loading = true;
        ThemeCombo.ItemsSource = ThemeCatalog.All.Select(t => new ThemeItem(t, t.Available)).ToList();
        _loading = false;
    }

    void SelectThemeInCombo(string id)
    {
        _loading = true;
        ThemeCombo.SelectedItem = ((IEnumerable<ThemeItem>)ThemeCombo.ItemsSource).FirstOrDefault(t => t.Def.Id == id);
        _loading = false;
    }

    (int W, int H) ScreenPixels()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return ((int)Math.Round(SystemParameters.PrimaryScreenWidth * dpi.DpiScaleX), (int)Math.Round(SystemParameters.PrimaryScreenHeight * dpi.DpiScaleY));
    }

    void LoadTheme(string themeId, string? select)
    {
        _themeId = themeId;
        var (sw, sh) = ScreenPixels();
        var names = _store.EnsureDefault(themeId, sw, sh);
        string name = select ?? _store.GetActiveProfile(themeId, sw, sh);
        if (!names.Contains(name)) name = names[0];
        RefreshProfileList(names, name);
        LoadProfile(name);
    }

    void RefreshProfileList(IReadOnlyList<string> names, string selected)
    {
        _loading = true;
        _profiles.Clear();
        foreach (var n in names) _profiles.Add(new ProfileItem(n));
        ProfilesList.SelectedItem = _profiles.FirstOrDefault(p => p.Name == selected);
        _loading = false;
        UpdateActiveMarkers();
    }

    void LoadProfile(string name)
    {
        var (sw, sh) = ScreenPixels();
        _profileName = name;
        _profile = _store.Load(_themeId, name, sw, sh) ?? ProfileFactory.CreateDefault(name, _themeId, sw, sh).Normalized(sw, sh);
        RebuildWidgets();
        UpdateActiveMarkers();
        UpdateBanner();
    }

    void RebuildWidgets()
    {
        _loading = true;
        string? keep = (WidgetsList.SelectedItem as WidgetVm)?.Id;
        foreach (var w in _widgets) w.Edited -= OnWidgetEdited;
        _widgets.Clear();
        foreach (var s in _profile.Ordered)
        {
            var def = WidgetCatalog.Find(s.Id);
            if (def is null || !def.InTheme(_themeId)) continue;   // so os widgets do tema ativo
            var vm = new WidgetVm(def, _themeId);
            vm.Load(s);
            vm.Edited += OnWidgetEdited;
            _widgets.Add(vm);
            EnsureFontListed(s.Font);
        }
        WidgetsList.SelectedItem = _widgets.FirstOrDefault(w => w.Id == keep) ?? _widgets.FirstOrDefault();
        _loading = false;
        ShowDetail(WidgetsList.SelectedItem as WidgetVm);
    }

    void EnsureFontListed(string? font)
    {
        if (font is null) return;
        var list = (List<string>)FontCombo.ItemsSource;
        if (!list.Contains(font)) list.Add(font);
    }

    bool IsLiveTarget => _client.Connected && _host is { } h && h.Theme == _themeId && h.ActiveProfile == _profileName;

    void UpdateActiveMarkers()
    {
        string? active = _host is { } h && h.Theme == _themeId ? h.ActiveProfile : _host is null ? _store.GetActiveProfile(_themeId) : null;
        foreach (var p in _profiles) p.IsActive = p.Name == active;
        ActivateButton.IsEnabled = _profileName != active;
    }

    void UpdateBanner()
    {
        string? active = _host is { } h && h.Theme == _themeId ? h.ActiveProfile : _host is null ? _store.GetActiveProfile(_themeId) : null;
        bool differs = _profileName != active;
        InactiveBanner.Visibility = differs ? Visibility.Visible : Visibility.Collapsed;
        InactiveText.Text = $"Você está editando \"{_profileName}\", que não é o perfil ativo. As mudanças só aparecem no jogo depois de ativar.";
        EditToggle.IsEnabled = _client.Connected && !differs;
    }

    async void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeCombo.SelectedItem is not ThemeItem item) return;
        if (!item.IsAvailable) { SelectThemeInCombo(_themeId); return; }
        if (item.Def.Id == _themeId) return;
        SaveNow();
        _store.SetActiveTheme(item.Def.Id);
        LoadTheme(item.Def.Id, null);
        _store.SetActiveProfile(_themeId, _profileName);
        if (_client.Connected) await TrySend(IpcCommands.SetTheme, m => m with { Theme = item.Def.Id });
    }

    void ProfilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ProfilesList.SelectedItem is not ProfileItem p || p.Name == _profileName) return;
        SaveNow();
        FlushPendingDiscard();
        LoadProfile(p.Name);
    }

    string NewNameSuggestion(string baseName)
    {
        var names = _store.List(_themeId);
        if (!names.Contains(baseName)) return baseName;
        for (int i = 2; ; i++) if (!names.Contains($"{baseName} {i}")) return $"{baseName} {i}";
    }

    bool NameTaken(string name) => _store.List(_themeId).Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

    void NewProfile_Click(object sender, RoutedEventArgs e)
    {
        var name = InputDialog.Ask(this, "Novo perfil", "Nome do perfil", NewNameSuggestion("Novo perfil"), "Criar");
        if (name is null) return;
        if (NameTaken(name)) { MessageBox.Show(this, $"Já existe um perfil \"{name}\".", "Novo perfil"); return; }
        SaveNow();
        var (sw, sh) = ScreenPixels();
        _store.Save(ProfileFactory.CreateDefault(name, _themeId, sw, sh));
        LoadTheme(_themeId, name);
    }

    void DuplicateProfile_Click(object sender, RoutedEventArgs e)
    {
        var name = InputDialog.Ask(this, "Duplicar perfil", "Nome da cópia", NewNameSuggestion(_profileName + " (cópia)"), "Duplicar");
        if (name is null) return;
        if (NameTaken(name)) { MessageBox.Show(this, $"Já existe um perfil \"{name}\".", "Duplicar perfil"); return; }
        SaveNow();
        _store.Duplicate(_themeId, _profileName, name);
        LoadTheme(_themeId, name);
    }

    async void RenameProfile_Click(object sender, RoutedEventArgs e)
    {
        var name = InputDialog.Ask(this, "Renomear perfil", "Novo nome", _profileName, "Renomear");
        if (name is null || name == _profileName) return;
        if (NameTaken(name)) { MessageBox.Show(this, $"Já existe um perfil \"{name}\".", "Renomear perfil"); return; }
        SaveNow();
        bool wasActiveOnHost = IsLiveTarget;
        var renamed = _store.Rename(_themeId, _profileName, name);
        LoadTheme(_themeId, name);
        if (wasActiveOnHost) await TrySend(IpcCommands.ApplyProfile, m => m with { Data = renamed });
    }

    async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_profiles.Count <= 1) { MessageBox.Show(this, "Mantenha ao menos um perfil por tema.", "Excluir perfil"); return; }
        if (MessageBox.Show(this, $"Excluir o perfil \"{_profileName}\"?", "Excluir perfil", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _saveDirty = false; _saveTimer.Stop();
        FlushPendingDiscard();
        bool wasActive = _host is { } h ? h.Theme == _themeId && h.ActiveProfile == _profileName : _store.GetActiveProfile(_themeId) == _profileName;
        _store.Delete(_themeId, _profileName);
        var next = _store.List(_themeId)[0];
        LoadTheme(_themeId, next);
        if (wasActive) await ActivateCurrentAsync();
    }

    async void Activate_Click(object sender, RoutedEventArgs e) => await ActivateCurrentAsync();

    async Task ActivateCurrentAsync()
    {
        SaveNow();
        _store.SetActiveTheme(_themeId);
        _store.SetActiveProfile(_themeId, _profileName);
        if (_client.Connected)
        {
            var profile = _profile;
            await TrySend(IpcCommands.ApplyProfile, m => m with { Data = profile });
        }
        else UpdateActiveMarkers();
        UpdateActiveMarkers();
        UpdateBanner();
    }

    // ---------------------------------------------------------------- widgets

    void WidgetsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading) ShowDetail(WidgetsList.SelectedItem as WidgetVm);
    }

    void ShowDetail(WidgetVm? vm)
    {
        bool has = vm is not null;
        NoSelection.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        DetailScroll.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        _loading = true; // ligar o DataContext nao pode contar como edicao
        Detail.DataContext = vm;
        _loading = false;
        SchedulePreview();
        if (vm is null) return;
        RowsPanel.Visibility = vm.SupportsRows ? Visibility.Visible : Visibility.Collapsed;
        SelectionPanel.Visibility = vm.HasSelection ? Visibility.Visible : Visibility.Collapsed;
        RadarPanel.Visibility = vm.HasRadarOptions ? Visibility.Visible : Visibility.Collapsed;
        WidthsPanel.Visibility = vm.HasWidths ? Visibility.Visible : Visibility.Collapsed;
        FormatPanel.Visibility = vm.HasFormat ? Visibility.Visible : Visibility.Collapsed;
        BoardModeBar.Visibility = vm.IsBoard ? Visibility.Visible : Visibility.Collapsed;
        ColumnsPanel.Visibility = vm.HasColumns ? Visibility.Visible : Visibility.Collapsed;
        ThemeOptionsPanel.Visibility = vm.HasThemeOptions ? Visibility.Visible : Visibility.Collapsed;
    }

    void OnWidgetEdited(WidgetVm vm, string prop)
    {
        if (_loading) return;
        if (ReferenceEquals(vm, Detail.DataContext)) SchedulePreview();
        _lastLocalEdit = DateTime.UtcNow;
        int order = _widgets.IndexOf(vm);
        _profile = _profile.WithWidget(vm.ToSettings(order));
        _saveDirty = true;
        _saveTimer.Stop(); _saveTimer.Start();
        if (IsLiveTarget && vm.PatchFor(prop) is { } patch)
        {
            _pending[vm.Id] = _pending.TryGetValue(vm.Id, out var old) ? Merge(old, patch) : patch;
            if (!_flushTimer.IsEnabled) _flushTimer.Start();
        }
    }

    static WidgetPatch Merge(WidgetPatch a, WidgetPatch b) => WidgetPatch.Merge(a, b);

    async Task FlushPendingAsync()
    {
        if (_pending.Count == 0) return;
        var batch = _pending.ToList();
        _pending.Clear();
        foreach (var (id, patch) in batch)
            if (!await TrySend(IpcCommands.SetWidget, m => m with { Widget = id, Patch = patch })) break;
    }

    void FlushPendingDiscard() { _pending.Clear(); _flushTimer.Stop(); }

    void SaveNow()
    {
        if (!_saveDirty) return;
        _saveDirty = false;
        try { _store.Save(_profile); }
        catch (Exception ex) { MessageBox.Show(this, "Não foi possível salvar o perfil: " + ex.Message, "Perfis"); }
    }

    void TopMinus_Click(object sender, RoutedEventArgs e) { if (Detail.DataContext is WidgetVm vm) vm.TopCount--; }
    void TopPlus_Click(object sender, RoutedEventArgs e) { if (Detail.DataContext is WidgetVm vm) vm.TopCount++; }
    void NearMinus_Click(object sender, RoutedEventArgs e) { if (Detail.DataContext is WidgetVm vm) vm.NearCount--; }
    void NearPlus_Click(object sender, RoutedEventArgs e) { if (Detail.DataContext is WidgetVm vm) vm.NearCount++; }
    void RadarRangeMinus_Click(object sender, RoutedEventArgs e) { if (Detail.DataContext is WidgetVm vm) vm.RadarRange -= 5; }
    void RadarRangePlus_Click(object sender, RoutedEventArgs e) { if (Detail.DataContext is WidgetVm vm) vm.RadarRange += 5; }
    void RadarSensMinus_Click(object sender, RoutedEventArgs e) { if (Detail.DataContext is WidgetVm vm) vm.RadarSensitivity--; }
    void RadarSensPlus_Click(object sender, RoutedEventArgs e) { if (Detail.DataContext is WidgetVm vm) vm.RadarSensitivity++; }

    // Previa do board: um modo por vez (segundos do --sim da corrida simulada de 20 carros; tabela em ams2/reference/board-spec.md).
    static readonly double[] BoardSimSeconds = [5, 16, 40, 20];   // torre (pagina 1 cheia), setor S2, comparativo, legenda
    int _boardMode;

    void BoardMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string tag } || !int.TryParse(tag, out int mode)) return;
        _boardMode = mode;
        foreach (var b in BoardModeBar.Children.OfType<ToggleButton>()) b.IsChecked = b.Tag is string t && t == tag;
        SchedulePreview();
    }

    void RowsMinus_Click(object sender, RoutedEventArgs e) { if (Detail.DataContext is WidgetVm vm) vm.Rows--; }
    void RowsPlus_Click(object sender, RoutedEventArgs e) { if (Detail.DataContext is WidgetVm vm) vm.Rows++; }

    void ResetWidget_Click(object sender, RoutedEventArgs e)
    {
        if (Detail.DataContext is not WidgetVm vm) return;
        var (sw, sh) = ScreenPixels();
        var d = ProfileFactory.CreateDefault("", _themeId, sw, sh).Get(vm.Id)!;
        vm.Visible = d.Visible; vm.Scale = d.Scale; vm.OpacityPct = 100; vm.FontChoice = WidgetVm.FontDefault;
        if (vm.SupportsRows) vm.Rows = vm.Def.DefaultRows ?? vm.Rows;
        if (vm.HasSelection) { vm.TopCount = WidgetCatalog.DefaultTopCount; vm.NearCount = WidgetCatalog.DefaultNearCount; }
        if (vm.HasRadarOptions) { vm.RadarRange = WidgetCatalog.DefaultRadarRange; vm.RadarSensitivity = WidgetCatalog.DefaultRadarSensitivity; }
        foreach (var c in vm.Columns) c.IsVisible = d.ColumnVisible(c.Def.Id);
        vm.ResetCustomization();
        vm.ResetSessions();
        vm.X = d.X; vm.Y = d.Y;
    }

    // ---------------------------------------------------------------- previa

    void SchedulePreview() { _previewTimer.Stop(); _previewTimer.Start(); }

    /// <summary>Procura o OverlayHost: ao lado deste exe (publicado) ou na saida de build irma (desenvolvimento).</summary>
    static string? FindHostExe()
    {
        string dir = AppContext.BaseDirectory;
        string local = Path.Combine(dir, "Ams2.OverlayHost.exe");
        if (File.Exists(local)) return local;
        for (var d = new DirectoryInfo(dir); d is not null; d = d.Parent)
        {
            var cand = Path.Combine(d.FullName, "Ams2.OverlayHost", "bin");
            if (!Directory.Exists(cand)) continue;
            return Directory.EnumerateFiles(cand, "Ams2.OverlayHost.exe", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        }
        return null;
    }

    /// <summary>
    /// Previa do widget selecionado: roda o proprio OverlayHost em modo --png (dados simulados, nunca abre o jogo nem uma janela)
    /// com o tema e as opcoes atuais do widget. Respostas antigas sao descartadas pela sequencia.
    /// </summary>
    async Task RefreshPreviewAsync()
    {
        if (Detail.DataContext is not WidgetVm vm) { PreviewImage.Source = null; PreviewStatus.Text = ""; return; }
        int seq = ++_previewSeq;
        var exe = FindHostExe();
        if (exe is null) { PreviewImage.Source = null; PreviewStatus.Text = "OverlayHost não encontrado."; return; }
        _previewDir ??= Path.Combine(Path.GetTempPath(), "ams2-cc-preview-" + Environment.ProcessId);
        Directory.CreateDirectory(_previewDir);
        string png = Path.Combine(_previewDir, $"{seq}.png");
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        // Todas as opcoes do widget (linhas, colunas, larguras, formato, texto, radar...) vao no mesmo JSON do perfil (--settings); a previa usa escala 1.
        string settingsJson = Path.ChangeExtension(png, ".json");
        var previewSettings = vm.ToSettings(0) with { Scale = 1f };
        // Widgets de evento (legenda, paradas, cronometro, vencedor) so aparecem com um evento recente: a previa os fixa (coluna "always") e simula o evento.
        double sim = vm.IsBoard ? BoardSimSeconds[_boardMode] : vm.IsRadar ? 3 : vm.Id == "racecontrol" ? 30 : 20;
        bool eventWidget = vm.Id is "drivercaption" or "pitstops" or "pittimer" or "winner";
        if (eventWidget)
        {
            previewSettings = previewSettings with { Columns = vm.Columns.Where(c => c.IsVisible).Select(c => c.Def.Id).Append("always").Distinct().ToArray() };
            sim = vm.Id switch { "winner" => 40, "pitstops" => 30, "pittimer" => 18, _ => 20 };
        }
        File.WriteAllText(settingsJson, JsonSerializer.Serialize(previewSettings, ProfileStore.Json));
        foreach (var a in new[] { "--png", png, "--widget", vm.Id, "--theme", _themeId, "--bg", "none", "--sim", sim.ToString(System.Globalization.CultureInfo.InvariantCulture), "--settings", settingsJson })
            psi.ArgumentList.Add(a);
        // Radar: carros orbitando o jogador (instante 3 s = um de cada lado); o alcance e a sensibilidade do widget valem na previa.
        if (vm.IsRadar) psi.Environment["AMS2_FAKE_RADAR"] = "1";
        if (vm.Id == "winner") psi.Environment["AMS2_FAKE_FINISH"] = "1";
        if (vm.Id is "pitstops" or "pittimer") psi.Environment["AMS2_FAKE_PITS"] = "1";
        // Race Control 2018: bandeira amarela + parada lenta do jogador (11,1 s parado, termina em t=28,1 s; a previa e tirada em t=30).
        if (vm.Id == "racecontrol") { psi.Environment["AMS2_FAKE_FLAG"] = "6"; psi.Environment["AMS2_FAKE_PITS"] = "1"; psi.Environment["AMS2_FAKE_PITSTOP"] = "11.1"; }
        // Standings e board: corrida simulada de 20 carros (o campo padrao de 8 nao mostra o topo + janela nem a torre em paginas).
        if (vm.HasSelection || vm.IsBoard) psi.Environment["AMS2_FAKE_BOARD"] = "1";
        try
        {
            using var p = Process.Start(psi)!;
            _ = p.StandardOutput.ReadToEndAsync();
            _ = p.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await p.WaitForExitAsync(cts.Token);
            TryDelete(settingsJson);
            if (seq != _previewSeq) { TryDelete(png); return; }
            if (p.ExitCode != 0 || !File.Exists(png)) { PreviewImage.Source = null; PreviewStatus.Text = "Prévia indisponível."; return; }
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;   // carrega tudo e solta o arquivo
            bmp.UriSource = new Uri(png);
            bmp.EndInit();
            bmp.Freeze();
            PreviewImage.Source = bmp;
            PreviewStatus.Text = "";
            PreviewCaption.Text = vm.IsBoard ? $"PRÉVIA · board · {new[] { "torre da linha", "gap de setor", "comparativo de voltas", "legenda" }[_boardMode]} · dados simulados" : $"PRÉVIA · {vm.Name} · dados simulados";
            TryDelete(png);
        }
        catch (Exception) { if (seq == _previewSeq) { PreviewImage.Source = null; PreviewStatus.Text = "Prévia indisponível."; } }
    }

    static void TryDelete(string f) { try { File.Delete(f); } catch { } }

    // ---------------------------------------------------------------- arrastar para reordenar

    void WidgetsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragItem = null;
        if (e.OriginalSource is FrameworkElement { Tag: "grip" } g && g.DataContext is WidgetVm vm)
        {
            _dragItem = vm;
            _dragStart = e.GetPosition(null);
        }
    }

    void WidgetsList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragItem is null || e.LeftButton != MouseButtonState.Pressed) return;
        var d = e.GetPosition(null) - _dragStart;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var item = _dragItem; _dragItem = null;
        WidgetsList.SelectedItem = item;
        DragDrop.DoDragDrop(WidgetsList, item, DragDropEffects.Move);
        MarkDrop(null);
    }

    WidgetVm? VmUnder(DragEventArgs e)
    {
        var el = WidgetsList.InputHitTest(e.GetPosition(WidgetsList)) as DependencyObject;
        while (el is not null and not ListBoxItem) el = VisualTreeHelper.GetParent(el);
        return (el as ListBoxItem)?.DataContext as WidgetVm;
    }

    void MarkDrop(WidgetVm? vm)
    {
        if (_dropMarked == vm) return;
        if (_dropMarked is not null) _dropMarked.DropTarget = false;
        _dropMarked = vm;
        if (vm is not null) vm.DropTarget = true;
    }

    void WidgetsList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(WidgetVm)) ? DragDropEffects.Move : DragDropEffects.None;
        MarkDrop(VmUnder(e));
        e.Handled = true;
    }

    void WidgetsList_DragLeave(object sender, DragEventArgs e) => MarkDrop(null);

    void WidgetsList_Drop(object sender, DragEventArgs e)
    {
        MarkDrop(null);
        if (e.Data.GetData(typeof(WidgetVm)) is not WidgetVm moving) return;
        var target = VmUnder(e);
        int from = _widgets.IndexOf(moving);
        int to = target is null ? _widgets.Count - 1 : _widgets.IndexOf(target);
        if (from < 0 || to < 0 || from == to) return;
        _loading = true;
        _widgets.Move(from, to);
        WidgetsList.SelectedItem = moving;
        _loading = false;
        _lastLocalEdit = DateTime.UtcNow;
        _profile = _profile.MoveTo(moving.Id, to);
        _saveDirty = true; _saveTimer.Stop(); _saveTimer.Start();
        if (IsLiveTarget)
        {
            var patch = new WidgetPatch { Order = to };
            _pending[moving.Id] = _pending.TryGetValue(moving.Id, out var old) ? Merge(old, patch) : patch;
            _flushTimer.Stop(); _flushTimer.Start();
        }
        e.Handled = true;
    }

    // ---------------------------------------------------------------- nome do piloto (um nome de exibicao por modelo de carro)

    void NamesToggle_Click(object sender, RoutedEventArgs e)
        => NamesPanel.Visibility = NamesToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Host fechado: a lista vem do arquivo (e edicoes manuais dele aparecem).</summary>
    void RefreshNamesOffline()
    {
        _names.Refresh();
        ApplyNames(_names.State());
    }

    void ApplyNames(PlayerNamesState st)
    {
        bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        foreach (var e in st.Entries)
        {
            var vm = _nameRows.FirstOrDefault(r => Same(r.Model, e.Model));
            if (vm is null) { vm = new PlayerNameVm(e.Model); _nameRows.Add(vm); }
            vm.Load(e, st.CurrentModel.Length > 0 && Same(e.Model, st.CurrentModel));
        }
        for (int i = _nameRows.Count - 1; i >= 0; i--)
            if (!_nameRows[i].Editing && !st.Entries.Any(e => Same(e.Model, _nameRows[i].Model))) _nameRows.RemoveAt(i);
        NamesEmpty.Visibility = _nameRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var cur = st.Entries.FirstOrDefault(e => st.CurrentModel.Length > 0 && Same(e.Model, st.CurrentModel));
        NamesCurrent.Text = cur is null
            ? "Carro atual: nenhum detectado (entre numa sessão no AMS2)"
            : $"Carro atual: {cur.Model}   ·   no jogo: {(cur.OriginalName.Length > 0 ? cur.OriginalName : "—")}   ·   nos widgets: {(cur.Name.Length > 0 ? cur.Name : cur.OriginalName.Length > 0 ? cur.OriginalName : "—")}";
    }

    async Task SetNameAsync(string model, string name)
    {
        if (_client.Connected && await TrySend(IpcCommands.SetPlayerName, m => m with { Model = model, Name = name })) return;
        _names.Refresh();
        _names.Set(model, name);
        ApplyNames(_names.State());
    }

    void NameBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: PlayerNameVm vm }) vm.Editing = true;
    }

    async void NameBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox { DataContext: PlayerNameVm vm } tb) return;
        vm.Editing = false;
        string text = tb.Text.Trim();
        if (text == vm.Name) { tb.Text = vm.Name; return; }
        vm.Name = text;                       // otimista; o estado do host confirma em seguida
        await SetNameAsync(vm.Model, text);
    }

    void NameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: PlayerNameVm vm } tb) return;
        if (e.Key == Key.Enter) { Keyboard.ClearFocus(); e.Handled = true; }          // perder o foco aplica
        else if (e.Key == Key.Escape) { tb.Text = vm.Name; Keyboard.ClearFocus(); e.Handled = true; }
    }

    async void UseSuggested_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PlayerNameVm { HasSuggestion: true } vm }) return;
        vm.Name = vm.Suggested;
        await SetNameAsync(vm.Model, vm.Suggested);
    }

    async void ApplySuggested_Click(object sender, RoutedEventArgs e)
    {
        if (_client.Connected && await TrySend(IpcCommands.ApplySuggestedNames)) return;
        _names.Refresh();
        _names.ApplySuggestedToUnnamed();
        ApplyNames(_names.State());
    }

    // ---------------------------------------------------------------- host

    async Task<bool> TrySend(string cmd, Func<IpcMessage, IpcMessage>? fill = null)
    {
        try
        {
            var st = await _client.SendAsync(cmd, fill);
            if (st is not null) ApplyHostState(st, force: cmd != IpcCommands.SetWidget);
            return true;
        }
        catch (InvalidOperationException ex) { MessageBox.Show(this, ex.Message, "Overlay"); return true; }
        catch (Exception) { return false; } // sem conexao/timeout: o indicador mostra o estado
    }

    async Task OnConnectionChanged(bool connected)
    {
        if (!connected) { _host = null; _adopted = false; RefreshNamesOffline(); }
        RefreshIndicators();
        UpdateActiveMarkers();
        UpdateBanner();
        if (connected) await PollAsync();
    }

    async Task PollAsync()
    {
        if (!_client.Connected) { RefreshNamesOffline(); return; }
        try { var st = await _client.SendAsync(IpcCommands.GetState); if (st is not null) ApplyHostState(st); }
        catch (Exception) { }
    }

    static string Sig(WidgetSettings s) => JsonSerializer.Serialize(s, ProfileStore.Json);

    /// <param name="force">Aceita o estado do host mesmo logo depois de uma edição local (usado após comandos de perfil/tema).</param>
    void ApplyHostState(HostState s, bool force = false)
    {
        _host = s;
        ApplyNames(s.PlayerNames);
        // Ao conectar pela primeira vez, assume o perfil/tema que o host está usando.
        if (!_adopted)
        {
            _adopted = true;
            if ((s.Theme != _themeId || s.ActiveProfile != _profileName) && ThemeCatalog.Find(s.Theme) is { Available: true } && _store.Exists(s.Theme, s.ActiveProfile))
            {
                SaveNow();
                if (s.Theme != _themeId) { SelectThemeInCombo(s.Theme); }
                LoadTheme(s.Theme, s.ActiveProfile);
            }
        }
        else if (force && (s.Theme != _themeId || s.ActiveProfile != _profileName) && _store.Exists(s.Theme, s.ActiveProfile))
        {
            SaveNow();
            if (s.Theme != _themeId) SelectThemeInCombo(s.Theme);
            LoadTheme(s.Theme, s.ActiveProfile);
        }

        RefreshIndicators();
        UpdateActiveMarkers();
        UpdateBanner();

        bool quiet = force || (_pending.Count == 0 && !_flushTimer.IsEnabled && DateTime.UtcNow - _lastLocalEdit > LocalEditQuietTime);
        if (IsLiveTarget && quiet) SyncWidgetsFromHost(s);
    }

    void SyncWidgetsFromHost(HostState s)
    {
        bool orderDiffers = !s.Widgets.Select(w => w.Id).SequenceEqual(_widgets.Select(w => w.Id));
        bool dataDiffers = s.Widgets.Any(w => _widgets.FirstOrDefault(v => v.Id == w.Id) is { } vm && Sig(vm.ToSettings(w.Order)) != Sig(w));
        if (!orderDiffers && !dataDiffers) return;
        _profile = _profile with { Widgets = s.Widgets.Select(w => w).ToList() };
        if (orderDiffers) RebuildWidgets();
        else
        {
            _loading = true;
            foreach (var w in s.Widgets) _widgets.FirstOrDefault(v => v.Id == w.Id)?.Load(w);
            _loading = false;
        }
    }

    void RefreshIndicators()
    {
        bool host = _client.Connected;
        HostDot.Fill = (Brush)FindResource(host ? "OkBrush" : "OffBrush");
        HostText.Text = host ? "Overlay: conectado" + (_host?.Fake == true ? " (simulado)" : "") : "Overlay: desconectado";
        bool game = host && _host?.GameConnected == true;
        GameDot.Fill = (Brush)FindResource(game ? "OkBrush" : host ? "WarnBrush" : "OffBrush");
        GameText.Text = game ? "AMS2: conectado" : host ? "AMS2: aguardando jogo" : "AMS2: sem dados";
        _loading = true;
        EditToggle.IsChecked = _host?.EditMode == true;
        _loading = false;
        EditToggle.Content = _host?.EditMode == true ? "Concluir edição" : "Editar layout";
        EditToggle.IsEnabled = host && IsLiveTarget;
    }

    async void EditToggle_Click(object sender, RoutedEventArgs e)
    {
        bool want = EditToggle.IsChecked == true;
        bool ok = await TrySend(IpcCommands.SetEditMode, m => m with { Edit = want });
        if (!ok) RefreshIndicators();
    }
}
