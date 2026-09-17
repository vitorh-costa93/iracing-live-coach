using IracingLiveCoach.Core.Telemetry;
using IracingLiveCoach.OverlayHost.Theme;
using Vortice.Win32; using Vortice.Win32.Graphics.Direct2D; using Vortice.Win32.Graphics.DirectWrite; using Vortice.Win32.Numerics; using static Vortice.Win32.Apis;
namespace IracingLiveCoach.OverlayHost.Widgets;
/// <summary>Only SDK-confirmed side-by-side occupancy is rendered; clear radar is invisible.</summary>
public sealed unsafe class RadarWidget:IDisposable
{private readonly TelemetryReader _t=new();private readonly object _g=new();private RadarStatus? _s;private ComPtr<IDWriteTextFormat> _f;private ComPtr<ID2D1SolidColorBrush> _b;
public RadarWidget(ID2D1DeviceContext* d,IDWriteFactory* w){_f=w->CreateTextFormat("Barlow Semi Condensed",18f,fontWeight:FontWeight.SemiBold,localeName:"en-us");ThrowIfFailed(_f.Get()->SetParagraphAlignment(ParagraphAlignment.Center));ThrowIfFailed(_f.Get()->SetTextAlignment(TextAlignment.Center));var c=PaletteTokens.TextOnLight;ThrowIfFailed(d->CreateSolidColorBrush(&c,null,_b.GetAddressOf()));_t.RadarUpdated+=x=>{lock(_g)_s=x;};_t.Start();}
public void Draw(ID2D1DeviceContext* d,float x,float y,float w=180){RadarStatus? s;lock(_g)s=_s;if(s is null)return;if(s.BlindSpotLeft)Box(d,"LEFT",x,y,w*.43f);if(s.BlindSpotRight)Box(d,"RIGHT",x+w*.57f,y,w*.43f);}private void Box(ID2D1DeviceContext*d,string t,float x,float y,float w){var c=PaletteTokens.Warning;_b.Get()->SetColor(&c);var r=new RectF(x,y,x+w,y+42);d->FillRectangle(&r,(ID2D1Brush*)_b.Get());var text=new RectF(x,y,x+w,y+42);fixed(char*p=t)d->DrawText(p,(uint)t.Length,_f.Get(),&text,(ID2D1Brush*)_b.Get(),DrawTextOptions.None,MeasuringMode.Natural);}public void Dispose(){_t.Dispose();_f.Dispose();_b.Dispose();}}
