using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ams2.Shared.Liveries;

namespace Ams2.ControlCenter;

/// <summary>Escolha feita no seletor: pintura do catalogo (ou "" se digitada a mao) e os tres campos que o overlay mostra.</summary>
public sealed record LiveryChoice(string Livery, string Driver, string Country, string Team);

/// <summary>
/// Seletor de pintura: busca no catalogo do jogo (piloto, pais, equipe, modelo), preenche piloto/pais/equipe e deixa ajustar
/// a mao (pinturas de mod fora dos XMLs). No mesmo tema escuro do app.
/// </summary>
public sealed class LiveryPickerDialog : Window
{
    readonly List<LiveryEntry> _all;
    readonly TextBox _search = new() { Margin = new Thickness(0, 0, 0, 8) };
    readonly ListBox _list = new() { Height = 300, MinWidth = 760, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    readonly TextBox _driver = new(), _country = new() { MaxLength = 3 }, _team = new();
    readonly TextBlock _count = new() { Margin = new Thickness(0, 6, 0, 0) };
    string _livery;

    LiveryPickerDialog(string model, List<LiveryEntry> all, LiveryChoice initial)
    {
        _all = all;
        _livery = initial.Livery;
        Title = $"Pintura · {model}";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "BgBrush");

        var hint = new TextBlock { Text = "Busque por piloto, equipe, país ou modelo. O jogo não informa a pintura do seu carro, então a escolha é manual.", Margin = new Thickness(0, 0, 0, 8) };
        hint.SetResourceReference(StyleProperty, "Muted");
        _count.SetResourceReference(StyleProperty, "Muted");

        _list.ItemTemplate = BuildRowTemplate();
        _list.SelectionChanged += (_, _) =>
        {
            if (_list.SelectedItem is not LiveryEntry e) return;
            _livery = e.Livery;
            _driver.Text = e.Driver; _country.Text = e.Country; _team.Text = e.Team;
        };
        _search.TextChanged += (_, _) => Refill();

        var fields = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddField(fields, 0, "Piloto (vazio = nome do jogo)", _driver);
        AddField(fields, 1, "País", _country);
        AddField(fields, 2, "Equipe", _team);

        var ok = new Button { Content = "Usar", IsDefault = true, Padding = new Thickness(18, 6, 18, 6), Margin = new Thickness(0, 0, 8, 0) };
        ok.SetResourceReference(StyleProperty, "PrimaryButton");
        var cancel = new Button { Content = "Cancelar", IsCancel = true, Padding = new Thickness(14, 6, 14, 6) };
        ok.Click += (_, _) => DialogResult = true;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(hint);
        panel.Children.Add(_search);
        panel.Children.Add(_list);
        panel.Children.Add(_count);
        panel.Children.Add(fields);
        panel.Children.Add(buttons);
        Content = panel;

        _driver.Text = initial.Driver; _country.Text = initial.Country; _team.Text = initial.Team;
        Refill();
        // Pintura ja escolhida: seleciona de novo (sem sobrescrever os campos se ela foi editada a mao).
        Loaded += (_, _) => { _search.Focus(); };
    }

    static void AddField(Grid g, int col, string label, TextBox box)
    {
        var sp = new StackPanel { Margin = new Thickness(col == 0 ? 0 : 8, 0, 0, 0) };
        var l = new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) };
        l.SetResourceReference(StyleProperty, "Muted");
        sp.Children.Add(l);
        sp.Children.Add(box);
        Grid.SetColumn(sp, col);
        g.Children.Add(sp);
    }

    static DataTemplate BuildRowTemplate()
    {
        // Colunas: piloto | pais | equipe | modelo (nome da pasta de textura / classe)
        var grid = new FrameworkElementFactory(typeof(Grid));
        double[] widths = [230, 50, 190, 240];
        foreach (double w in widths)
        {
            var c = new FrameworkElementFactory(typeof(ColumnDefinition));
            c.SetValue(ColumnDefinition.WidthProperty, new GridLength(w));
            grid.AppendChild(c);
        }
        string[] paths = [nameof(LiveryEntry.Driver), nameof(LiveryEntry.Country), nameof(LiveryEntry.Team), nameof(LiveryEntry.Model)];
        for (int i = 0; i < paths.Length; i++)
        {
            var t = new FrameworkElementFactory(typeof(TextBlock));
            t.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(paths[i]));
            t.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            t.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 10, 0));
            t.SetBinding(FrameworkElement.ToolTipProperty, new System.Windows.Data.Binding(nameof(LiveryEntry.Livery)));
            t.SetValue(Grid.ColumnProperty, i);
            grid.AppendChild(t);
        }
        return new DataTemplate { VisualTree = grid };
    }

    void Refill()
    {
        var rows = LiveryCatalog.Filter(_all, _search.Text).Take(400).ToList();
        _list.ItemsSource = rows;
        int total = LiveryCatalog.Filter(_all, _search.Text).Count();
        _count.Text = total > rows.Count ? $"{total} pinturas; mostrando as primeiras {rows.Count} (refine a busca)" : $"{total} pinturas";
    }

    public static LiveryChoice? Ask(Window owner, string model, List<LiveryEntry> all, LiveryChoice current)
    {
        var d = new LiveryPickerDialog(model, all, current) { Owner = owner };
        DarkTitleBar.Apply(d);
        if (d.ShowDialog() != true) return null;
        string driver = d._driver.Text.Trim(), country = d._country.Text.Trim().ToUpperInvariant(), team = d._team.Text.Trim();
        // Pintura so conta se o texto ainda bate com ela; editado a mao vira escolha livre.
        var sel = d._list.SelectedItem as LiveryEntry;
        string livery = sel is not null && sel.Driver == driver && sel.Team == team && sel.Country == country ? sel.Livery : (sel is null ? d._livery : "");
        return new LiveryChoice(livery, driver, country, team);
    }
}
