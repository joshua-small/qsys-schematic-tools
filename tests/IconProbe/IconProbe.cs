using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Designer.Modules.Common;

namespace Designer.Modules.IconLabProbe;

// Guarded native UI acceptance only. Never install this driver in regular Designer.
public sealed class IconProbe : IDesignerModule
{
    private record Config(string ApplicationDirectory, string OutputDirectory);
    private Config? config;
    private DispatcherTimer? timer;
    private int attempts;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    public string Description => "Isolated native icon search check";
    public string MenuTitle => "Icon lab probe";
    public string Parent => "mainMenu_Edit";
    public string NestedMenu => "";
    public Key ShortcutKey => Key.None;
    public ModifierKeys ModifierKey => ModifierKeys.None;
    public string ShortcutInputGestureText => "";
    public Action Execute => () => { };

    public IconProbe()
    {
        string file = Path.Combine(AppContext.BaseDirectory, ".icon-lab-probe.json");
        if (!File.Exists(file) || !File.Exists(Path.Combine(AppContext.BaseDirectory, ".selection-probe-lab"))) return;
        config = JsonSerializer.Deserialize<Config>(File.ReadAllText(file))!;
        if (!Path.GetFullPath(config.ApplicationDirectory).TrimEnd('\\').Equals(AppContext.BaseDirectory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
            || File.Exists(Path.Combine(config.OutputDirectory, "result.json"))) return;
        timer = new DispatcherTimer(DispatcherPriority.ContextIdle) { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += async (_, _) => await Run(); timer.Start();
    }

    private static IEnumerable<DependencyObject> Tree(DependencyObject parent)
    {
        yield return parent;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Tree(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static string Name(object item) => (string)item.GetType().GetProperty("Description")!.GetValue(item)!;
    private static Task Idle() => Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle).Task;
    private static void SendKey(UIElement target, Key key, bool preview = true) => target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
        PresentationSource.FromVisual(target), Environment.TickCount, key) { RoutedEvent = preview ? Keyboard.PreviewKeyDownEvent : Keyboard.KeyDownEvent });
    private void Render(FrameworkElement root, string name)
    {
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * 2), (int)Math.Ceiling(root.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(root); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(config!.OutputDirectory, name + ".png")); encoder.Save(stream);
    }

    private async Task Run()
    {
        Window? window = null; var checks = new List<object>();
        try
        {
            if (++attempts < 10 || Application.Current.MainWindow is not { IsLoaded: true, IsEnabled: true }) return;
            timer!.Stop(); Directory.CreateDirectory(config!.OutputDirectory);
            var host = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Q-Sys Designer");
            string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(host.Location))).ToLowerInvariant();
            Require(hash == "0e7150f8dc02e0159e019188780fb003ce025f5ec18c491609cf337d849bda60", "Unexpected host.");
            var selection = host.GetType("QSC.QSys.Util.Selection")!.GetField("Global")!.GetValue(null)!;
            Require(Application.Current.MainWindow.Title.StartsWith("untitled", StringComparison.OrdinalIgnoreCase), "Requires blank disposable lab session.");
            selection.GetType().GetMethod("Clear", Type.EmptyTypes)!.Invoke(selection, null);
            Require((int)selection.GetType().GetProperty("Count")!.GetValue(selection)! == 0, "Lab must have no selected design objects.");
            ComboBox MakeCombo(string ownerType)
            {
                var owner = (FrameworkElement)Activator.CreateInstance(host.GetType("QSC.QSys.windows.Editors." + ownerType, true)!)!;
                var combo = (ComboBox)owner.GetType().GetField("_Icon", Instance)!.GetValue(owner)!;
                Require(combo.Parent is Panel, "Unexpected icon parent.");
                ((Panel)combo.Parent).Children.Remove(combo); combo.Resources = owner.Resources;
                combo.Visibility = Visibility.Visible; combo.IsEnabled = true; combo.Width = 280; combo.Height = 28;
                return combo;
            }
            var combo = MakeCombo("GraphicStylingsEditor"); var other = MakeCombo("WebPageEditor");
            var source = combo.ItemsSource; var template = combo.ItemContainerStyle;
            var all = combo.Items.Cast<object>().ToArray(); var originalTextSearch = combo.IsTextSearchEnabled;
            var panel = new StackPanel(); panel.Children.Add(combo); panel.Children.Add(other);
            window = new Window { Content = panel, Width = 420, Height = 150, Left = -32000, Top = -32000,
                ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            window.Show(); await Idle();
            Require(combo.IsLoaded && other.IsLoaded, "Native combos did not load.");
            File.WriteAllText(Path.Combine(config.OutputDirectory, "combo-template.xaml"), System.Windows.Markup.XamlWriter.Save(combo.Template));
            File.WriteAllText(Path.Combine(config.OutputDirectory, "combo-state.json"), JsonSerializer.Serialize(new { combo.Name, combo.IsLoaded, combo.IsVisible,
                combo.IsEnabled, combo.SelectedValuePath, propertyName = combo.GetType().GetProperty("PropertyName")!.GetValue(combo), count = combo.Items.Count,
                visualTypes = Tree(combo).Select(x => x.GetType().FullName).ToArray() }, new JsonSerializerOptions { WriteIndented = true }));
            combo.SelectedItem = all.Single(x => Name(x) == "Home");
            int changes = 0; combo.SelectionChanged += (_, _) => changes++;
            combo.Focus(); await Idle();
            combo.IsDropDownOpen = true; await Idle();
            var popup = combo.Template.FindName("PART_Popup", combo) as Popup ?? Tree(combo).OfType<Popup>().SingleOrDefault() ?? throw new Exception("No native popup.");
            var root = (FrameworkElement)popup.Child;
            var feature = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Designer.Modules.SelectionProbe").GetType("Designer.Modules.SelectionProbe.IconSearch")!;
            var stateProperty = (DependencyProperty)feature.GetField("StateProperty", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            File.WriteAllText(Path.Combine(config.OutputDirectory, "popup-state.json"), JsonSerializer.Serialize(new { combo.IsDropDownOpen, popup.IsOpen,
                attached = combo.GetValue(stateProperty) is not null, types = Tree(root).Select(x => x.GetType().FullName).ToArray(),
                itemTypes = all.Select(x => x.GetType().FullName).Distinct().ToArray() }, new JsonSerializerOptions { WriteIndented = true }));
            var search = Tree(root).OfType<TextBox>().Single(x => x.Name == "IconSearchText");
            Require(search.IsKeyboardFocusWithin, "Search did not receive initial keyboard focus.");
            string[] Visible() => combo.Items.Cast<object>().Where(x => combo.ItemContainerGenerator.ContainerFromItem(x) is ComboBoxItem { Visibility: Visibility.Visible }).Select(Name).ToArray();
            void Query(string query, string[] expected)
            {
                search.Text = query; root.UpdateLayout();
                Require(Visible().SequenceEqual(expected), "Wrong results for " + query + ": " + string.Join(",", Visible()));
                Require(Name(combo.SelectedItem) == "Home" && changes == 0, "Typing changed native selection.");
                checks.Add(new { query, visible = Visible(), selectionUnchanged = true });
            }
            Query("micro", new[] { "Microphone", "Microphone None" }); Render(root, "microphone");
            var visibleTiles = Tree(root).OfType<ComboBoxItem>().Where(x => x.Visibility == Visibility.Visible).ToArray();
            Require(visibleTiles.Length == 2 && Math.Abs(visibleTiles[1].TranslatePoint(new Point(), root).X - visibleTiles[0].TranslatePoint(new Point(), root).X - 35) < 0.01,
                "Filtered icons did not pack together.");
            Query("LEFT-arrow", new[] { "Arrow Left", "Arrow Left-Down", "Arrow Left-Up" });
            Query("volume", new[] { "Volume None", "Volume Strike", "Volume" });
            Query("mute", new[] { "Microphone None", "Volume Strike" }); Render(root, "mute");
            Query("MUTED", new[] { "Microphone None", "Volume Strike" });
            Query("speaker mute", new[] { "Volume Strike" });
            Query("mute speaker", new[] { "Volume Strike" });
            Query("mic-off", new[] { "Microphone None" });
            Query("speaker", new[] { "Volume None", "Volume Strike", "Volume" });
            Query("quiet", new[] { "Volume None" });
            Query("settings", new[] { "Widget" });
            Query("gear", new[] { "Widget" });
            Query("search", new[] { "Magnifying Glass", "Page Search" });
            Query("delete", new[] { "Page Delete", "Trash" });
            Query("checkmark", new[] { "Check", "Checkbox" });
            Query("warning", new[] { "Alert" });
            var queryType = feature.Assembly.GetType("Designer.Modules.SelectionProbe.IconQuery")!;
            var aliases = (System.Collections.IDictionary)queryType.GetField("Aliases", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            Require(aliases.Keys.Cast<string>().All(key => all.Any(item => Name(item) == key)), "An alias targets an unknown native icon.");
            checks.Add(new { nativeAliasTargets = aliases.Count });
            Query("does-not-exist-qsys", Array.Empty<string>()); Render(root, "no-results");
            SendKey(search, Key.Enter); await Idle();
            Require(combo.IsDropDownOpen && Name(combo.SelectedItem) == "Home" && changes == 0, "No-result Enter changed the icon.");
            Tree(root).OfType<Button>().Single(x => x.Content as string == "Clear").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(search.Text == "" && Visible().Length == all.Length && changes == 0, "Clear failed or changed the icon.");
            Query("  ", all.Select(Name).ToArray());
            SendKey(search, Key.Enter); await Idle();
            Require(!combo.IsDropDownOpen && Name(combo.SelectedItem) == "Home" && changes == 0, "Empty-query Enter changed the existing icon.");
            combo.IsDropDownOpen = true; await Idle();
            Require(other.Items.Count == all.Length - 1, "Other native filtered collection changed.");
            Require(ReferenceEquals(source, combo.ItemsSource) && ReferenceEquals(template, combo.ItemContainerStyle), "Native source or template changed.");
            search.Text = "book"; await Idle(); SendKey(search, Key.Enter); await Idle();
            Require(Name(combo.SelectedItem) == "Book" && changes == 1 && !combo.IsDropDownOpen, "Exact-name Enter did not commit once.");
            Require(combo.IsTextSearchEnabled == originalTextSearch && all.SequenceEqual(combo.Items.Cast<object>()), "Close did not restore native behavior.");
            checks.Add(new { exactNameEnter = true, commitEvents = changes, selectedValue = combo.SelectedValue });
            combo.IsDropDownOpen = true; await Idle(); Require(search.Text == "" && Visible().Length == all.Length, "Reopen did not reset search.");
            search.Text = "micro"; SendKey(search, Key.Escape); await Idle();
            Require(Name(combo.SelectedItem) == "Book" && changes == 1 && !combo.IsDropDownOpen, "Escape committed a filtered result.");
            checks.Add(new { escapePreservesSelection = true, reopeningRestoresAll = true });
            combo.Focus();
            var composition = new TextComposition(InputManager.Current, combo, "Home");
            combo.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) { RoutedEvent = TextCompositionManager.TextInputEvent });
            Require(Name(combo.SelectedItem) == "Home", "Native known-name typing no longer works.");
            checks.Add(new { nativeKnownNameTyping = true });
            int beforeKeys = changes;
            combo.IsDropDownOpen = true; await Idle(); search.Text = "micro";
            SendKey(search, Key.Down); await Idle();
            Require(Keyboard.FocusedElement is ComboBoxItem && changes == beforeKeys, "Down did not focus a result without committing.");
            var focused = (UIElement)Keyboard.FocusedElement;
            var extraText = new TextComposition(InputManager.Current, focused, " none");
            focused.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, extraText) { RoutedEvent = TextCompositionManager.PreviewTextInputEvent });
            Require(search.Text == "micro none" && search.IsKeyboardFocusWithin && Visible().SequenceEqual(new[] { "Microphone None" }) && changes == beforeKeys,
                "Typing from the grid did not continue filtering.");
            SendKey(search, Key.Down); await Idle();
            SendKey((UIElement)Keyboard.FocusedElement, Key.Enter); await Idle();
            Require(Name(combo.SelectedItem) == "Microphone None" && !combo.IsDropDownOpen && changes == beforeKeys + 1, "Native result keyboard commit failed.");
            checks.Add(new { arrowFocusWithoutCommit = true, typingFromResults = true, nativeResultEnter = true, clearAndNoResultEnter = true });
            combo.IsDropDownOpen = true; await Idle(); search.Text = "custom"; SendKey(search, Key.Enter); await Idle();
            Require(combo.IsDropDownOpen && Visible().SequenceEqual(new[] { "<CUSTOM>" }) && changes == beforeKeys + 1,
                "Custom search must preserve the native image chooser and not commit its sentinel."); combo.IsDropDownOpen = false;
            checks.Add(new { customTileRetained = true, customEnterDoesNotCommitSentinel = true });
            var nativeTemplate = combo.Template; combo.Template = null; combo.UpdateLayout(); combo.Template = nativeTemplate; combo.UpdateLayout();
            combo.Focus(); combo.IsDropDownOpen = true; await Idle();
            popup = Tree(combo).OfType<Popup>().Single(); root = (FrameworkElement)popup.Child;
            search = Tree(root).OfType<TextBox>().Single(x => x.Name == "IconSearchText"); search.Text = "micro"; await Idle();
            Require(Visible().Length == 2, "Recreated native popup lost its search."); combo.IsDropDownOpen = false;
            checks.Add(new { templateRecreation = true });
            int beforeAlias = changes;
            combo.IsDropDownOpen = true; await Idle(); search.Text = "speaker mute"; SendKey(search, Key.Enter); await Idle();
            var nativeMutedSpeaker = all.Single(item => Name(item) == "Volume Strike");
            Require(ReferenceEquals(combo.SelectedItem, nativeMutedSpeaker) && changes == beforeAlias + 1 && !combo.IsDropDownOpen
                && Equals(combo.SelectedValue, nativeMutedSpeaker.GetType().GetProperty("String")!.GetValue(nativeMutedSpeaker)),
                "Alias Enter must commit exactly one native glyph value.");
            checks.Add(new { aliasEnterUsesNativeGlyph = true });
            other.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
            other.IsDropDownOpen = true; await Idle();
            var otherPopup = other.Template.FindName("PART_Popup", other) as Popup ?? Tree(other).OfType<Popup>().Single();
            var otherSearch = Tree(otherPopup.Child).OfType<TextBox>().Single(x => x.Name == "IconSearchText");
            otherSearch.Text = "mute"; await Idle();
            Require(other.Items.Cast<object>().Count(x => other.ItemContainerGenerator.ContainerFromItem(x) is ComboBoxItem { Visibility: Visibility.Visible }) == 2,
                "Web page icon filter failed."); other.IsDropDownOpen = false;
            checks.Add(new { separateWebPageChooser = true, nativeCustomExclusionRetained = true });
            File.WriteAllText(Path.Combine(config.OutputDirectory, "result.json"), JsonSerializer.Serialize(new { status = "complete", pid = Environment.ProcessId,
                hash, originalIconCount = all.Length, checks }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error)
        {
            timer?.Stop(); Directory.CreateDirectory(config!.OutputDirectory);
            File.WriteAllText(Path.Combine(config.OutputDirectory, "result.json"), JsonSerializer.Serialize(new { status = "error", error = error.ToString(), checks }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { window?.Close(); }
    }
}
