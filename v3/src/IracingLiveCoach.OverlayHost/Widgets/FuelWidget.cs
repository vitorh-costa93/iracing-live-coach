using System.Globalization;
using IracingLiveCoach.Core.Telemetry;
using IracingLiveCoach.OverlayHost.Theme;
using Vortice.Win32;
using Vortice.Win32.Graphics.Direct2D;
using Vortice.Win32.Graphics.DirectWrite;
using Vortice.Win32.Numerics;
using static Vortice.Win32.Apis;

namespace IracingLiveCoach.OverlayHost.Widgets;

public sealed unsafe class FuelWidget : IDisposable
{
    private readonly TelemetryReader _telemetry=new(); private readonly object _gate=new(); private FuelStatus? _status; private ComPtr<IDWriteTextFormat> _small,_big; private ComPtr<ID2D1SolidColorBrush> _brush;
    public FuelWidget(ID2D1DeviceContext* dc,IDWriteFactory* write){_small=write->CreateTextFormat("Barlow Semi Condensed",13f,fontWeight:FontWeight.SemiBold,localeName:"en-us");_big=write->CreateTextFormat("Barlow Semi Condensed",26f,fontWeight:FontWeight.SemiBold,localeName:"en-us");ThrowIfFailed(_small.Get()->SetParagraphAlignment(ParagraphAlignment.Center));ThrowIfFailed(_big.Get()->SetParagraphAlignment(ParagraphAlignment.Center));var c=PaletteTokens.TextPrimary;ThrowIfFailed(dc->CreateSolidColorBrush(&c,null,_brush.GetAddressOf()));_telemetry.FuelUpdated+=OnStatus;_telemetry.Start();}
    private void OnStatus(FuelStatus s){lock(_gate)_status=s;}
    public void Draw(ID2D1DeviceContext* dc,float x,float y,float width=300){FuelStatus? s;lock(_gate)s=_status;Set(PaletteTokens.OverlayBackground);var bg=new RectF(x,y,x+width,y+138);dc->FillRectangle(&bg,(ID2D1Brush*)_brush.Get());Text(dc,"FUEL CALCULATOR",x+8,y+4,width-16,15,_small.Get(),PaletteTokens.TextSecondary);if(s is null){Text(dc,"WAITING FOR IRACING…",x+8,y+50,width-16,22,_small.Get(),PaletteTokens.TextDisabled);return;}Metric(dc,"FUEL LEVEL",$"{s.FuelLevelLiters:0.0} L",x+8,y+28,width*.45f,PaletteTokens.TextPrimary);var refuel=s.FuelNeededForFinishLiters is >0?$"+{s.FuelNeededForFinishLiters.Value:0.0} L":"—";Metric(dc,"REFUEL",refuel,x+width*.52f,y+28,width*.4f,s.FuelNeededForFinishLiters is >0?PaletteTokens.Warning:PaletteTokens.TextDisabled);Text(dc,$"AVG/LAP {s.AverageFuelPerLapLiters?.ToString("0.00",CultureInfo.InvariantCulture)??"—"} L    RANGE {s.LapsRemaining?.ToString("0.0",CultureInfo.InvariantCulture)??"—"} LAPS",x+8,y+97,width-16,18,_small.Get(),PaletteTokens.TextSecondary);}
    private void Metric(ID2D1DeviceContext* dc,string l,string v,float x,float y,float w,Color4 c){Text(dc,l,x,y,w,13,_small.Get(),PaletteTokens.TextSecondary);Text(dc,v,x,y+14,w,34,_big.Get(),c);}private void Text(ID2D1DeviceContext* dc,string t,float x,float y,float w,float h,IDWriteTextFormat* f,Color4 c){Set(c);fixed(char* p=t){var r=new RectF(x,y,x+w,y+h);dc->DrawText(p,(uint)t.Length,f,&r,(ID2D1Brush*)_brush.Get(),DrawTextOptions.None,MeasuringMode.Natural);}}private void Set(Color4 c){var a=c;_brush.Get()->SetColor(&a);}public void Dispose(){_telemetry.FuelUpdated-=OnStatus;_telemetry.Dispose();_small.Dispose();_big.Dispose();_brush.Dispose();}
}
