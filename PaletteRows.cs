using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Designer.Modules.SelectionProbe;

/// <summary>Layout-only correction for the inspected native 13-by-18 palette.</summary>
internal static class PaletteRows
{
    private static readonly DependencyProperty AppliedProperty = DependencyProperty.RegisterAttached(
        "Applied", typeof(bool), typeof(PaletteRows), new PropertyMetadata(false));
    private static bool installed;
    private static Action<string, object>? report;
    private static FieldInfo? colorsField;
    private static readonly string[] Groups = { "Magenta", "Fuscia", "Violet", "Blue", "Cyan", "Aqua", "Green", "Lime", "Yellow", "Orange", "Red", "Pink", "" };

    internal static void Install(Action<string, object> receipt)
    {
        if (installed) return;
        var type = SelectionMover.HostType("QSC.QSys.windows.Editors.ColorPalette");
        colorsField = type.GetField("_Colors", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(type.FullName, "_Colors");
        report = receipt;
        EventManager.RegisterClassHandler(type, FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded));
        installed = true;
        Report("palette-rows-installed", new { rows = 13, columns = 18, swatchPitch = 20, colorsUnchanged = true });
    }

    private static void Report(string kind, object detail)
    {
        try { report?.Invoke(kind, detail); }
        catch (Exception) { /* Diagnostic I/O must not interrupt a native popup. */ }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement palette || (bool)palette.GetValue(AppliedProperty)) return;
        try
        {
            if (colorsField!.GetValue(palette) is not ListBox list || list.Items.Count != 234)
                throw new InvalidOperationException("Expected the native 234-swatch palette; left its layout unchanged.");
            for (int i = 0; i < list.Items.Count; i++)
            {
                var item = list.Items[i];
                string expected = i < 216 ? $"{Groups[i / 18]} {i % 18 + 1}" : "";
                if (item.GetType().GetProperty("Name")?.GetValue(item) as string != expected)
                    throw new InvalidOperationException("Native palette group order differs; left its layout unchanged.");
            }

            // Native cells are 16 units (15-unit swatch plus its border). Scale the
            // existing container so all template bindings, hover and selection stay native.
            var scale = new ScaleTransform(1.25, 1.25); scale.Freeze();
            var style = new Style(typeof(ListBoxItem), list.ItemContainerStyle);
            style.Setters.Add(new Setter(FrameworkElement.LayoutTransformProperty, scale));
            style.Seal();
            var panel = new FrameworkElementFactory(typeof(UniformGrid));
            panel.SetValue(UniformGrid.ColumnsProperty, 18);
            panel.SetValue(FrameworkElement.WidthProperty, 360.0);
            panel.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            var template = new ItemsPanelTemplate(panel); template.Seal();
            list.SetCurrentValue(ItemsControl.ItemContainerStyleProperty, style);
            list.SetCurrentValue(ItemsControl.ItemsPanelProperty, template);
            palette.SetValue(AppliedProperty, true);
        }
        catch (Exception error)
        {
            // An incompatible palette must not interrupt the editor or affect packing.
            palette.SetValue(AppliedProperty, true);
            Report("palette-rows-error", new { error = error.ToString() });
        }
    }
}
