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
    public (float Width, float Height) DesignSize => SplitMode ? (SplitW, SplitTop + SplitBandH) : BarMode ? (BarWidth, BarTop + 2 * BarPitch + 8) : (Math.Max(MinWidth, BlockEnd(Ahead, AheadX) + BlockGap + BlockWidth(Behind) + EdgeRight), RowsTop + RowsPerSide * RowPitch + 15);

    public int RowsPerSide { get; set; } = 3;
    bool _b04, _b98;
    public void UseTheme(Theme.Theme theme) { _b04 = theme.Style == ThemeStyle.Broadcast2000s; _b98 = theme.Style == ThemeStyle.Broadcast98; }
    /// <summary>1998–2001 com a coluna "bar": barra de tempo dividido (GP do Brasil 2003) entre o jogador e o vizinho mais próximo. Sem ela, a lista por lado.</summary>
    bool SplitMode => _b98 && _cfg.ColumnVisible("bar");
    /// <summary>2004–2008 com a coluna "bar": barras de gap do vídeo (vizinho imediato à frente e atrás). Sem ela, a lista por lado.</summary>
    bool BarMode => _b04 && _cfg.ColumnVisible("bar");
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
        if (SplitMode) { DrawSplit(c, t, m); return; }
        c.Panel(0, 0, w, h);
        if (BarMode) { DrawBars(c, t, m); return; }
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
        DrawColumn(c, t, ahead, AheadX, Ahead, true);
        DrawColumn(c, t, behind, BehindX, Behind, false);
    }

    // Barra de tempo dividido 1998–2001: [caixa pos][degradê][NOME] gap [NOME][degradê][caixa pos] sobre a faixa; selo "TIMING" preto no canto superior direito.
    const float SplitW = 800, SplitTop = 30, SplitBandH = 78, SplitBox = 56, SplitEdge = 20, SplitBarW = 160;

    void DrawSplit(ThemeCanvas c, Theme.Theme t, OverlayModel m)
    {
        float w = SplitW, bt = SplitTop;
        c.Panel(0, bt, w, SplitBandH);
        Chrome.BlackTag(c, w - 190, 0, 190, SplitTop - 2, "TIMING", t.Label with { Size = 21 });
        var me = m.Relative.FirstOrDefault(r => r.IsPlayer);
        if (!m.Connected || m.Session is null || me is null)
        {
            c.Text(m.Connected ? "NO DATA" : "WAITING FOR AMS2", t.Label, SplitEdge + 4, bt, 400, SplitBandH, t.LabelColor, shadow: t.TextShadow);
            return;
        }
        var ahead = m.Relative.TakeWhile(r => !r.IsPlayer).LastOrDefault();
        var behind = m.Relative.SkipWhile(r => !r.IsPlayer).Skip(1).FirstOrDefault();
        if (ahead is null && behind is null) { c.Text("NO DATA", t.Label, SplitEdge + 4, bt, 400, SplitBandH, t.LabelColor, shadow: t.TextShadow); return; }
        // Vizinho mais próximo em tempo (sem tempo = mais distante); empate: o da frente.
        static double Key(RelativeRow? r) => r is null ? double.MaxValue : r.LapDelta != 0 ? 1e6 + Math.Abs(r.LapDelta) : r.GapSeconds is { } g ? Math.Abs(g) : 1e5;
        bool aheadCloser = Key(ahead) <= Key(behind);
        var neighbor = aheadCloser ? ahead! : behind!;
        var left = aheadCloser ? neighbor : me;
        var right = aheadCloser ? me : neighbor;
        string gap = FormatGap(neighbor).TrimStart('+', '-');
        if (neighbor.LapDelta != 0) gap = Math.Abs(neighbor.LapDelta).ToString(CultureInfo.InvariantCulture) + " LAP";
        var field = m.Relative.Select(r => r.Car).ToList();
        float by = bt + 10, nameW = w / 2 - SplitEdge - SplitBox - 14 - 120;
        var big = t.Numbers with { Size = 46 };
        // esquerda
        Chrome.AccentBox(c, SplitEdge, by, SplitBox, 58, left.Car.Position.ToString(CultureInfo.InvariantCulture), big);
        float lx = SplitEdge + SplitBox + 14;
        Chrome.SplitBar(c, lx, by + 4, SplitBarW, 12, false);
        string ln = BroadcastUi.ShortName(left.Car, field).ToUpperInvariant();
        c.Text(ln, BroadcastUi.Fit(c, ln, t.Text, nameW), lx, by + 18, nameW + 10, 38, left.IsPlayer ? t.PlayerColor : t.TextColor, shadow: t.TextShadow);
        // direita (espelhada)
        Chrome.AccentBox(c, w - SplitEdge - SplitBox, by, SplitBox, 58, right.Car.Position.ToString(CultureInfo.InvariantCulture), big);
        float rx = w - SplitEdge - SplitBox - 14;
        Chrome.SplitBar(c, rx - SplitBarW, by + 4, SplitBarW, 12, true);
        string rn = BroadcastUi.ShortName(right.Car, field).ToUpperInvariant();
        c.Text(rn, BroadcastUi.Fit(c, rn, t.Text, nameW), rx - nameW - 10, by + 18, nameW + 10, 38, right.IsPlayer ? t.PlayerColor : t.TextColor, HAlign.Right, t.TextShadow);
        // gap ao centro
        var gf = t.Numbers with { Size = 44 };
        if (neighbor.LapDelta != 0) c.Text(gap, t.Label with { Size = 34 }, w / 2 - 110, by + 6, 220, 46, t.ValueColor, HAlign.Center, t.TextShadow);
        else c.Text(gap, gf, w / 2 - 110, by + 6, 220, 46, t.ValueColor, HAlign.Center, t.ValueShadow);
    }

    // Barra de gap (transmissão 2004–2008): [pos][nome à direita][gap preto][nome à esquerda][pos], células coladas.
    const float BarPos = 34, BarName = 176, BarGap = 118, BarTop = 38, BarPitch = 31, BarH = 29;
    float BarWidth => 2 * (_cfg.ColumnVisible("pos") ? BarPos : 0) + 2 * (_cfg.ColumnVisible("name") ? BarName : 0) + (_cfg.ColumnVisible("gap") ? BarGap : 0) + 8;

    void DrawBars(ThemeCanvas c, Theme.Theme t, OverlayModel m)
    {
        var (w, h) = DesignSize;
        // Cabeçalho branco com o nome do widget em teal (no lugar da legenda do patrocinador do vídeo).
        Chrome.Caption(c, 4, 7, "RELATIVE", 26, t.Label, ink: Chrome.HeaderTeal);
        if (!m.Connected || m.Session is null || m.Relative.Count == 0)
        {
            Chrome.Notice(c, m.Connected ? "NO DATA" : "WAITING FOR AMS2", 4, BarTop, 360);
            return;
        }
        var me = m.Relative.FirstOrDefault(r => r.IsPlayer);
        var ahead = m.Relative.TakeWhile(r => !r.IsPlayer).LastOrDefault();
        var behind = m.Relative.SkipWhile(r => !r.IsPlayer).Skip(1).FirstOrDefault();
        if (me is null) return;
        if (ahead is not null) DrawBar(c, t, 4, BarTop, ahead, me, true);
        if (behind is not null) DrawBar(c, t, 4, BarTop + BarPitch, me, behind, false);
    }

    /// <summary>Uma barra entre dois carros: o da frente à esquerda, o de trás à direita; <paramref name="neighbor"/> é quem o gap descreve.</summary>
    void DrawBar(ThemeCanvas c, Theme.Theme t, float x, float y, RelativeRow left, RelativeRow right, bool neighborIsLeft)
    {
        var neighbor = neighborIsLeft ? left : right;
        bool p = _cfg.ColumnVisible("pos"), n = _cfg.ColumnVisible("name"), g = _cfg.ColumnVisible("gap");
        var nf = t.Text with { Size = 22 };
        if (p) { Chrome.PositionBox(c, x, y, BarPos, BarH, left.Car.Position, t.Numbers); x += BarPos; }
        if (n) { Chrome.WhiteCell(c, x, y, BarName, BarH, ShortName(left.Car.Name), nf, HAlign.Right, left.IsPlayer ? Chrome.PlayerInk : null); x += BarName; }
        if (g)
        {
            Chrome.BlackCell(c, x, y, BarGap, BarH, FormatGap(neighbor), t.Numbers with { Size = 22 }, HAlign.Center,
                kind: neighbor.GapSeconds is null && neighbor.LapDelta == 0 ? Chrome.CellKind.Black : neighborIsLeft ? Chrome.CellKind.Orange : Chrome.CellKind.Green);
            x += BarGap;
        }
        if (n) { Chrome.WhiteCell(c, x, y, BarName, BarH, ShortName(right.Car.Name), nf, HAlign.Left, right.IsPlayer ? Chrome.PlayerInk : null); x += BarName; }
        if (p) Chrome.PositionBox(c, x, y, BarPos, BarH, right.Car.Position, t.Numbers);
    }

    /// <summary>"M Schumacher": inicial do primeiro nome + sobrenome (como na barra da transmissão).</summary>
    public static string ShortName(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return name;
        string last = string.Join(' ', parts.Skip(1));
        // sobrenome em caixa alta no AMS2 ("Vitor COSTA"): capitaliza só a primeira letra.
        if (last.All(ch => !char.IsLetter(ch) || char.IsUpper(ch)) && last.Length > 1) last = char.ToUpperInvariant(last[0]) + last[1..].ToLowerInvariant();
        return char.ToUpperInvariant(parts[0][0]) + " " + last;
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

    void DrawColumn(ThemeCanvas c, Theme.Theme t, List<RelativeRow> rows, float x, Side side, bool ahead)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            float y = RowsTop + i * RowPitch;
            string pos = row.Car.Position.ToString(CultureInfo.InvariantCulture);
            if (t.Style == ThemeStyle.Broadcast2000s) { DrawRow2000s(c, t, row, pos, x, y, side, ahead); continue; }
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

    /// <summary>Linha flutuante 2004–2008: caixa de posição, célula branca com a sigla e célula do gap: laranja para quem está à frente do jogador (perde-se tempo para ele), verde para quem está atrás.</summary>
    void DrawRow2000s(ThemeCanvas c, Theme.Theme t, RelativeRow row, string pos, float x, float y, Side side, bool ahead)
    {
        const float h = 29;
        bool p = _cfg.ColumnVisible("pos"), n = _cfg.ColumnVisible("name"), g = _cfg.ColumnVisible("gap");
        float cx = x;
        if (p) { Chrome.PositionBox(c, cx, y + 1, 34, h, row.Car.Position, t.Numbers); cx += 34; }
        if (n)
        {
            Chrome.WhiteCell(c, cx, y + 1, NameCellW, h, Code(row.Car.Name), t.Text, ink: row.IsPlayer ? Chrome.PlayerInk : null);
            cx += NameCellW;
        }
        if (g) Chrome.BlackCell(c, cx, y + 1, 112, h, FormatGap(row), t.Numbers,
            kind: row.GapSeconds is null && row.LapDelta == 0 ? Chrome.CellKind.Black : ahead ? Chrome.CellKind.Orange : Chrome.CellKind.Green);
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
