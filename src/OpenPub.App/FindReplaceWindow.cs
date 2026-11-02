using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenPub.App;

/// <summary>Fenetre « Rechercher et remplacer » (non modale) : parcourt tous les textes du document.</summary>
public sealed class FindReplaceWindow : Window
{
    readonly MainWindow _main;
    readonly TextBox _query = new() { Width = 260 }, _replace = new() { Width = 260 };
    readonly CheckBox _case = new() { Content = "Respecter la casse et les accents", Margin = new Thickness(0, 6, 0, 0) };
    readonly CheckBox _word = new() { Content = "Mot entier", Margin = new Thickness(0, 4, 0, 0) };
    readonly StackPanel _replaceRow;
    readonly Button _replaceOne, _replaceAll;
    readonly TextBlock _info = new() { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 380 };

    public FindReplaceWindow(MainWindow main)
    {
        _main = main;
        Title = "Rechercher et remplacer";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        Foreground = (Brush)Application.Current.FindResource("Ink");
        Background = (Brush)Application.Current.FindResource("Chrome");
        _info.Foreground = (Brush)Application.Current.FindResource("Muted");

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(Row("Rechercher", _query));
        _replaceRow = Row("Remplacer par", _replace);
        root.Children.Add(_replaceRow);
        root.Children.Add(_case);
        root.Children.Add(_word);
        root.Children.Add(_info);
        var buttons = new WrapPanel { Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        var next = new Button { Content = "Suivant", IsDefault = true, MinWidth = 90, Style = (Style)Application.Current.FindResource("PillButton") };
        _replaceOne = new Button { Content = "Remplacer", MinWidth = 90, Margin = new Thickness(6, 0, 0, 0) };
        _replaceAll = new Button { Content = "Remplacer tout", MinWidth = 110, Margin = new Thickness(6, 0, 0, 0) };
        var close = new Button { Content = "Fermer", IsCancel = true, MinWidth = 80, Margin = new Thickness(6, 0, 0, 0) };
        buttons.Children.Add(next); buttons.Children.Add(_replaceOne); buttons.Children.Add(_replaceAll); buttons.Children.Add(close);
        root.Children.Add(buttons);
        Content = root;

        next.Click += (_, _) => { if (!_main.FindNext(_query.Text, MatchCase, Whole)) _info.Text = $"« {_query.Text} » est introuvable."; else _info.Text = ""; };
        _replaceOne.Click += (_, _) => { _main.ReplaceCurrent(_query.Text, _replace.Text, MatchCase, Whole); _info.Text = ""; };
        _replaceAll.Click += (_, _) =>
        {
            int n = _main.ReplaceAll(_query.Text, _replace.Text, MatchCase, Whole);
            _info.Text = n == 0 ? $"« {_query.Text} » est introuvable." : $"{n} remplacement(s) effectué(s). Ctrl+Z pour annuler.";
        };
        close.Click += (_, _) => Close();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.F1) { Help.HelpService.Open(Help.HelpService.FindReplace); e.Handled = true; } };
    }

    bool MatchCase => _case.IsChecked == true;
    bool Whole => _word.IsChecked == true;

    public void Prepare(bool replace, string? initial)
    {
        Title = replace ? "Rechercher et remplacer" : "Rechercher";
        _replaceRow.Visibility = _replaceOne.Visibility = _replaceAll.Visibility = replace ? Visibility.Visible : Visibility.Collapsed;
        if (initial is not null) _query.Text = initial;
        _query.Focus();
        _query.SelectAll();
    }

    static StackPanel Row(string label, Control box)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 4) };
        sp.Children.Add(new TextBlock { Text = label, Width = 110, VerticalAlignment = VerticalAlignment.Center });
        sp.Children.Add(box);
        return sp;
    }
}
