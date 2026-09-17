using IracingLiveCoach.Core.Telemetry;
using IracingLiveCoach.OverlayHost.Theme;
using Vortice.Win32;
using Vortice.Win32.Graphics.Direct2D;
using Vortice.Win32.Graphics.DirectWrite;
using Vortice.Win32.Numerics;
using static Vortice.Win32.Apis;

namespace IracingLiveCoach.OverlayHost.Widgets;

/// <summary>Direct2D weather widget. Wetness comes from the SDK TrackWetness enum only; it is
/// intentionally never inferred from precipitation.</summary>
public sealed unsafe class WeatherWidget : IDisposable
{
    private readonly TelemetryReader _telemetry = new(); private readonly object _gate = new(); private WeatherStatus? _status;
    private ComPtr<IDWriteTextFormat> _small, _large; private ComPtr<ID2D1SolidColorBrush> _brush;
    public WeatherWidget(ID2D1DeviceContext* dc, IDWriteFactory* write)
    {
        _small=write->CreateTextFormat("Barlow Semi Condensed",12f,fontWeight:FontWeight.SemiBold,localeName:"en-us"); _large=write->CreateTextFormat("Barlow Semi Condensed",17f,fontWeight:FontWeight.SemiBold,localeName:"en-us");
        ThrowIfFailed(_small.Get()->SetParagraphAlignment(ParagraphAlignment.Center)); ThrowIfFailed(_large.Get()->SetParagraphAlignment(ParagraphAlignment.Center)); var c=PaletteTokens.TextPrimary; ThrowIfFailed(dc->CreateSolidColorBrush(&c,null,_brush.GetAddressOf()));
        _telemetry.WeatherUpdated+=OnStatus; _telemetry.Start();
    }
    private void OnStatus(WeatherStatus s){lock(_gate)_status=s;}
    public void Draw(ID2D1DeviceContext* dc,float x,float y,float width=280f)
    {
        WeatherStatus? s;lock(_gate)s=_status; Set(PaletteTokens.OverlayBackground);var bg=new RectF(x,y,x+width,y+116);dc->FillRectangle(&bg,(ID2D1Brush*)_brush.Get());
        Text(dc,"WEATHER REPORT",x+8,y+4,width-16,14,_small.Get(),PaletteTokens.TextSecondary);if(s is null){Text(dc,"WAITING FOR IRACING…",x+8,y+44,width-16,24,_large.Get(),PaletteTokens.TextDisabled);return;}
        Metric(dc,"AIR",$"{s.AirTempC:0.#}°C",x+8,y+27,75,PaletteTokens.TextPrimary);Metric(dc,"TRACK",$"{s.TrackTempC:0.#}°C",x+90,y+27,75,PaletteTokens.TextPrimary);Metric(dc,"WETNESS",Wetness(s.TrackWetness),x+8,y+72,width-16,s.TrackWetness>1?PaletteTokens.WeatherWet:PaletteTokens.WeatherDry);
    }
    private void Metric(ID2D1DeviceContext* dc,string label,string value,float x,float y,float w,Color4 c){Text(dc,label,x,y,w,12,_small.Get(),PaletteTokens.TextSecondary);Text(dc,value,x,y+12,w,22,_large.Get(),c);} private void Text(ID2D1DeviceContext* dc,string t,float x,float y,float w,float h,IDWriteTextFormat* f,Color4 c){Set(c);fixed(char* p=t){var r=new RectF(x,y,x+w,y+h);dc->DrawText(p,(uint)t.Length,f,&r,(ID2D1Brush*)_brush.Get(),DrawTextOptions.None,MeasuringMode.Natural);}}private void Set(Color4 c){var a=c;_brush.Get()->SetColor(&a);}private static string Wetness(int w)=>w switch{1=>"DRY",2=>"MOSTLY DRY",3=>"VERY LIGHTLY WET",4=>"LIGHTLY WET",5=>"MODERATELY WET",6=>"VERY WET",7=>"EXTREMELY WET",_=>"—"};public void Dispose(){_telemetry.WeatherUpdated-=OnStatus;_telemetry.Dispose();_small.Dispose();_large.Dispose();_brush.Dispose();}
}
