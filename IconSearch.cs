using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Designer.Modules.SelectionProbe;

/// <summary>Search presentation only; native items, source filters and commit handlers stay intact.</summary>
internal static class IconSearch
{
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(State), typeof(IconSearch), new PropertyMetadata(null));
    private static Action<string, object>? report;
    private static bool installed;

    internal static void Install(Action<string, object> receipt)
    {
        if (installed) return;
        report = receipt;
        var type = SelectionMover.HostType("QSC.QSys.windows.Editors.ValueComboBox");
        EventManager.RegisterClassHandler(type,
            FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded), true);
        // Designer constructs some editor controls before loading modules. Their
        // cached Loaded-event flags predate our class handler, so also attach on
        // mouse/keyboard entry before the native combo opens its popup.
        EventManager.RegisterClassHandler(type, Mouse.PreviewMouseDownEvent,
            new MouseButtonEventHandler((sender, args) => OnLoaded(sender, args)), true);
        EventManager.RegisterClassHandler(type, Keyboard.PreviewGotKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler((sender, args) => OnLoaded(sender, args)), true);
        installed = true;
        Report("icon-search-installed", new { scope = "Native legacy IconString dropdowns", matching = "All name/alias fragments, case and punctuation insensitive" });
    }

    private static void Report(string kind, object detail)
    {
        try { report?.Invoke(kind, detail); }
        catch (Exception) { /* Diagnostics must not interrupt the native editor. */ }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ComboBox combo || combo.GetValue(StateProperty) is not null) return;
        try
        {
            if (combo.Name != "_Icon" || combo.SelectedValuePath != "String"
                || combo.GetType().GetProperty("PropertyName")?.GetValue(combo) as string != "IconString") return;
            var state = new State(combo);
            combo.SetValue(StateProperty, state);
            Report("icon-search-attached", new { combo.Name, count = combo.Items.Count });
        }
        catch (Exception error) { Report("icon-search-error", new { error = error.ToString() }); }
    }

    private sealed class State
    {
        private readonly ComboBox combo;
        private readonly TextBox search = new() { Name = "IconSearchText", MinWidth = 180, Padding = new Thickness(4, 2, 4, 2) };
        private readonly TextBlock count = new() { Margin = new Thickness(0, 4, 0, 0), FontSize = 11 };
        private readonly Dictionary<ComboBoxItem, Visibility> originalVisibility = new();
        private Popup? popup;
        private UIElement? body;
        private DockPanel? wrapper;
        private bool applying;
        private bool active;
        private bool textSearchEnabled;

        internal State(ComboBox combo)
        {
            this.combo = combo;
            AutomationProperties.SetName(search, "Search icons");
            search.ToolTip = "Search icon names or common terms such as mute, speaker, or settings. Enter selects a match; Escape closes without changing the icon.";
            search.TextChanged += (_, _) => Guard(ApplyFilter);
            combo.DropDownOpened += (_, _) => Guard(Open);
            combo.DropDownClosed += (_, _) => Guard(Close);
            combo.ItemContainerGenerator.StatusChanged += (_, _) =>
            {
                if (combo.IsDropDownOpen && combo.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated)
                    Guard(ApplyFilter);
            };
        }

        private static string Name(object item) => item.GetType().GetProperty("Description")?.GetValue(item) as string ?? "";
        private object[] Matches() => combo.Items.Cast<object>().Where(item => IconQuery.Matches(Name(item), search.Text)).ToArray();
        private void Guard(Action action)
        {
            try { action(); }
            catch (Exception error) { Report("icon-search-error", new { error = error.ToString() }); }
        }

        private void Open()
        {
            var itemType = SelectionMover.HostType("QSC.QSys.windows.Editors.ButtonIcon");
            if (combo.Items.Count == 0 || !combo.Items.Cast<object>().All(itemType.IsInstanceOfType)) return;
            combo.ApplyTemplate();
            var current = combo.Template.FindName("PART_Popup", combo) as Popup
                ?? FindVisual<Popup>(combo)
                ?? throw new InvalidOperationException("Native icon dropdown has no standard popup; search was not attached.");
            if (!ReferenceEquals(current, popup) || !ReferenceEquals(current.Child, wrapper))
            {
                if (body is not null) wrapper?.Children.Remove(body);
                if (popup is not null && ReferenceEquals(popup.Child, wrapper))
                {
                    popup.Child = null; popup.Child = body;
                }
                if (search.Parent is Panel oldRow) oldRow.Children.Remove(search);
                if (count.Parent is Panel oldHeader) oldHeader.Children.Remove(count);
                popup = current; body = popup.Child;
                if (body is null) throw new InvalidOperationException("Native icon popup has no content.");
                wrapper = new DockPanel { LastChildFill = true, MinWidth = 385 };
                wrapper.SetBinding(Panel.BackgroundProperty, new Binding(nameof(Control.Background)) { Source = combo });
                wrapper.SetBinding(TextElement.ForegroundProperty, new Binding(nameof(Control.Foreground)) { Source = combo });
                var header = new StackPanel { Margin = new Thickness(6) };
                header.Children.Add(new TextBlock { Text = "Search icons", Margin = new Thickness(0, 0, 0, 4) });
                var row = new DockPanel();
                var clear = new Button { Content = "Clear", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(6, 2, 6, 2) };
                clear.Click += (_, _) => { search.Clear(); search.Focus(); };
                DockPanel.SetDock(clear, Dock.Right); row.Children.Add(clear); row.Children.Add(search);
                header.Children.Add(row); header.Children.Add(count);
                DockPanel.SetDock(header, Dock.Top); wrapper.Children.Add(header);
                popup.Child = null; wrapper.Children.Add(body); popup.Child = wrapper;
                wrapper.PreviewKeyDown += OnKey;
                wrapper.PreviewTextInput += (_, args) =>
                {
                    if (search.IsKeyboardFocusWithin || string.IsNullOrEmpty(args.Text)) return;
                    search.Focus(); search.AppendText(args.Text); search.CaretIndex = search.Text.Length; args.Handled = true;
                };
            }
            textSearchEnabled = combo.IsTextSearchEnabled;
            combo.SetCurrentValue(ItemsControl.IsTextSearchEnabledProperty, false);
            active = true;
            search.Clear(); ApplyFilter();
            combo.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (!combo.IsDropDownOpen) return;
                search.Focus(); Keyboard.Focus(search);
            }));
        }

        private void ApplyFilter()
        {
            if (applying || !active || !combo.IsDropDownOpen) return;
            applying = true;
            try
            {
                for (int i = 0; i < combo.Items.Count; i++)
                {
                    if (combo.ItemContainerGenerator.ContainerFromIndex(i) is not ComboBoxItem tile) continue;
                    originalVisibility.TryAdd(tile, tile.Visibility);
                    tile.SetCurrentValue(UIElement.VisibilityProperty,
                        IconQuery.Matches(Name(combo.Items[i]), search.Text) ? originalVisibility[tile] : Visibility.Collapsed);
                }
                int found = Matches().Length;
                count.Text = found == 0 ? "No matching icons" : $"{found} icon{(found == 1 ? "" : "s")}";
                if (body is not null) FindVisual<ScrollViewer>(body)?.ScrollToTop();
            }
            finally { applying = false; }
        }

        private void Close()
        {
            if (!active) return;
            active = false;
            foreach (var pair in originalVisibility) pair.Key.SetCurrentValue(UIElement.VisibilityProperty, pair.Value);
            originalVisibility.Clear();
            combo.SetCurrentValue(ItemsControl.IsTextSearchEnabledProperty, textSearchEnabled);
            search.Clear();
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true; combo.SetCurrentValue(ComboBox.IsDropDownOpenProperty, false); combo.Focus();
            }
            else if (e.Key == Key.Enter && (search.IsKeyboardFocusWithin || Keyboard.FocusedElement is ComboBoxItem))
            {
                e.Handled = true;
                var matches = Matches();
                var choice = Keyboard.FocusedElement is ComboBoxItem focused
                    ? combo.ItemContainerGenerator.ItemFromContainer(focused)
                    : IconQuery.Normalize(search.Text).Length == 0 ? combo.SelectedItem
                    : matches.FirstOrDefault(item => IconQuery.Normalize(Name(item)) == IconQuery.Normalize(search.Text)) ?? matches.FirstOrDefault();
                if (choice is null) return;
                if (!matches.Contains(choice)) return;
                if (choice.GetType().FullName == "QSC.QSys.windows.Editors.CustomButtonIcon")
                {
                    // The native Custom tile opens a file dialog on its template's
                    // mouse handler; selecting the sentinel alone cannot import an image.
                    count.Text = "Click Custom to choose an image";
                    return;
                }
                // Use the native selector and its existing value/undo handlers only on explicit confirmation.
                combo.SetCurrentValue(Selector.SelectedItemProperty, choice);
                combo.SetCurrentValue(ComboBox.IsDropDownOpenProperty, false); combo.Focus();
            }
            else if (search.IsKeyboardFocusWithin && e.Key == Key.Down)
            {
                e.Handled = true;
                if (Matches().FirstOrDefault() is { } first && combo.ItemContainerGenerator.ContainerFromItem(first) is ComboBoxItem tile)
                    tile.Focus();
            }
        }

        private static T? FindVisual<T>(DependencyObject root) where T : DependencyObject
        {
            if (root is T found) return found;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                if (FindVisual<T>(VisualTreeHelper.GetChild(root, i)) is { } child) return child;
            return null;
        }
    }
}
