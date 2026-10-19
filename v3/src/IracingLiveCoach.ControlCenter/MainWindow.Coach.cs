using System.IO;
using System.Speech.Synthesis;
using System.Windows;
using System.Windows.Controls;
using IracingLiveCoach.Core.Coach;

namespace IracingLiveCoach.ControlCenter;

/// <summary>"Coach" tab: edits coach-settings.json (the OverlayHost reloads it on change) and offers a
/// local voice test plus a read-only view of the Weekly Focus history.</summary>
public partial class MainWindow
{
    private const string CoachAutoVoice = "(Automatic)";

    private static readonly Dictionary<string, string> CoachSamples = new()
    {
        ["en"] = "Brake a little later into turn three, and stay smooth on the exit.",
        ["pt-BR"] = "Freie um pouco mais tarde na curva três e seja suave na saída.",
        ["es"] = "Frena un poco más tarde en la curva tres y sé suave a la salida.",
        ["de"] = "Bremse etwas später in Kurve drei und bleibe am Ausgang sauber.",
        ["fr"] = "Freine un peu plus tard au virage trois et reste souple en sortie."
    };

    private void LoadCoachIntoControls()
    {
        var s = CoachSettings.Load();
        CoachEnabledBox.IsChecked = s.Enabled;
        CoachVoiceBox.IsChecked = s.VoiceEnabled;
        CoachCornerTipsBox.IsChecked = s.CornerTips;
        CoachLapSummaryBox.IsChecked = s.LapSummary;
        CoachBrakeTonesBox.IsChecked = s.BrakeTones;
        CoachVoiceCommandsBox.IsChecked = s.VoiceCommands;
        CoachEngineerBox.IsChecked = s.EngineerEnabled;
        CoachFuelCallsBox.IsChecked = s.FuelCalls;
        CoachPitCallsBox.IsChecked = s.PitCalls;
        CoachRejoinCallsBox.IsChecked = s.RejoinCalls;
        CoachFlagCallsBox.IsChecked = s.FlagCalls;
        CoachGapCallsBox.IsChecked = s.GapCalls;
        CoachSkillBox.SelectedIndex = (int)s.Skill;
        CoachLanguageBox.SelectedIndex = 0;
        for (int i = 0; i < CoachLanguageBox.Items.Count; i++)
            if (CoachLanguageBox.Items[i] is ComboBoxItem { Tag: string tag } && tag == s.Language) CoachLanguageBox.SelectedIndex = i;
        CoachVoiceVolumeSlider.Value = Math.Clamp(s.VoiceVolume, 0, 100);
        CoachToneVolumeSlider.Value = Math.Clamp(s.ToneVolume, 0, 100);
        CoachVoiceRateSlider.Value = Math.Clamp(s.VoiceRate, -10, 10);

        CoachVoiceNameBox.Items.Clear();
        CoachVoiceNameBox.Items.Add(CoachAutoVoice);
        try
        {
            using var synth = new SpeechSynthesizer();
            foreach (var v in synth.GetInstalledVoices())
                if (v.Enabled) CoachVoiceNameBox.Items.Add(v.VoiceInfo.Name);
        }
        catch { /* no speech engine available: only automatic */ }
        CoachVoiceNameBox.SelectedItem = string.IsNullOrEmpty(s.VoiceName) ? CoachAutoVoice : s.VoiceName;
        if (CoachVoiceNameBox.SelectedIndex < 0) CoachVoiceNameBox.SelectedIndex = 0;

        RefreshCoachFocus();
    }

    private void RefreshCoachFocus()
    {
        string path = Path.Combine(CoachSettings.DefaultDirectory, "history.json");
        try
        {
            if (!File.Exists(path)) { CoachFocusText.Text = "No history yet."; return; }
            string text = File.ReadAllText(path);
            CoachFocusText.Text = text.Length > 2000 ? text[..2000] + "…" : text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CoachFocusText.Text = "Could not read history.";
        }
    }

    private CoachSettings CurrentCoach() => new()
    {
        Enabled = CoachEnabledBox.IsChecked == true,
        VoiceEnabled = CoachVoiceBox.IsChecked == true,
        CornerTips = CoachCornerTipsBox.IsChecked == true,
        LapSummary = CoachLapSummaryBox.IsChecked == true,
        BrakeTones = CoachBrakeTonesBox.IsChecked == true,
        VoiceCommands = CoachVoiceCommandsBox.IsChecked == true,
        EngineerEnabled = CoachEngineerBox.IsChecked == true,
        FuelCalls = CoachFuelCallsBox.IsChecked == true,
        PitCalls = CoachPitCallsBox.IsChecked == true,
        RejoinCalls = CoachRejoinCallsBox.IsChecked == true,
        FlagCalls = CoachFlagCallsBox.IsChecked == true,
        GapCalls = CoachGapCallsBox.IsChecked == true,
        Skill = (SkillLevel)Math.Max(0, CoachSkillBox.SelectedIndex),
        Language = (CoachLanguageBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "en",
        VoiceName = CoachVoiceNameBox.SelectedItem is string name && name != CoachAutoVoice ? name : null,
        VoiceVolume = (int)Math.Round(CoachVoiceVolumeSlider.Value),
        ToneVolume = (int)Math.Round(CoachToneVolumeSlider.Value),
        VoiceRate = (int)Math.Round(CoachVoiceRateSlider.Value)
    };

    private void CoachChanged(object sender, RoutedEventArgs e) => QueueCoachSave();

    private void CoachSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => QueueCoachSave();

    private void QueueCoachSave()
    {
        if (_suppressChangeEvents || !IsLoaded) return;
        Debounce("coach", () =>
        {
            try { CurrentCoach().Save(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SetStatus("Could not save coach settings: " + ex.Message);
            }
        }, 300);
    }

    private void CoachTestVoice(object sender, RoutedEventArgs e)
    {
        var s = CurrentCoach();
        string text = CoachSamples.TryGetValue(s.Language, out var sample) ? sample : CoachSamples["en"];
        try
        {
            var synth = new SpeechSynthesizer { Volume = s.VoiceVolume, Rate = s.VoiceRate };
            if (!string.IsNullOrEmpty(s.VoiceName))
                synth.SelectVoice(s.VoiceName);
            else
            {
                try { synth.SelectVoiceByHints(VoiceGender.NotSet, VoiceAge.NotSet, 0, new System.Globalization.CultureInfo(s.Language)); }
                catch { /* keep default voice */ }
            }
            synth.SpeakCompleted += (_, _) => synth.Dispose();
            synth.SpeakAsync(text);
        }
        catch (Exception ex)
        {
            SetStatus("Could not play the voice: " + ex.Message);
        }
        RefreshCoachFocus();
    }
}
