using System.IO;
using System.Windows;
using System.Windows.Controls;
using Ams2.Shared.Ipc;
using Ams2.Shared.Victory;
using Microsoft.Win32;

namespace Ams2.ControlCenter;

/// <summary>
/// Tema da vitoria: ativar, volume e um arquivo de audio por tema (Padrao/Senna/Barrichello/Massa), copiado para {raiz}\victory\.
/// A config e global (victory.json). Com o host aberto ele recebe setVictory e grava; fechado, o CC grava direto (mesmo padrao dos nomes).
/// Testar/parar so tocam pelo host (o audio sai do processo do overlay).
/// </summary>
public partial class MainWindow
{
    bool _victoryLoading;

    static VictoryTheme ThemeOf(object sender)
        => sender is FrameworkElement { Tag: string t } && Enum.TryParse<VictoryTheme>(t, out var theme) ? theme : VictoryTheme.Default;

    TextBlock FileLabel(VictoryTheme t) => t switch
    {
        VictoryTheme.Senna => VictoryFileSenna,
        VictoryTheme.Barrichello => VictoryFileBarrichello,
        VictoryTheme.Massa => VictoryFileMassa,
        _ => VictoryFileDefault,
    };

    static string NameOf(VictoryTheme t) => t == VictoryTheme.Default ? "Padrão" : t.ToString();

    System.Windows.Threading.DispatcherTimer? _victoryVolumeTimer;
    int _victoryVolumePending;

    void VictoryToggle_Click(object sender, RoutedEventArgs e)
    {
        VictoryPanel.Visibility = VictoryToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (VictoryToggle.IsChecked == true) { NamesToggle.IsChecked = false; NamesPanel.Visibility = Visibility.Collapsed; RefreshVictoryUi(); }
    }

    void RefreshVictoryUi()
    {
        _victory.Refresh();
        var cfg = _victory.Current;
        _victoryLoading = true;
        try
        {
            VictoryEnabled.IsChecked = cfg.Enabled;
            VictoryVolume.Value = cfg.VolumePct;
            VictoryVolumeText.Text = cfg.VolumePct + "%";
            foreach (var t in Enum.GetValues<VictoryTheme>())
            {
                var p = cfg.PathFor(t);
                var label = FileLabel(t);
                bool ok = !string.IsNullOrWhiteSpace(p) && File.Exists(p);
                label.Text = string.IsNullOrWhiteSpace(p) ? "(sem arquivo)" : ok ? Path.GetFileName(p) : Path.GetFileName(p) + " (arquivo não encontrado)";
                label.ToolTip = string.IsNullOrWhiteSpace(p) ? null : p;
            }
            if (string.IsNullOrWhiteSpace(_victoryPlayerName)) VictoryCurrent.Text = "";
            else
            {
                var theme = VictoryThemeSelector.Select(_victoryPlayerName);
                var file = cfg.Resolve(theme);
                string used = file is null ? "silêncio (sem arquivo)" : file == cfg.PathFor(theme) ? NameOf(theme) : "Padrão (tema sem arquivo)";
                VictoryCurrent.Text = $"Piloto atual: {_victoryPlayerName}   ·   tocaria: {used}";
            }
        }
        finally { _victoryLoading = false; }
    }

    async Task MutateVictoryAsync(Func<VictoryConfig, VictoryConfig> change)
    {
        _victory.Refresh();
        var next = change(_victory.Current).Normalized();
        if (_client.Connected && await TrySend(IpcCommands.SetVictory, m => m with { Victory = next })) _victory.Refresh();
        else _victory.Save(next);
        RefreshVictoryUi();
    }

    async void VictoryEnabled_Click(object sender, RoutedEventArgs e)
    {
        if (_victoryLoading) return;
        bool on = VictoryEnabled.IsChecked == true;
        await MutateVictoryAsync(c => c with { Enabled = on });
    }

    void VictoryVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_victoryLoading || !IsLoaded) return;
        _victoryVolumePending = (int)Math.Round(e.NewValue);
        VictoryVolumeText.Text = _victoryVolumePending + "%";
        // Debounce: arrastar o controle nao grava o arquivo a cada passo.
        _victoryVolumeTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _victoryVolumeTimer.Tick -= VictoryVolumeFlush;
        _victoryVolumeTimer.Tick += VictoryVolumeFlush;
        _victoryVolumeTimer.Stop(); _victoryVolumeTimer.Start();
    }

    async void VictoryVolumeFlush(object? sender, EventArgs e)
    {
        _victoryVolumeTimer?.Stop();
        int v = _victoryVolumePending;
        await MutateVictoryAsync(c => c with { VolumePct = v });
    }

    async void VictoryPick_Click(object sender, RoutedEventArgs e)
    {
        var theme = ThemeOf(sender);
        var dlg = new OpenFileDialog
        {
            Title = $"Áudio do tema {theme}",
            Filter = "Áudio (mp3, wav, wma, m4a)|*.mp3;*.wav;*.wma;*.m4a|Todos os arquivos|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            string dest = _victory.ImportFile(theme, dlg.FileName);
            await MutateVictoryAsync(c => c.WithPath(theme, dest));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(this, ex.Message, "Tema da vitória");
        }
    }

    async void VictoryClear_Click(object sender, RoutedEventArgs e)
    {
        var theme = ThemeOf(sender);
        _victory.DeleteCopies(theme);
        await MutateVictoryAsync(c => c.WithPath(theme, ""));
    }

    async void VictoryTest_Click(object sender, RoutedEventArgs e)
    {
        var theme = ThemeOf(sender);
        if (!_client.Connected) { MessageBox.Show(this, "O teste toca pelo overlay: abra o overlay (OverlayHost) primeiro.", "Tema da vitória"); return; }
        await TrySend(IpcCommands.TestVictory, m => m with { VictoryTheme = theme.ToString() });
    }

    async void VictoryStop_Click(object sender, RoutedEventArgs e)
    {
        if (_client.Connected) await TrySend(IpcCommands.StopVictory);
    }
}
