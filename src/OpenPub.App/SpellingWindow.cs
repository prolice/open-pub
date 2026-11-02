using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenPub.App;

/// <summary>
/// Verification de l'orthographe de tout le document (F7) : chaque mot inconnu est selectionne dans le texte,
/// avec des suggestions ; Remplacer, Ignorer, Ignorer tout, Ajouter au dictionnaire.
/// </summary>
public sealed class SpellingWindow : Window
{
    readonly MainWindow _main;
    readonly TextBlock _word = new() { FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 2, 0, 8) };
    readonly ListBox _suggestions = new() { Height = 150, Width = 260 };
    readonly TextBox _custom = new() { Width = 260, Margin = new Thickness(0, 6, 0, 0) };
    readonly Button _replace, _ignore, _ignoreAll, _add;
    (string Word, string? Lang)? _current;

    public SpellingWindow(MainWindow main)
    {
        _main = main;
        Title = "Orthographe";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        Foreground = (Brush)Application.Current.FindResource("Ink");
        Background = (Brush)Application.Current.FindResource("Chrome");

        var grid = new Grid { Margin = new Thickness(16) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel();
        left.Children.Add(new TextBlock { Text = "Absent du dictionnaire :", Foreground = (Brush)Application.Current.FindResource("Muted") });
        _word.Foreground = new SolidColorBrush(Color.FromRgb(0xd1, 0x34, 0x38));
        left.Children.Add(_word);
        left.Children.Add(new TextBlock { Text = "Suggestions :", Margin = new Thickness(0, 0, 0, 4) });
        left.Children.Add(_suggestions);
        left.Children.Add(new TextBlock { Text = "Ou remplacer par :", Margin = new Thickness(0, 8, 0, 0) });
        left.Children.Add(_custom);
        grid.Children.Add(left);
        var right = new StackPanel { Margin = new Thickness(14, 20, 0, 0) };
        Button B(string text, bool primary = false)
        {
            var b = new Button { Content = text, MinWidth = 160, Margin = new Thickness(0, 0, 0, 6) };
            if (primary) b.Style = (Style)Application.Current.FindResource("PillButton");
            right.Children.Add(b);
            return b;
        }
        _replace = B("Remplacer", true);
        _ignore = B("Ignorer");
        _ignoreAll = B("Ignorer tout");
        _add = B("Ajouter au dictionnaire");
        var close = B("Fermer");
        close.IsCancel = true;
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        Content = grid;

        _suggestions.SelectionChanged += (_, _) => { if (_suggestions.SelectedItem is string s) _custom.Text = s; };
        _suggestions.MouseDoubleClick += (_, _) => Replace();
        _replace.Click += (_, _) => Replace();
        _ignore.Click += (_, _) => Next();
        _ignoreAll.Click += (_, _) => { if (_current is { } c) { Spell?.Ignore(c.Word); _main.RefreshSpelling(); } Next(); };
        _add.Click += (_, _) => { if (_current is { } c) { Spell?.Add(c.Word, c.Lang); _main.RefreshSpelling(); } Next(); };
        close.Click += (_, _) => Close();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.F1) { Help.HelpService.Open(Help.HelpService.Review); e.Handled = true; } };
    }

    static Editing.SpellService? Spell => EditorSession.SharedSpell.Value;

    void Replace()
    {
        if (_current is null || _custom.Text.Length == 0) { Next(); return; }
        _main.ReplaceSelection(_custom.Text);
        Next();
    }

    /// <summary>Passe a la faute suivante, ou annonce la fin de la verification.</summary>
    public void Next()
    {
        _current = _main.NextSpellingError();
        _suggestions.Items.Clear();
        if (_current is not { } c)
        {
            _word.Text = "Vérification terminée";
            _word.Foreground = (Brush)Application.Current.FindResource("Ink");
            _custom.Text = "";
            _replace.IsEnabled = _ignore.IsEnabled = _ignoreAll.IsEnabled = _add.IsEnabled = false;
            return;
        }
        _word.Text = c.Word;
        _word.Foreground = new SolidColorBrush(Color.FromRgb(0xd1, 0x34, 0x38));
        _replace.IsEnabled = _ignore.IsEnabled = _ignoreAll.IsEnabled = _add.IsEnabled = true;
        foreach (var s in Spell?.Suggest(c.Word, c.Lang) ?? new List<string>()) _suggestions.Items.Add(s);
        if (_suggestions.Items.Count > 0) _suggestions.SelectedIndex = 0;
        else _custom.Text = c.Word;
    }
}
