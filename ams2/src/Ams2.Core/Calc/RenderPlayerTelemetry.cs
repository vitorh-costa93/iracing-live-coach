namespace Ams2.Core.Calc;

/// <summary>Leitura rapida para instrumentos; nao depende da cadencia dos trackers de classificacao.</summary>
public readonly record struct RenderPlayerTelemetry(double SpeedMps, double Rpm, double MaxRpm, int Gear, double Throttle, double Brake)
{
    public static RenderPlayerTelemetry Read(InputRing? ring, PlayerSnapshot player)
    {
        var fallback = new RenderPlayerTelemetry(player.SpeedMps, player.Rpm, player.MaxRpm, player.Gear,
            player.Inputs.Throttle, player.Inputs.Brake);
        if (ring is null || !ring.TryLatest(out var sample)) return fallback;
        double age = ring.Now - sample.T;
        if (age < 0 || age > 0.1 || !float.IsFinite(sample.SpeedMps) || !float.IsFinite(sample.Rpm) ||
            !float.IsFinite(sample.MaxRpm) || sample.Gear == int.MinValue) return fallback;
        return new(Math.Max(0, sample.SpeedMps), Math.Max(0, sample.Rpm), Math.Max(0, sample.MaxRpm), sample.Gear,
            float.IsFinite(sample.Throttle) ? Math.Clamp(sample.Throttle, 0, 1) : fallback.Throttle,
            float.IsFinite(sample.Brake) ? Math.Clamp(sample.Brake, 0, 1) : fallback.Brake);
    }
}
