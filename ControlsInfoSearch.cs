using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;

namespace Designer.Modules.SelectionProbe;

/// <summary>Filter a separate display; the host's complete txt source stays intact.</summary>
internal static class ControlsInfoSearch
{
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "SearchState", typeof(State), typeof(ControlsInfoSearch), new PropertyMetadata(null));
    private static Action<string, object>? report;
    private static bool installed;

    internal static void Install(Action<string, object> receipt)
    {
        if (installed) return;
        report = receipt;
        var type = SelectionMover.HostType("QSC.QSys.windows.ComponentInfo");
        EventManager.RegisterClassHandler(type, FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => Attach((UserControl)sender)), true);
        EventManager.RegisterClassHandler(type, Keyboard.PreviewGotKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler((sender, _) => Attach((UserControl)sender)), true);
        installed = true;
    }

    private static void Attach(UserControl owner)
    {
        if (owner.GetValue(StateProperty) is not null) return;
        try
        {
            var source = owner.GetType().GetField("txt", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner) as TextBox
                ?? throw new MissingFieldException("Native ComponentInfo.txt is unavailable.");
            if (!source.IsReadOnly || owner.Content is not Grid grid || grid.Children.Count != 1 || grid.Children[0] != source)
                throw new InvalidOperationException("Unknown native Component Controls Info layout; left unchanged.");
            var state = new State(owner, grid, source);
            owner.SetValue(StateProperty, state);
            EditorShortcuts.BindControlsInfo(owner);
        }
        catch (Exception error)
        {
            try { report?.Invoke("controls-info-search-error", new { error = error.ToString() }); }
            catch (Exception) { }
        }
    }

    private sealed class State
    {
        private readonly TextBox source;
        private readonly TextBox search = new() { Name = "ComponentInfoSearchText", Padding = new Thickness(4, 2, 4, 2), MinWidth = 80 };
        private readonly TextBox display = new() { Name = "ComponentInfoFilteredText", IsReadOnly = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        private readonly TextBlock count = new() { Margin = new Thickness(0, 4, 0, 0) };

        internal State(UserControl owner, Grid grid, TextBox source)
        {
            this.source = source;
            AutomationProperties.SetName(search, "Filter component controls");
            AutomationProperties.SetName(display, "Matching component controls");
            search.ToolTip = "Filter by control name, label, or type. All words must match. Escape clears; Ctrl+F focuses this field.";
            foreach (var property in new[] { Control.FontFamilyProperty, Control.FontSizeProperty, Control.FontStyleProperty,
                Control.FontWeightProperty, Control.ForegroundProperty, Control.BackgroundProperty, FrameworkElement.StyleProperty })
                display.SetBinding(property, new Binding(property.Name) { Source = source });
            var header = new StackPanel { Margin = new Thickness(6) };
            grid.SetBinding(Panel.BackgroundProperty, new Binding(nameof(Control.Background)) { Source = source });
            header.SetBinding(TextElement.ForegroundProperty, new Binding(nameof(Control.Foreground)) { Source = source });
            header.Children.Add(new TextBlock { Text = "Filter controls", Margin = new Thickness(0, 0, 0, 4) });
            var row = new DockPanel();
            var clear = new Button { Content = "Clear", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(6, 2, 6, 2) };
            clear.Click += (_, _) => { search.Clear(); search.Focus(); };
            DockPanel.SetDock(clear, Dock.Right); row.Children.Add(clear); row.Children.Add(search);
            header.Children.Add(row); header.Children.Add(count);
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            source.Visibility = Visibility.Collapsed;
            Grid.SetRow(display, 1); grid.Children.Add(header); grid.Children.Add(display);
            search.TextChanged += (_, _) => Filter();
            source.TextChanged += (_, _) => Filter();
            owner.PreviewTextInput += (_, e) =>
            {
                if (search.IsKeyboardFocusWithin || string.IsNullOrEmpty(e.Text)
                    || (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0) return;
                search.Focus(); search.AppendText(e.Text); search.CaretIndex = search.Text.Length; e.Handled = true;
            };
            owner.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    search.Focus(); search.SelectAll(); e.Handled = true;
                }
                else if (e.Key == Key.Escape && search.Text.Length > 0)
                {
                    search.Clear(); e.Handled = true;
                }
                else if (e.Key == Key.Back && !search.IsKeyboardFocusWithin && Keyboard.Modifiers == ModifierKeys.None && search.Text.Length > 0)
                {
                    search.Text = search.Text[..^1]; search.Focus(); search.CaretIndex = search.Text.Length; e.Handled = true;
                }
            };
            Filter();
            owner.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
            {
                if (owner.IsVisible && Window.GetWindow(owner)?.IsActive == true) search.Focus();
            }));
        }

        private void Filter()
        {
            var lines = source.Text.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
            // Keep the host's localized "select a single component" message visible.
            if (lines.Length > 0 && !lines.All(line => line.Contains(" ) ( ", StringComparison.Ordinal)))
            {
                display.Text = source.Text; count.Text = ""; return;
            }
            var words = search.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var matches = lines.Where(line => words.All(word => line.Contains(word, StringComparison.OrdinalIgnoreCase))).ToArray();
            display.Text = string.Join("\r\n", matches);
            display.ScrollToHome();
            count.Text = matches.Length == 0 && lines.Length > 0 ? $"No matching controls (0 of {lines.Length})"
                : $"{matches.Length} of {lines.Length} controls";
        }
    }
}
