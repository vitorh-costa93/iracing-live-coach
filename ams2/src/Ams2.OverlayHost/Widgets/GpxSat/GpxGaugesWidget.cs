using System.Numerics;
using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;
using Vortice.Win32.Numerics;

namespace Ams2.OverlayHost.Widgets.GpxSat;

/// <summary>GPxCInfo Lo2k Counter.ini: mesma escala 0–360 km/h / 0–18000 rpm em todas as épocas.</summary>
public sealed class GpxGaugesWidget : IWidget
{
    public string Id => "gpxgauges";
    public (float Width, float Height) DesignSize => (256, 200);
    // Ritmo de vblank com divisor (N=2 a 144/165 Hz): ponteiros sem o judder do acumulador de 60 Hz, sem disputar todo vblank com Inputs/Radar.
    public bool HighFrequency => true;
    public double VblankTargetHz => 80;
    double _peakRpm, _peakAt, _last = double.NegativeInfinity;
    string? _session;
    WidgetSettings _settings = new() { Id = "gpxgauges" };
    public void UseTheme(Theme.Theme theme) { }
    public void Configure(WidgetSettings settings) => _settings = settings;

    public static float Angle(double value, double max) => 180 - (float)Math.Clamp(value / max, 0, 1) * 270;

    public void Draw(ThemeCanvas c, OverlayModel m)
    {
        if (!m.Connected || m.Session?.Player is not { } p) { _session = null; return; }
        string key = m.Session.Track + "\n" + m.Session.Kind;
        var telemetry = RenderPlayerTelemetry.Read(m.Inputs, p);
        double rpm = Math.Max(0, telemetry.Rpm);
        if (_session != key || m.Now < _last) { _peakRpm = rpm; _peakAt = m.Now; _session = key; }
        _last = m.Now;
        if (rpm >= _peakRpm || m.Now - _peakAt >= 1.5) { _peakRpm = rpm; _peakAt = m.Now; }
        bool original = c.GpxGaugeImage("Counter_Color", 0, 0, 200, 200);
        if (original) c.GpxGaugeImage("Counter_Display", 0, 0, 200, 200);
        else DrawFallback(c);
        c.GpxGaugeImage("Gauge_Temp", 57, 80, 40, 40);
        c.GpxGaugeImage("Gauge_Fuel", 102, 80, 40, 40);
        if (p.WaterTempC is { } temp && double.IsFinite(temp))
        {
            float a = 225 + (float)Math.Clamp((temp - 80) / 45, 0, 1) * 90;
            Needle(c, "Gauge_Needle_Shadow", 55, 71, 2, 60, a, .4f);
            Needle(c, "Gauge_Needle", 56, 70, 2, 60, a);
        }
        if (m.Fuel?.LapsRemainingOnFuel is { } fuel && double.IsFinite(fuel))
        {
            float a = 135 - (float)Math.Clamp(fuel / 20, 0, 1) * 90;
            Needle(c, "Gauge_Needle_Shadow", 140, 71, 2, 60, a, .4f);
            Needle(c, "Gauge_Needle", 141, 70, 2, 60, a);
            c.GpxGaugeImage(fuel <= 4 ? "Led1" : "Led0", 127, 105, 16, 16);
        }
        Needle(c, "Rev_Needle", 98, 0, 4, 200, Angle(_peakRpm, 18000));
        Needle(c, "Rev_Needle_Shadow", 98, 1, 2, 200, Angle(rpm, 18000), .4f);
        Needle(c, "Rev_Needle", 99, 0, 2, 200, Angle(rpm, 18000));
        Needle(c, "Speed_Needle_shadow", 87, 2, 24, 200, Angle(telemetry.SpeedMps * 3.6, 360), .4f);
        Needle(c, "Speed_Needle", 88, 0, 24, 200, Angle(telemetry.SpeedMps * 3.6, 360));
        var font = new FontToken("Arial", 700, 16 * (_settings.TextScale ?? 1)) { Element = "value" };
        var white = new Color4(1, 1, 1, 1);
        c.Text(telemetry.Gear < 0 ? "R" : telemetry.Gear == 0 ? "N" : telemetry.Gear.ToString(), font, 93, 117, 24, 24, white);
        c.Text($"{telemetry.SpeedMps * 3.6:0} km/h", font, 166, 117, 90, 24, white, HAlign.Right);
    }

    static void Needle(ThemeCanvas c, string name, float x, float y, float w, float h, float angle, float alpha = 1)
    {
        if (c.GpxGaugeImage(name, x, y, w, h, angle, alpha)) return;
        float radians = -angle * MathF.PI / 180;
        var v = Vector2.Transform(new Vector2(0, -h * .43f), Matrix3x2.CreateRotation(radians));
        c.Line(x+w/2, y+h/2, x+w/2+v.X, y+h/2+v.Y, new Color4(1, .85f, .15f, alpha), Math.Max(1, w / 8));
    }

    static void DrawFallback(ThemeCanvas c)
    {
        c.FillEllipse(100, 100, 99, 99, new Color4(.02f, .12f, .32f, .8f));
        var font = new FontToken("Arial", 700, 12);
        for (int speed = 0; speed <= 360; speed += 30)
        {
            float a = -Angle(speed, 360) * MathF.PI / 180 - MathF.PI/2;
            float x = MathF.Cos(a), y = MathF.Sin(a);
            c.Line(100+x*82, 100+y*82, 100+x*95, 100+y*95, new Color4(1, 1, 1, 1));
            c.Text(speed.ToString(), font, 100+x*70-15, 100+y*70-8, 30, 16, new Color4(1, 1, 1, 1), HAlign.Center);
        }
    }
}
