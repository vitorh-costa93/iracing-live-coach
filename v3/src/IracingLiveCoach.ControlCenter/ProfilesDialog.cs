using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using IracingLiveCoach.OverlayHost.Persistence;
using IracingLiveCoach.OverlayHost.Layout;

namespace IracingLiveCoach.ControlCenter;

/// <summary>"Gerenciar perfis": save the current layout under a name, load / duplicate / delete saved
/// ones. Built in code with the Control Center's theme resources. Loading is requested to the owner
/// (<see cref="LoadRequested"/>) because it must go through the window's reload pipeline.</summary>
public sealed class ProfilesDialog : Window
{
    private readonly WidgetPlacementStore _store;
    private readonly ListBox _list = new();
    private readonly TextBox _nameBox = new();
    private readonly TextBlock _status = new();

    public string? LoadRequested { get; private set; }

    public ProfilesDialog(WidgetPlacementStore store)
    {
        _store = store;
        Title = "Gerenciar perfis";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = (Brush)FindResource("WindowBrush");
        Foreground = (Brush)FindResource("TextBrush");
        FontFamily = (FontFamily)FindResource("UiFont");
        FontSize = 15;

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock { Text = "Perfis de layout", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        root.Children.Add(new TextBlock
        {
            Text = "Um perfil guarda posições, colunas, tipografia, formatos, regras, cores e combustível. Carregar troca o layout ao vivo (o anterior fica em _anterior).",
            Style = (Style)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14)
        });

        _list.Height = 190;
        _list.Background = (Brush)FindResource("FieldBrush");
        _list.BorderBrush = (Brush)FindResource("FieldBorderBrush");
        _list.Foreground = (Brush)FindResource("TextBrush");
        _list.Padding = new Thickness(4);
        root.Children.Add(_list);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 14) };
        buttons.Children.Add(MakeButton("Carregar", OnLoad, true));
        buttons.Children.Add(MakeButton("Duplicar", OnDuplicate, false));
        buttons.Children.Add(MakeButton("Excluir", OnDelete, false));
        root.Children.Add(buttons);

        root.Children.Add(new TextBlock { Text = "Salvar o layout atual como novo perfil", Style = (Style)FindResource("SectionLabel"), Margin = new Thickness(0, 0, 0, 6) });
        var saveRow = new StackPanel { Orientation = Orientation.Horizontal };
        _nameBox.Width = 260; _nameBox.Margin = new Thickness(0, 0, 8, 0);
        saveRow.Children.Add(_nameBox);
        saveRow.Children.Add(MakeButton("Salvar perfil", OnSave, false));
        root.Children.Add(saveRow);

        _status.Style = (Style)FindResource("Muted");
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 12, 0, 0);
        root.Children.Add(_status);

        var close = MakeButton("Fechar", (_, _) => Close(), false);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Margin = new Thickness(0, 14, 0, 0);
        root.Children.Add(close);

        Content = root;
        Refresh();
    }

    private Button MakeButton(string text, RoutedEventHandler onClick, bool primary)
    {
        var button = new Button { Content = text, Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(0, 0, 8, 0) };
        if (primary) button.Style = (Style)FindResource("PrimaryButton");
        button.Click += onClick;
        return button;
    }

    private void Refresh()
    {
        string? active = ProfileFiles.ActiveName;
        _list.Items.Clear();
        foreach (var name in ProfileFiles.List())
            _list.Items.Add(new ListBoxItem { Content = name == active ? $"{name}   (ativo)" : name, Tag = name, Foreground = (Brush)FindResource("TextBrush"), Padding = new Thickness(8, 5, 8, 5) });
    }

    private string? Selected => (_list.SelectedItem as ListBoxItem)?.Tag as string;

    private void OnLoad(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } name) { _status.Text = "Selecione um perfil."; return; }
        LoadRequested = name;
        Close();
    }

    private void OnDuplicate(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } name) { _status.Text = "Selecione um perfil."; return; }
        string copy = name + " (cópia)";
        _status.Text = ProfileFiles.Duplicate(name, copy) ? $"Criado “{copy}”." : "Não foi possível duplicar.";
        Refresh();
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } name) { _status.Text = "Selecione um perfil."; return; }
        if (MessageBox.Show(this, $"Excluir o perfil “{name}”?", "Excluir perfil", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _status.Text = ProfileFiles.Delete(name) ? $"Perfil “{name}” excluído." : "Não foi possível excluir.";
        Refresh();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        string name = _nameBox.Text.Trim();
        if (ProfileFiles.Sanitize(name).Length == 0) { _status.Text = "Informe um nome para o perfil."; return; }
        // Flush what this window holds first, so the snapshot is exactly what's on screen.
        var disk = new WidgetPlacementStore();
        PlacementPersistence.Load(disk);
        _store.ReplacePlacements(disk.All);
        _store.SessionVisibility = disk.SessionVisibility ?? _store.SessionVisibility;
        _store.ClassProfiles.Clear();
        foreach (var (key, placements) in disk.ClassProfiles) _store.ClassProfiles[key] = placements;
        PlacementPersistence.Save(_store);
        _status.Text = ProfileFiles.Save(name) ? $"Layout atual salvo como “{name}”." : "Não foi possível salvar o perfil.";
        _nameBox.Clear();
        Refresh();
    }
}
