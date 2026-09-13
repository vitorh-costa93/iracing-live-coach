// src/IracingLiveCoach.App/RelativeWidgetViewModel.cs
using System.Collections.ObjectModel;
using System.Globalization;
using Brush = System.Windows.Media.Brush;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Color = System.Windows.Media.Color;

namespace IracingLiveCoach.App;

// Named "Full..." to avoid colliding with the pre-existing RelativeRowViewModel in
// RelativeViewModel.cs, which backs the separate P2P strip (RelativeOverlayWindow) and is not
// touched by this task.
public class FullRelativeRowViewModel
{
    public string DriverCode { get; }
    public string GapText { get; }
    public string P2PText { get; }
    public Brush P2PBrush { get; }
    public bool IsPlayerRow { get; }

    private static readonly Brush WarnBrush = new SolidColorBrush(Color.FromRgb(0xE1, 0x06, 0x00));
    private static readonly Brush NeutralBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xAF));

    public FullRelativeRowViewModel(RelativeRow row)
    {
        IsPlayerRow = row.PositionOffset == 0;
        DriverCode = row.DriverCode;
        GapText = row.GapSeconds is double gap ? $"{(gap >= 0 ? "+" : "")}{gap.ToString("0.0", CultureInfo.InvariantCulture)}" : "--";
        (P2PText, P2PBrush) = row.P2PActive switch
        {
            true => ("P2P", WarnBrush),
            false => ("", NeutralBrush),
            null => ("", NeutralBrush),
        };
    }
}

public class RelativeWidgetViewModel
{
    public ObservableCollection<FullRelativeRowViewModel> Rows { get; } = new();

    public void SetRows(System.Collections.Generic.List<RelativeRow> rows)
    {
        Rows.Clear();
        foreach (var row in rows) Rows.Add(new FullRelativeRowViewModel(row));
    }
}
