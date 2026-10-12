using System.Collections;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Designer.Modules.Common;

namespace Designer.Modules.PaletteLabProbe;

// Lab-only: renders a separate native control; never edits the active document.
public sealed class PaletteProbe : IDesignerModule
{
    private record Config(string ApplicationDirectory, string OutputDirectory);
    private Config? config;
    private DispatcherTimer? timer;
    private int attempts;
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    public string Description => "Isolated native palette layout check";
    public string MenuTitle => "Palette lab probe";
    public string Parent => "mainMenu_Edit";
    public string NestedMenu => "";
    public Key ShortcutKey => Key.None;
    public ModifierKeys ModifierKey => ModifierKeys.None;
    public string ShortcutInputGestureText => "";
    public Action Execute => () => { };

    public PaletteProbe()
    {
        string file = Path.Combine(AppContext.BaseDirectory, ".palette-lab-probe.json");
        if (!File.Exists(file) || !File.Exists(Path.Combine(AppContext.BaseDirectory, ".selection-probe-lab"))) return;
        config = JsonSerializer.Deserialize<Config>(File.ReadAllText(file))!;
        if (!Path.GetFullPath(config.ApplicationDirectory).TrimEnd('\\').Equals(AppContext.BaseDirectory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
            || File.Exists(Path.Combine(config.OutputDirectory, "result.json"))) return;
        timer = new DispatcherTimer(DispatcherPriority.ContextIdle) { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += async (_, _) => await Run(); timer.Start();
    }

    private static IEnumerable<DependencyObject> Tree(DependencyObject parent)
    {
        yield return parent;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Tree(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }

    private static void Layout(FrameworkElement root)
    {
        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        root.Arrange(new Rect(new Point(), root.DesiredSize)); root.UpdateLayout();
    }

    private void Render(FrameworkElement root, string name)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * 2), (int)Math.Ceiling(root.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(root); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(config!.OutputDirectory, name + ".png")); encoder.Save(stream);
    }

    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static object Property(object o, string name) => o.GetType().GetProperty(name, Instance)!.GetValue(o)!;

    private async Task Run()
    {
        try
        {
            if (++attempts < 10 || Application.Current.MainWindow is not { IsLoaded: true, IsEnabled: true }) return;
            timer!.Stop();
            var host = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Q-Sys Designer");
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(host.Location))).ToLowerInvariant();
            if (hash != "0e7150f8dc02e0159e019188780fb003ce025f5ec18c491609cf337d849bda60") throw new Exception("Unexpected host");
            Directory.CreateDirectory(config!.OutputDirectory);
            var type = host.GetType("QSC.QSys.windows.Editors.ColorPalette", true)!;
            var swatch = (FrameworkElement)Activator.CreateInstance(host.GetType("QSC.QSys.windows.Editors.ColorSwatch", true)!)!;
            Require(string.IsNullOrEmpty((string?)Property(swatch, "PropertyName")), "Test swatch must not target design properties.");
            var popup = (Popup)Property(swatch, "_ColorPopup");
            var root = (FrameworkElement)popup.Child;
            Layout(root);
            var palette = Tree(root).OfType<FrameworkElement>().Single(x => x.GetType() == type);
            var list = (ListBox)type.GetField("_Colors", Instance)!.GetValue(palette)!;
            var used = (ListBox)type.GetField("_UsedColors2", Instance)!.GetValue(palette)!;
            var usedPanel = used.ItemsPanel; var usedStyle = used.ItemContainerStyle;
            var nativeTemplate = list.ItemTemplate; var nativeItems = list.Items.Cast<object>().ToArray();
            var beforeColors = nativeItems.Select(x => Property(x, "Color").ToString()).ToArray();
            Render(root, "before");
            var beforeSize = root.RenderSize.ToString();
            palette.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, palette));
            Layout(root);
            Require(Tree(list).OfType<UniformGrid>().Single().Columns == 18, "Palette layout hook did not apply.");
            Require(ReferenceEquals(nativeTemplate, list.ItemTemplate) && nativeItems.SequenceEqual(list.Items.Cast<object>()), "Native items/template replaced.");
            Require(ReferenceEquals(usedPanel, used.ItemsPanel) && ReferenceEquals(usedStyle, used.ItemContainerStyle), "Design Colors changed.");
            var cells = Enumerable.Range(0, 234).Select(i => (FrameworkElement)list.ItemContainerGenerator.ContainerFromIndex(i)).ToArray();
            var rectangles = cells.Select(c => c.TransformToAncestor(list).TransformBounds(new Rect(new Point(), c.RenderSize))).ToArray();
            for (int i = 0; i < rectangles.Length; i++)
            {
                var cell = rectangles[i];
                Require(Math.Abs(cell.Width - 20) < 0.01 && Math.Abs(cell.Height - 20) < 0.01, "Swatch dimensions differ.");
                Require(Math.Abs(cell.X - rectangles[0].X - i % 18 * 20) < 0.01 && Math.Abs(cell.Y - rectangles[0].Y - i / 18 * 20) < 0.01, "Row/column position differs.");
                Require(cell.Right <= list.ActualWidth && cell.Bottom <= list.ActualHeight, "Palette clipped.");
                list.SelectedIndex = i;
                Require(((Color)Property(palette, "SelectedColor")).ToString() == beforeColors[i], "Native swatch selection differs.");
                Require(((Color)Property(swatch, "Color")).ToString() == beforeColors[i], "Native popup color binding differs.");
            }
            var appliedStyle = list.ItemContainerStyle; var appliedPanel = list.ItemsPanel;
            palette.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, palette)); Layout(root);
            Require(ReferenceEquals(appliedStyle, list.ItemContainerStyle) && ReferenceEquals(appliedPanel, list.ItemsPanel), "Repeated load changed layout again.");
            Require(beforeColors.SequenceEqual(nativeItems.Select(x => Property(x, "Color").ToString())), "Colors changed.");
            list.SelectedIndex = 211; Layout(root); Render(root, "after");
            var fresh = (FrameworkElement)Activator.CreateInstance(type)!; Layout(fresh);
            fresh.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, fresh)); Layout(fresh);
            Require(Tree(fresh).OfType<UniformGrid>().Single().Columns == 18, "New palette did not inherit layout.");
            type.GetProperty("IsTransparencyEnabled")!.SetValue(fresh, true);
            type.GetProperty("Color")!.SetValue(fresh, Color.FromArgb(120, 1, 2, 3));
            var freshList = (ListBox)type.GetField("_Colors", Instance)!.GetValue(fresh)!; freshList.SelectedIndex = 20;
            Require(((Color)Property(fresh, "Color")).A == 120, "Native alpha selection changed.");
            // Let WPF deliver Loaded itself to a fresh palette, rather than only
            // exercising the handler with a manually raised event above.
            var automaticallyLoaded = (FrameworkElement)Activator.CreateInstance(type)!;
            var offscreen = new Window { Content = automaticallyLoaded, Width = 384, Height = 600,
                Left = -32000, Top = -32000, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            try
            {
                offscreen.Show();
                await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                offscreen.UpdateLayout();
                Require(automaticallyLoaded.IsLoaded && PresentationSource.FromVisual(automaticallyLoaded) is not null,
                    "Native WPF palette load did not occur.");
                Require(Tree(automaticallyLoaded).OfType<UniformGrid>().Single().Columns == 18,
                    "Automatic palette load did not apply layout.");
            }
            finally { offscreen.Close(); }
            var unexpected = (FrameworkElement)Activator.CreateInstance(type)!; Layout(unexpected);
            var unexpectedList = (ListBox)type.GetField("_Colors", Instance)!.GetValue(unexpected)!;
            var originalPanel = unexpectedList.ItemsPanel; var originalStyle = unexpectedList.ItemContainerStyle;
            ((IList)unexpectedList.ItemsSource).RemoveAt(0);
            var extension = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Designer.Modules.SelectionProbe")
                .GetType("Designer.Modules.SelectionProbe.PaletteRows", true)!;
            var reportField = extension.GetField("report", BindingFlags.Static | BindingFlags.NonPublic)!;
            var originalReport = reportField.GetValue(null);
            try
            {
                Action<string, object> failedDiagnostic = (_, _) => throw new IOException("Intentional lab diagnostic failure");
                reportField.SetValue(null, failedDiagnostic);
                unexpected.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, unexpected));
                Require(ReferenceEquals(originalPanel, unexpectedList.ItemsPanel) && ReferenceEquals(originalStyle, unexpectedList.ItemContainerStyle),
                    "Unexpected palette structure was modified.");
            }
            finally { reportField.SetValue(null, originalReport); }
            var items = list.Items.Cast<object>().Select((item, i) => new { index = i,
                name = item.GetType().GetProperty("Name")!.GetValue(item), color = item.GetType().GetProperty("Color")!.GetValue(item)?.ToString(),
                container = list.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement e ? new { width = e.ActualWidth, height = e.ActualHeight, position = e.TranslatePoint(new Point(), list).ToString() } : null }).ToArray();
            var tree = Tree(list).OfType<FrameworkElement>().Take(24).Select(e => new { type = e.GetType().FullName, e.Name, e.Width, e.Height, e.ActualWidth, e.ActualHeight,
                margin = e.Margin.ToString(), padding = (e as Control)?.Padding.ToString(), e.HorizontalAlignment });
            File.WriteAllText(Path.Combine(config.OutputDirectory, "result.json"), JsonSerializer.Serialize(new { status = "complete", pid = Environment.ProcessId, hash,
                beforeSize, afterSize = root.RenderSize.ToString(), rows = 13, columns = 18, all234Selections = true, nativeItemsAndTemplatePreserved = true,
                designColorsUnchanged = true, repeatedLoadStable = true, freshPaletteApplied = true, alphaPreserved = true,
                automaticWpfLoadApplied = true, unknownPalettePreserved = true, diagnosticFailureIsolated = true, items, tree },
                new JsonSerializerOptions { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
        }
        catch (Exception error)
        {
            timer?.Stop(); Directory.CreateDirectory(config!.OutputDirectory);
            File.WriteAllText(Path.Combine(config.OutputDirectory, "result.json"), JsonSerializer.Serialize(new { status = "error", error = error.ToString() }));
        }
    }
}
