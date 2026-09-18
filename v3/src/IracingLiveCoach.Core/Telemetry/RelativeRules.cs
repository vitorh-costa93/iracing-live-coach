namespace IracingLiveCoach.Core.Telemetry;

/// <summary>How many cars the Relative widget shows ahead of / behind the player (spec §12 "Relative
/// (acima) / (abaixo)"). The reader always produces up to <see cref="Max"/> each side; the widget trims.</summary>
public sealed record RelativeRules(int Ahead, int Behind)
{
    public const int Max = 8;
    public static RelativeRules Default { get; } = new(3, 3);

    public RelativeRules Clamped() => new(Math.Clamp(Ahead, 0, Max), Math.Clamp(Behind, 0, Max));

    /// <summary>True when a row at this overall-position offset (negative = ahead) should be shown.</summary>
    public bool Includes(int positionOffset) => positionOffset < 0 ? -positionOffset <= Ahead : positionOffset <= Behind;
}
