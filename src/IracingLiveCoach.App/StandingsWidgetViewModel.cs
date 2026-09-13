// src/IracingLiveCoach.App/StandingsWidgetViewModel.cs
using System.Collections.ObjectModel;
using System.Globalization;

namespace IracingLiveCoach.App;

public class StandingsRowViewModel
{
    public string PositionText { get; }
    public string DriverCode { get; }
    public string LastLapText { get; }

    public StandingsRowViewModel(StandingsRow row)
    {
        PositionText = row.Position.ToString(CultureInfo.InvariantCulture);
        DriverCode = row.IsPlayer ? $"{row.DriverCode} *" : row.DriverCode;
        LastLapText = row.LastLapTime is double t ? t.ToString("0.000", CultureInfo.InvariantCulture) : "--";
    }
}

public class StandingsWidgetViewModel
{
    public ObservableCollection<StandingsRowViewModel> Rows { get; } = new();

    public void SetRows(System.Collections.Generic.List<StandingsRow> rows)
    {
        Rows.Clear();
        foreach (var row in rows) Rows.Add(new StandingsRowViewModel(row));
    }
}
