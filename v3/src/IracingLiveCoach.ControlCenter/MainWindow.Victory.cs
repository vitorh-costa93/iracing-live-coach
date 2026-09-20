using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using IracingLiveCoach.Core.Telemetry;
using IracingLiveCoach.OverlayHost.Persistence;

namespace IracingLiveCoach.ControlCenter;

/// <summary>"Vitória" card: plays the user's own audio file when they win a race (the overlay decides,
/// this only configures). The chosen file is copied under the app's data folder so the setting keeps
/// working if the original is moved; nothing is bundled with the app.</summary>
public partial class MainWindow
{
    private static string VictoryDir => Path.Combine(ProfileFiles.DataDir, "victory");

    private void LoadVictoryIntoControls()
    {
        var config = _profileStore.Victory ?? VictoryConfig.Default;
        VictoryEnabledBox.IsChecked = config.Enabled;
        VictoryFileText.Text = config.FilePath.Length == 0 ? "Nenhum arquivo escolhido" : Path.GetFileName(config.FilePath);
        VictoryFileText.ToolTip = config.FilePath;
        VictoryVolumeSlider.Value = config.VolumePct;
        VictoryRuleBox.SelectedIndex = config.Rule == VictoryRule.OverallWin ? 1 : 0;
    }

    private VictoryConfig CurrentVictory() => new(
        VictoryEnabledBox.IsChecked == true,
        (_profileStore.Victory ?? VictoryConfig.Default).FilePath,
        (int)Math.Round(VictoryVolumeSlider.Value),
        VictoryRuleBox.SelectedIndex == 1 ? VictoryRule.OverallWin : VictoryRule.ClassWin);

    private void VictoryChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressChangeEvents || !IsLoaded) return;
        Debounce("victory", () => _ = ApplyVictoryAsync(), 250);
    }

    private void VictoryVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressChangeEvents || !IsLoaded) return;
        Debounce("victory", () => _ = ApplyVictoryAsync(), 250);
    }

    private async Task ApplyVictoryAsync()
    {
        var config = CurrentVictory();
        _profileStore.Victory = config;
        bool sent = await _profilesClient.SendAsync("setVictory", JsonSerializer.Serialize(config), null);
        ReportSent(sent, "Tema da vitória");
        Debounce("save", SaveProfileStore, 600);
    }

    private async void ChooseVictoryFile(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Escolha o tema da vitória",
            Filter = "Áudio (*.mp3;*.wav;*.wma;*.m4a)|*.mp3;*.wav;*.wma;*.m4a|Todos os arquivos (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            Directory.CreateDirectory(VictoryDir);
            string target = Path.Combine(VictoryDir, Path.GetFileName(dialog.FileName));
            if (!string.Equals(Path.GetFullPath(dialog.FileName), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                File.Copy(dialog.FileName, target, overwrite: true);
            _profileStore.Victory = CurrentVictory() with { FilePath = target, Enabled = true };
            LoadVictoryIntoControls();
            await ApplyVictoryAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus("Não foi possível copiar o arquivo de áudio: " + ex.Message);
        }
    }

    private async void TestVictory(object sender, RoutedEventArgs e)
    {
        if ((_profileStore.Victory ?? VictoryConfig.Default).FilePath.Length == 0)
        {
            SetStatus("Escolha um arquivo de áudio primeiro.");
            return;
        }
        await ApplyVictoryAsync();
        bool sent = await _profilesClient.SendAsync("testVictory", "", null);
        SetStatus(sent ? "Tocando o tema no overlay…" : "O overlay precisa estar aberto para tocar o tema.");
    }

    private async void StopVictory(object sender, RoutedEventArgs e)
        => await _profilesClient.SendAsync("stopVictory", "", null);
}
