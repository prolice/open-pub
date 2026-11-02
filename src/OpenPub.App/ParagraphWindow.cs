using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenPub.Core.Model;

namespace OpenPub.App;

/// <summary>
/// Boite « Paragraphe » : alignement, retraits, espacements, interligne, lettrine et tabulations.
/// Initialisee depuis un paragraphe modele ; le resultat est applique aux paragraphes vises par l'appelant.
/// </summary>
public sealed class ParagraphWindow : Window
{
    const double CmPerPt = 2.54 / 72;
    static readonly CultureInfo Cu = CultureInfo.CurrentCulture;

    readonly ComboBox _align = Combo(("Gauche", "left"), ("Centré", "center"), ("Droite", "right"), ("Justifié", "justify"));
    readonly TextBox _left = Box(), _right = Box(), _first = Box(), _before = Box(), _after = Box(), _spacing = Box();
    readonly ComboBox _firstKind = Combo(("Aucun", "none"), ("Première ligne", "first"), ("Suspendu", "hanging"));
    readonly ComboBox _lineKind = Combo(("Simple", "1"), ("1,15", "1.15"), ("1,5 ligne", "1.5"), ("Double", "2"), ("Multiple", "multi"));
    readonly ComboBox _drop = Combo(("Aucune", "0"), ("2 lignes", "2"), ("3 lignes", "3"), ("4 lignes", "4"), ("5 lignes", "5"));
    readonly ListBox _tabs = new() { Height = 96, Margin = new Thickness(0, 4, 0, 0) };
    readonly TextBox _tabPos = Box();
    readonly ComboBox _tabAlign = Combo(("Gauche", "left"), ("Centré", "center"), ("Droite", "right"), ("Décimal", "decimal"));
    readonly ComboBox _tabLeader = Combo(("Aucun", ""), ("Points ....", "dot"), ("Tirets ----", "dash"), ("Trait ____", "line"));
    readonly List<TabStop> _stops;

    /// <summary>Applique les reglages a un paragraphe (null si annule).</summary>
    public Action<Paragraph>? Result { get; private set; }

    public ParagraphWindow(Paragraph model)
    {
        Title = "Paragraphe";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        Foreground = (Brush)Application.Current.FindResource("Ink");
        Background = (Brush)Application.Current.FindResource("Chrome");
        _stops = model.Tabs?.Select(t => t.Clone()).ToList() ?? new List<TabStop>();

        var root = new StackPanel { Margin = new Thickness(18), Width = 460 };
        root.Children.Add(Section("Général"));
        root.Children.Add(Row("Alignement", _align));
        root.Children.Add(Section("Retraits (cm)"));
        root.Children.Add(Row("À gauche", _left));
        root.Children.Add(Row("À droite", _right));
        var firstRow = new StackPanel { Orientation = Orientation.Horizontal };
        firstRow.Children.Add(_firstKind);
        _first.Margin = new Thickness(8, 0, 0, 0);
        firstRow.Children.Add(_first);
        root.Children.Add(Row("Première ligne", firstRow));
        root.Children.Add(Section("Espacement"));
        root.Children.Add(Row("Avant (pt)", _before));
        root.Children.Add(Row("Après (pt)", _after));
        var lineRow = new StackPanel { Orientation = Orientation.Horizontal };
        lineRow.Children.Add(_lineKind);
        _spacing.Margin = new Thickness(8, 0, 0, 0);
        lineRow.Children.Add(_spacing);
        root.Children.Add(Row("Interligne", lineRow));
        root.Children.Add(Row("Lettrine", _drop));

        root.Children.Add(Section("Tabulations"));
        var tabRow = new StackPanel { Orientation = Orientation.Horizontal };
        _tabPos.Width = 60;
        tabRow.Children.Add(new TextBlock { Text = "Position (cm)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        tabRow.Children.Add(_tabPos);
        _tabAlign.Margin = _tabLeader.Margin = new Thickness(6, 0, 0, 0);
        _tabAlign.Width = 90; _tabLeader.Width = 110;
        tabRow.Children.Add(_tabAlign);
        tabRow.Children.Add(_tabLeader);
        root.Children.Add(tabRow);
        var tabButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        var add = new Button { Content = "Ajouter", Padding = new Thickness(12, 4, 12, 4) };
        var del = new Button { Content = "Supprimer", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(6, 0, 0, 0) };
        var clear = new Button { Content = "Tout effacer", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(6, 0, 0, 0) };
        tabButtons.Children.Add(add); tabButtons.Children.Add(del); tabButtons.Children.Add(clear);
        root.Children.Add(tabButtons);
        root.Children.Add(_tabs);
        root.Children.Add(new TextBlock
        {
            Text = "Sans taquet, la touche Tab avance tous les 1,27 cm.", FontSize = 11.5,
            Foreground = (Brush)Application.Current.FindResource("Muted"), Margin = new Thickness(0, 4, 0, 0),
        });
        add.Click += (_, _) => AddTab();
        _tabPos.KeyDown += (_, e) => { if (e.Key == Key.Enter) { AddTab(); e.Handled = true; } };
        del.Click += (_, _) => { if (_tabs.SelectedIndex >= 0) { _stops.RemoveAt(_tabs.SelectedIndex); RefreshTabs(); } };
        clear.Click += (_, _) => { _stops.Clear(); RefreshTabs(); };
        _tabs.SelectionChanged += (_, _) =>
        {
            if (_tabs.SelectedIndex < 0) return;
            var t = _stops[_tabs.SelectedIndex];
            _tabPos.Text = Cm(t.Pos);
            Select(_tabAlign, t.Align);
            Select(_tabLeader, t.Leader ?? "");
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = "Annuler", IsCancel = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 90, Style = (Style)Application.Current.FindResource("PillButton") };
        ok.Click += (_, _) => { if (Build() is { } r) { Result = r; DialogResult = true; } };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);
        Content = root;

        // valeurs du modele
        Select(_align, model.Align is "end" ? "right" : model.Align ?? "left");
        _left.Text = Cm(model.IndentLeft ?? 0);
        _right.Text = Cm(model.IndentRight ?? 0);
        double fi = model.FirstIndent ?? 0;
        Select(_firstKind, Math.Abs(fi) < 0.01 ? "none" : fi > 0 ? "first" : "hanging");
        _first.Text = Cm(Math.Abs(fi));
        _before.Text = (model.SpaceBefore ?? 0).ToString("0.#", Cu);
        _after.Text = (model.SpaceAfter ?? 0).ToString("0.#", Cu);
        double lf = model.LineSpacingFactor();
        var preset = new[] { 1.0, 1.15, 1.5, 2.0 }.FirstOrDefault(v => Math.Abs(v - lf) < 0.001);
        Select(_lineKind, preset == 0 ? "multi" : preset.ToString(CultureInfo.InvariantCulture));
        _spacing.Text = lf.ToString("0.##", Cu);
        Select(_drop, (model.DropCap ?? 0).ToString(CultureInfo.InvariantCulture));
        _firstKind.SelectionChanged += (_, _) => _first.IsEnabled = (string)((ComboBoxItem)_firstKind.SelectedItem).Tag != "none";
        _first.IsEnabled = (string)((ComboBoxItem)_firstKind.SelectedItem).Tag != "none";
        _lineKind.SelectionChanged += (_, _) =>
        {
            var tag = (string)((ComboBoxItem)_lineKind.SelectedItem).Tag;
            _spacing.IsEnabled = tag == "multi";
            if (tag != "multi") _spacing.Text = double.Parse(tag, CultureInfo.InvariantCulture).ToString("0.##", Cu);
        };
        _spacing.IsEnabled = preset == 0;
        RefreshTabs();
    }

    void AddTab()
    {
        if (Parse(_tabPos.Text) is not { } cm || cm <= 0) return;
        double pos = cm / CmPerPt;
        _stops.RemoveAll(t => Math.Abs(t.Pos - pos) < 0.5);
        var leader = (string)((ComboBoxItem)_tabLeader.SelectedItem).Tag;
        _stops.Add(new TabStop { Pos = pos, Align = (string)((ComboBoxItem)_tabAlign.SelectedItem).Tag, Leader = leader == "" ? null : leader });
        _stops.Sort((a, b) => a.Pos.CompareTo(b.Pos));
        RefreshTabs();
    }

    void RefreshTabs()
    {
        _tabs.Items.Clear();
        foreach (var t in _stops)
        {
            string align = t.Align switch { "center" => "centré", "right" => "droite", "decimal" => "décimal", _ => "gauche" };
            string leader = t.Leader switch { "dot" => " · points de suite", "dash" => " · tirets", "line" => " · trait", _ => "" };
            _tabs.Items.Add($"{Cm(t.Pos)} cm — {align}{leader}");
        }
    }

    Action<Paragraph>? Build()
    {
        double? P(TextBox b) => Parse(b.Text);
        if (P(_left) is not { } left || P(_right) is not { } right || P(_first) is not { } first || P(_before) is not { } before
            || P(_after) is not { } after || P(_spacing) is not { } spacing || spacing <= 0 || spacing > 10)
        {
            MessageBox.Show(this, "Une des valeurs n'est pas un nombre valide.", "Paragraphe", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
        var align = (string)((ComboBoxItem)_align.SelectedItem).Tag;
        var kind = (string)((ComboBoxItem)_firstKind.SelectedItem).Tag;
        double fi = kind == "none" ? 0 : (kind == "first" ? 1 : -1) * first / CmPerPt;
        int drop = int.Parse((string)((ComboBoxItem)_drop.SelectedItem).Tag, CultureInfo.InvariantCulture);
        var tabs = _stops.Select(t => t.Clone()).ToList();
        return p =>
        {
            p.Align = align;
            p.IndentLeft = left / CmPerPt;
            p.IndentRight = right / CmPerPt;
            p.FirstIndent = fi;
            p.SpaceBefore = before;
            p.SpaceAfter = after;
            p.LineHeight = Math.Abs(spacing - 1) < 0.0001 ? null : (spacing * 100).ToString("0.####", CultureInfo.InvariantCulture) + "%";
            p.DropCap = drop >= 2 ? drop : null;
            p.Tabs = tabs.Count > 0 ? tabs.Select(t => t.Clone()).ToList() : null;
        };
    }

    static double? Parse(string text)
    {
        var sep = Cu.NumberFormat.NumberDecimalSeparator;
        return double.TryParse(text.Trim().Replace(".", sep).Replace(",", sep), NumberStyles.Float, Cu, out var v) ? v : null;
    }

    static string Cm(double pt) => (pt * CmPerPt).ToString("0.##", Cu);

    static TextBox Box() => new() { Width = 70, VerticalContentAlignment = VerticalAlignment.Center };

    static ComboBox Combo(params (string Label, string Tag)[] items)
    {
        var c = new ComboBox { Width = 140 };
        foreach (var (l, t) in items) c.Items.Add(new ComboBoxItem { Content = l, Tag = t });
        c.SelectedIndex = 0;
        return c;
    }

    static void Select(ComboBox c, string tag)
    {
        var it = c.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == tag);
        if (it is not null) c.SelectedItem = it;
    }

    static TextBlock Section(string text) => new()
    {
        Text = text, FontWeight = FontWeights.Bold, FontSize = 12.5, Margin = new Thickness(0, 12, 0, 6),
        Foreground = (Brush)Application.Current.FindResource("SelectedText"),
    };

    static Grid Row(string label, UIElement control)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var t = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(t);
        Grid.SetColumn(control, 1);
        if (control is FrameworkElement fe) fe.HorizontalAlignment = HorizontalAlignment.Left;
        g.Children.Add(control);
        return g;
    }
}
