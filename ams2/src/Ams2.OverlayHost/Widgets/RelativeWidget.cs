using System.Globalization;
using Ams2.Core.Calc;
using Ams2.OverlayHost.Data;
using Ams2.OverlayHost.Theme;
using Ams2.Shared.Profiles;
using Vortice.Win32.Numerics;

namespace Ams2.OverlayHost.Widgets;

/// <summary>
/// Relative no layout da transmissão 1998–2001: cabeçalho (caixa amarela, título, barra dourada) e duas colunas,
/// AHEAD e BEHIND, com posição, sigla e gap. Coordenadas em unidades de design = pixels do mockup 1536x1024.
/// </summary>
public sealed class RelativeWidget : IWidget
{
    public string Id => "relative";
    public (float Width, float Height) DesignSize => (Math.Max(MinWidth, BlockEnd(Ahead, AheadX) + BlockGap + BlockWidth(Behind) + EdgeRight), RowsTop + RowsPerSide * RowPitch + 15);

    public int RowsPerSide { get; set; } = 3;
    WidgetSettings _cfg = new() { Id = "relative" };
    public void Configure(WidgetSettings s) { _cfg = s; RowsPerSide = s.Rows ?? 3; }

    // Geometria (extraída de ams2\mockups\v3\1990s\overlay.png)
    const float PanelLeft = 0;
    const float HeaderCenterY = 22;
    const float RowsTop = 72;     // topo da primeira linha
    const float RowPitch = 31;
    const float AheadX = 68;      // início da coluna AHEAD (número)
    const float PosSlot = 6, NameCellW = 112, MinWidth = 420, BlockGap = 126, EdgeRight = 62;

    /// <summary>Colunas visiveis de um lado: posicoes relativas ao inicio do bloco, sem buracos. Todas visiveis = mockup.</summary>
    readonly record struct Side(float PosDx, float NameDx, float ValueRight, float Width);
    Side Ahead => SideLayout(50, 279);
    Side Behind => SideLayout(43, 285);
    float BehindX => AheadX + Ahead.Width + BlockGap;
    static float BlockEnd(Side s, float x) => x + s.Width;
    static float BlockWidth(Side s) => s.Width;

    Side SideLayout(float nameDx, float valueRight)
    {
        bool pos = _cfg.ColumnVisible("pos"), name = _cfg.ColumnVisible("name"), gap = _cfg.ColumnVisible("gap");
        float cursor = pos ? nameDx : PosSlot;   // sem posição, o texto do nome fica 6 px à direita da célula
        float nx = cursor;
        if (name) cursor = nx + NameCellW - 0;
        else if (!pos) cursor = 0;
        float gapRight = 0;
        if (gap) { gapRight = cursor + (valueRight - nameDx - NameCellW); cursor = gapRight; }
        return new Side(0, nx, gapRight, Math.Max(cursor, 60));
    }

    public void Draw(ThemeCanvas c, OverlayModel m)
    {
        var t = c.Theme;
        var (w, h) = DesignSize;
        c.Panel(0, 0, w, h);
        DrawHeader(c, t);

        if (!m.Connected || m.Session is null || m.Relative.Count == 0)
        {
            c.Text(m.Connected ? "NO DATA" : "WAITING FOR AMS2", t.Label, AheadX, 52, 400, 30, t.LabelColor, shadow: t.TextShadow);
            return;
        }

        var ahead = m.Relative.TakeWhile(r => !r.IsPlayer).Reverse().Take(RowsPerSide).ToList(); // mais próximo primeiro
        var behind = m.Relative.SkipWhile(r => !r.IsPlayer).Skip(1).Take(RowsPerSide).ToList();

        if (t.Style == ThemeStyle.Broadcast2000s)
        {
            Chrome.Caption(c, AheadX, 42, "AHEAD", 24, t.Label with { Size = 18 });
            Chrome.Caption(c, BehindX, 42, "BEHIND", 24, t.Label with { Size = 18 });
        }
        else
        {
            c.Text("AHEAD", t.Label, AheadX, 44, 200, 26, t.LabelColor, shadow: t.TextShadow);
            c.Text("BEHIND", t.Label, BehindX, 44, 200, 26, t.LabelColor, shadow: t.TextShadow);
        }
        DrawColumn(c, t, ahead, AheadX, Ahead);
        DrawColumn(c, t, behind, BehindX, Behind);
    }

    static void DrawHeader(ThemeCanvas c, Theme.Theme t)
    {
        if (t.Style != ThemeStyle.Broadcast98) { Chrome.Header(c, "RELATIVE", 20, 7, 160); return; }
        // Caixa amarela com o ícone (duas pontas de seta, em tinta escura).
        const float bx = 20, by = 11, bw = 45, bh = 22;
        c.FillRect(bx, by, bw, bh, t.AccentFill);
        c.Text("RELATIVE", t.Title, 78, HeaderCenterY - 15, 220, 30, t.TitleColor, shadow: t.TextShadow);
        float titleW = c.Measure("RELATIVE", t.Title);
        float barX = Math.Max(222, 78 + titleW + 10);
        c.GradientBar(barX, 11, 160, 15, t.TitleBar);
        DrawIcon(c, t, bx, by, bw, bh);
    }

    static void DrawIcon(ThemeCanvas c, Theme.Theme t, float x, float y, float w, float h)
    {
        // Duas barras diagonais escuras: lembra o ícone de "carros em fila" do mockup, sem depender de glifo.
        float cx = x + w / 2;
        for (int i = 0; i < 2; i++)
        {
            float oy = y + 5 + i * 8;
            c.FillRect(cx - 7, oy, 14, 3, t.AccentInk);
        }
        c.FillRect(cx - 2, y + 3, 4, h - 6, t.AccentInk);
    }

    void DrawColumn(ThemeCanvas c, Theme.Theme t, List<RelativeRow> rows, float x, Side side)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            float y = RowsTop + i * RowPitch;
            string pos = row.Car.Position.ToString(CultureInfo.InvariantCulture);
            if (t.Style == ThemeStyle.Broadcast2000s) { DrawRow2000s(c, t, row, pos, x, y, side); continue; }
            if (_cfg.ColumnVisible("pos"))
            {
                if (t.Style == ThemeStyle.Broadcast98) c.Text(pos, t.Numbers, x, y, 36, 30, t.NumberColor, shadow: t.ValueShadow);
                else Chrome.AccentBox(c, x, y + 2, 34, 26, pos, t.Numbers);
            }
            if (_cfg.ColumnVisible("name"))
            {
                var ink = row.IsPlayer && t.NameCellFill.A <= 0f ? t.PlayerColor : t.TextColor;
                ink = Chrome.NameCell(c, x + side.NameDx - 6, y + 2, 118, 26, ink);
                c.Text(Code(row.Car.Name), t.Text, x + side.NameDx, y, 130, 30, ink, shadow: t.NameCellFill.A > 0f ? null : t.TextShadow);
            }
            if (_cfg.ColumnVisible("gap")) c.Text(FormatGap(row), t.Numbers, x + side.ValueRight - 140, y, 140, 30, t.ValueColor, HAlign.Right, t.ValueShadow);
        }
    }

    /// <summary>Linha flutuante 2004–2008: caixa vermelha com a posição, célula branca com a sigla, célula preta com o gap.</summary>
    void DrawRow2000s(ThemeCanvas c, Theme.Theme t, RelativeRow row, string pos, float x, float y, Side side)
    {
        const float h = 28;
        bool p = _cfg.ColumnVisible("pos"), n = _cfg.ColumnVisible("name"), g = _cfg.ColumnVisible("gap");
        float cx = x;
        if (p) { Chrome.Cell(c, cx, y + 1, 34, h, pos, t.Numbers, t.AccentFill, t.AccentInk, HAlign.Center, 0); cx += 34; }
        if (n)
        {
            Chrome.WhiteCell(c, cx, y + 1, NameCellW, h, Code(row.Car.Name), t.Text, ink: row.IsPlayer ? t.AccentFill : null);
            cx += NameCellW;
        }
        if (g) Chrome.BlackCell(c, cx, y + 1, 112, h, FormatGap(row), t.Numbers);
    }

    /// <summary>Sigla de 3 letras: início do sobrenome (última palavra do nome), em maiúsculas.</summary>
    public static string Code(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string last = parts.Length > 0 ? parts[^1] : name;
        last = new string(last.Where(char.IsLetter).ToArray());
        return (last.Length >= 3 ? last[..3] : last).ToUpperInvariant();
    }

    public static string FormatGap(RelativeRow r)
    {
        if (r.LapDelta != 0) return (r.LapDelta > 0 ? "+" : "-") + Math.Abs(r.LapDelta).ToString(CultureInfo.InvariantCulture) + "L";
        if (r.GapSeconds is not { } g) return "--.---";
        return "+" + Math.Abs(g).ToString("0.000", CultureInfo.InvariantCulture);
    }
}
