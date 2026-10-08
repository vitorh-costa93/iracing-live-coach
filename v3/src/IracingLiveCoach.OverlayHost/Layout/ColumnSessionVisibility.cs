using IracingLiveCoach.Core.Telemetry;

namespace IracingLiveCoach.OverlayHost.Layout;

public static class ColumnSessionVisibility
{
    public static List<ColumnDefinition> Apply(IEnumerable<ColumnDefinition> columns, string? sessionType)
    {
        var kind = SessionKinds.Classify(sessionType);
        return columns.Select(c => c with { Visible = c.Visible && (kind switch
        {
            SessionKind.Practice => c.ShowInPractice,
            SessionKind.Qualify => c.ShowInQualify,
            SessionKind.Race => c.ShowInRace,
            _ => true,
        }) }).ToList();
    }
}
