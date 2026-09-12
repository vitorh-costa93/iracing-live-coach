using System;
using System.Globalization;
using Brush = System.Windows.Media.Brush;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Color = System.Windows.Media.Color;
using IracingLiveCoach.Core;

namespace IracingLiveCoach.App;

/// <summary>Formats one CornerFeedback into the labeled, unit-carrying strings the overlay actually
/// shows -- raw numbers with no label/unit ("8.4", "True") were the whole point of a review finding
/// this app is meant to fix. A null signal always renders as a neutral "--" placeholder, never a
/// fabricated zero or a misleadingly blank cell, per this project's own "never treat null as zero"
/// rule (see CornerFeedback's own doc comment).
///
/// Only WheelspinDetected gets a real good/bad color: it has an unambiguous true/false meaning.
/// BrakingDeltaMeters and CorrectionDeg are shown in a neutral accent color -- this app doesn't yet
/// compare either one against its own historical baseline threshold (CorrectionBaselineDeg is
/// fetched but not wired into a comparison; braking direction alone isn't a good/bad judgment), so
/// coloring them red/green would assert a verdict the underlying data doesn't support.</summary>
public class CornerRowViewModel
{
    public string Title { get; }
    public string BrakingText { get; }
    public string CorrectionText { get; }
    public string WheelspinText { get; }
    public Brush WheelspinBrush { get; }

    private static readonly Brush AccentBrush = new SolidColorBrush(Color.FromRgb(0x2D, 0xE2, 0xB2));
    private static readonly Brush WarnBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x54, 0x70));
    private static readonly Brush NeutralBrush = new SolidColorBrush(Color.FromRgb(0x8A, 0x93, 0xA6));

    public CornerRowViewModel(CornerFeedback feedback)
    {
        Title = feedback.CornerName is string name && name.Length > 0
            ? $"{feedback.CornerNumber}. {name}"
            : $"Curva {feedback.CornerNumber}";

        BrakingText = FormatBraking(feedback.BrakingDeltaMeters);
        CorrectionText = feedback.CorrectionDeg is double deg
            ? $"Volante: {deg.ToString("0.0", CultureInfo.InvariantCulture)}° de correção"
            : "Volante: --";

        (WheelspinText, WheelspinBrush) = feedback.WheelspinDetected switch
        {
            true => ("Tração: patinou na saída", WarnBrush),
            false => ("Tração: saída limpa", AccentBrush),
            null => ("Tração: sem dado", NeutralBrush),
        };
    }

    // BrakingDeltaMeters is only ever a raw percentage-of-lap value (not meters) when the baseline
    // response omitted trackLengthMeters -- which, per iracing-analytics's own route, only happens
    // together with an empty corners list, so a real CornerFeedback for a real corner never hits
    // that branch in practice. Labeling it "m" unconditionally reflects that real invariant.
    private static string FormatBraking(double? deltaMeters)
    {
        if (deltaMeters is not double delta) return "Frenagem: --";
        if (Math.Abs(delta) < 2) return "Frenagem: igual ao habitual";
        var meters = Math.Round(Math.Abs(delta));
        return delta > 0
            ? $"Frenagem: {meters:0}m mais tarde que o habitual"
            : $"Frenagem: {meters:0}m mais cedo que o habitual";
    }
}
