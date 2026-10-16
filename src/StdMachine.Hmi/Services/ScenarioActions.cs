using System.Windows;
using System.Windows.Media;
using StdMachine.Hmi.Controls;
using StdMachine.Hmi.ViewModels;

namespace StdMachine.Hmi.Services;

/// <summary>
/// Actions déclenchées après l'ouverture d'une page par <c>--scenario</c> : elles servent à capturer les surimpressions
/// (pavé, clavier, tiroir, dialogue, toasts) sans toucher l'écran.
/// </summary>
public static class ScenarioActions
{
    public static void Apply(string? scenario, DependencyObject root)
    {
        switch (scenario)
        {
            case "toast":
                AppServices.Toast.Show("Projet enregistré.", ToastKind.Success);
                AppServices.Toast.Show("Refusée : la station n'est pas en Idle.", ToastKind.Error);
                break;
            case "drawer":
                (FindDataContext(root) as ComponentsViewModel)?.OpenDrawerCommand.Execute(null);
                break;
            case "dialog":
                (FindDataContext(root) as ComponentsViewModel)?.ConfirmCommand.Execute(null);
                break;
            case "keypad":
                if (Find<NumberField>(root) is NumberField nf) _ = OpenNumber(nf);
                break;
            case "keyboard":
                if (Find<Controls.TextField>(root) is Controls.TextField tf) _ = OpenText(tf);
                break;
            case "override":
                Find<OverrideControl>(root)?.ShowSheet();
                break;
            case "login":
                (System.Windows.Application.Current.MainWindow as MainWindow)?.ShowLogin();
                break;
        }
    }

    private static Task OpenNumber(NumberField f)
        => AppServices.Keypad.EditNumberAsync(f, new NumberEditRequest(f.Label, f.Value, f.Unit, f.Minimum, f.Maximum, f.Step, f.Decimals));

    private static Task OpenText(Controls.TextField f)
        => AppServices.Keypad.EditTextAsync(f, new TextEditRequest(f.Label, f.Text, f.Placeholder));

    private static object? FindDataContext(DependencyObject root) => (Find<FrameworkElement>(root, fe => fe.DataContext is ViewModelBase))?.DataContext;

    private static T? Find<T>(DependencyObject root, Func<T, bool>? predicate = null) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t && (predicate == null || predicate(t))) return t;
            var found = Find(child, predicate);
            if (found != null) return found;
        }
        return null;
    }
}
