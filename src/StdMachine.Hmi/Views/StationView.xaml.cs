using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StdMachine.Hmi.ViewModels;

namespace StdMachine.Hmi.Views;

public partial class StationView : UserControl
{
    public StationView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        // Un toucher de l'utilisateur sur le canevas suspend le suivi 10 s (docs/08 § 7.2).
        CanvasView.PreviewMouseDown += (_, _) => (DataContext as StationViewModel)?.SuspendFollowCommand.Execute(null);
        CanvasView.PreviewTouchDown += (_, _) => (DataContext as StationViewModel)?.SuspendFollowCommand.Execute(null);
        Loaded += (_, _) => Dispatcher.BeginInvoke(new Action(() => (DataContext as StationViewModel)?.RequestFocus()), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is StationViewModel old) old.FocusRequested -= OnFocus;
        if (e.NewValue is StationViewModel vm) vm.FocusRequested += OnFocus;
    }

    private void OnFocus(object? sender, Editor.StepCardModel card) => CanvasView.EnsureVisible(card);
}
