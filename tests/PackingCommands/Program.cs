using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Designer.Modules.SelectionProbe;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Actual WPF routed events on an unshown visual tree; no Designer, input synthesis, or file mutation.
        var root = new Grid();
        var canvas = new Canvas();
        root.Children.Add(canvas);
        var commands = new Dictionary<RoutedCommand, PackAlignment>();
        var native = 0;
        var custom = 0;
        var errors = 0;
        var passed = 0;
        var eligible = true;
        var fail = false;
        var focus = PackingFocus.Schematic;
        PackAlignment? last = null;
        foreach (var (alignment, key) in new[] { (PackAlignment.Left, Key.L), (PackAlignment.Right, Key.R), (PackAlignment.Top, Key.T), (PackAlignment.Bottom, Key.B) })
        {
            var command = new RoutedCommand("Pack" + alignment, typeof(Program), new InputGestureCollection { new KeyGesture(key, ModifierKeys.Control | ModifierKeys.Shift) });
            commands.Add(command, alignment);
            root.CommandBindings.Add(new CommandBinding(command, (_, _) => native++, (_, e) => e.CanExecute = true));
        }
        var other = new RoutedCommand("Unrelated", typeof(Program));
        root.CommandBindings.Add(new CommandBinding(other, (_, _) => native++, (_, e) => e.CanExecute = true));
        var router = new PackingCommandRouter(root, commands,
            () => new(focus, canvas), _ => eligible,
            (alignment, target, _) =>
            {
                if (!ReferenceEquals(canvas, target)) throw new Exception("Wrong canvas");
                if (fail) throw new ArgumentException("Invalid gap");
                custom++;
                last = alignment;
            }, _ => errors++);
        void Check(string name, Action action)
        {
            action();
            Console.WriteLine("PASS " + name);
            passed++;
        }
        void Assert(bool value) { if (!value) throw new Exception("Routing contract failed"); }
        foreach (var pair in commands)
            Check(pair.Value + " uses the replacement exactly once and preserves its gesture", () =>
            {
                var before = custom;
                Assert(pair.Key.CanExecute(null, canvas));
                pair.Key.Execute(null, canvas);
                Assert(custom == before + 1 && native == 0 && last == pair.Value);
                Assert(pair.Key.InputGestures.Count == 1);
            });
        var left = commands.Keys.First();
        Check("Unsupported schematic selection disables native and replacement", () =>
        {
            eligible = false;
            var before = custom;
            Assert(!left.CanExecute(null, canvas));
            left.Execute(null, canvas);
            Assert(custom == before && native == 0);
            eligible = true;
        });
        Check("Text/editor focus blocks stale selection and continues input routing", () =>
        {
            focus = PackingFocus.Blocked;
            var before = custom;
            CanExecuteRoutedEventArgs? observed = null;
            var observe = new CanExecuteRoutedEventHandler((_, args) => observed = args);
            canvas.AddHandler(CommandManager.PreviewCanExecuteEvent, observe, handledEventsToo: true);
            Assert(!left.CanExecute(null, canvas));
            canvas.RemoveHandler(CommandManager.PreviewCanExecuteEvent, observe);
            Assert(observed is { Handled: true, CanExecute: false, ContinueRouting: true });
            left.Execute(null, canvas);
            Assert(custom == before && native == 0);
        });
        Check("Other canvas retains native packing", () =>
        {
            focus = PackingFocus.OtherCanvas;
            var before = custom;
            Assert(left.CanExecute(null, canvas));
            left.Execute(null, canvas);
            Assert(custom == before && native == 1);
        });
        Check("Unrelated commands retain their native route", () =>
        {
            focus = PackingFocus.Schematic;
            var before = custom;
            other.Execute(null, canvas);
            Assert(custom == before && native == 2);
        });
        Check("Rejected action cannot fall through to native packing", () =>
        {
            fail = true;
            var before = custom;
            left.Execute(null, canvas);
            Assert(custom == before && native == 2 && errors == 1);
            fail = false;
        });
        Check("Current context is read again at execution", () =>
        {
            Assert(left.CanExecute(null, canvas));
            focus = PackingFocus.Blocked;
            var before = custom;
            left.Execute(null, canvas);
            Assert(custom == before && native == 2);
            focus = PackingFocus.Schematic;
        });
        Check("Disposal restores native route without duplicate handlers", () =>
        {
            router.Dispose();
            router.Dispose();
            var before = custom;
            left.Execute(null, canvas);
            Assert(custom == before && native == 3);
        });
        Console.WriteLine($"{passed} WPF routing checks passed.");
    }
}
