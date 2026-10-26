using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Ams2.Shared.Profiles;

namespace Ams2.ControlCenter;

/// <summary>Aba "Telas e exibição": cartões por situação (Treino/Classificação/Corrida) sobre o mesmo modelo da Exibição; "Avançado" mantém a lista antiga.</summary>
public partial class MainWindow
{
    readonly ObservableCollection<ScreenCardVm> _cards = [];
    string _situation = SessionIds.Race;
    Dictionary<string, bool>? _defaultVisible;
    string? _defaultVisibleTheme;

    static readonly Dictionary<string, string> SituationHints = new()
    {
        [SessionIds.Practice] = "Treino: o que aparece quando você está em treino livre. Ligue ou desligue cada item e ajuste quando ele surge.",
        [SessionIds.Qualify] = "Classificação: o que aparece nas sessões de classificação. A ordem dos cartões é a prioridade do widget.",
        [SessionIds.Race] = "Corrida: o que aparece durante a corrida. Use as setas para mudar a ordem; cada frase pode ser editada ali mesmo."
    };

    bool DefaultVisible(string id)
    {
        if (_defaultVisible is null || _defaultVisibleTheme != _themeId)
        {
            var (sw, sh) = ScreenPixels();
            _defaultVisible = ProfileFactory.CreateDefault("padrao", _themeId, sw, sh).Widgets.ToDictionary(w => w.Id, w => w.Visible, StringComparer.OrdinalIgnoreCase);
            _defaultVisibleTheme = _themeId;
        }
        return _defaultVisible.TryGetValue(id, out var v) ? v : true;
    }

    /// <summary>Recria os cartões da situação atual a partir dos widgets do tema (ordem do perfil) e das peças de cada um.</summary>
    void BuildCards()
    {
        if (CardsList is null) return;
        CardsList.ItemsSource ??= _cards;
        _cards.Clear();
        foreach (var vm in _widgets)
        {
            if (WidgetCatalog.IsPiece(vm.Id) || _virtualPieces.Contains(vm.Id)) continue;
            _cards.Add(new ScreenCardVm(vm, null, 0, _situation, DefaultVisible(vm.Id), CardPreview));
            for (int i = 0; i < vm.Show.Pieces.Count; i++)
                _cards.Add(new ScreenCardVm(vm, vm.Show.Pieces[i], i, _situation, true, CardPreview));
        }
        ScreensHint.Text = SituationHints[_situation];
    }

    void ViewScreens_Click(object sender, RoutedEventArgs e) => SetView(screens: true);
    void ViewAdvanced_Click(object sender, RoutedEventArgs e) => SetView(screens: false);

    void SetView(bool screens)
    {
        ViewScreens.IsChecked = screens;
        ViewAdvanced.IsChecked = !screens;
        ScreensView.Visibility = screens ? Visibility.Visible : Visibility.Collapsed;
        WidgetsPanel.Visibility = DetailPanel.Visibility = screens ? Visibility.Collapsed : Visibility.Visible;
        if (screens) BuildCards();
    }

    void SituationTab_Click(object sender, RoutedEventArgs e)
    {
        _situation = (string)((FrameworkElement)sender).Tag;
        TabPractice.IsChecked = _situation == SessionIds.Practice;
        TabQualify.IsChecked = _situation == SessionIds.Qualify;
        TabRace.IsChecked = _situation == SessionIds.Race;
        BuildCards();
    }

    static ScreenCardVm? CardOf(object sender) => (sender as FrameworkElement)?.DataContext as ScreenCardVm;

    void CardUp_Click(object sender, RoutedEventArgs e) => MoveCard(CardOf(sender), -1);
    void CardDown_Click(object sender, RoutedEventArgs e) => MoveCard(CardOf(sender), +1);

    void MoveCard(ScreenCardVm? card, int dir)
    {
        if (card is null || card.IsPiece) return;
        // vizinho entre os widgets "de cima" (janelas de peca virtuais ficam no fim e nao contam)
        var list = _widgets.Where(w => !WidgetCatalog.IsPiece(w.Id) && !_virtualPieces.Contains(w.Id)).ToList();
        int i = list.IndexOf(card.Owner), j = i + dir;
        if (i < 0 || j < 0 || j >= list.Count) return;
        MoveWidget(card.Owner, _widgets.IndexOf(list[j]));
    }

    void CardReset_Click(object sender, RoutedEventArgs e) => CardOf(sender)?.ResetToDefault();

    void ResetAllCards_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Restaurar todos os cartões desta situação para o padrão do tema?", "Restaurar padrão", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        foreach (var c in _cards.ToArray()) c.ResetToDefault();
    }

    void CardPosition_Click(object sender, RoutedEventArgs e)
    {
        if (CardOf(sender) is not { } c) return;
        string id = c.IsPiece ? c.Owner.Id + WidgetCatalog.PieceSeparator + c.Piece!.Def.Id : c.Owner.Id;
        SetView(screens: false);
        WidgetsList.SelectedItem = _widgets.FirstOrDefault(w => string.Equals(w.Id, id, StringComparison.OrdinalIgnoreCase)) ?? c.Owner;
        WidgetsList.ScrollIntoView(WidgetsList.SelectedItem);
    }

    async void CardPreview(ScreenCardVm card)
    {
        string id = card.IsPiece ? card.Owner.Id + WidgetCatalog.PieceSeparator + card.Piece!.Def.Id : card.Owner.Id;
        var vm = _widgets.FirstOrDefault(w => string.Equals(w.Id, id, StringComparison.OrdinalIgnoreCase)) ?? card.Owner;
        card.PreviewStatus = "Gerando prévia…";
        var (img, err) = await RenderPreviewAsync(vm, "card-" + Guid.NewGuid().ToString("N")[..8]);
        card.PreviewImage = img;
        card.PreviewStatus = img is null ? err : "Dados simulados.";
    }
}
