using System.Windows.Controls;
using StdMachine.Hmi.DragDrop;
using StdMachine.Hmi.ViewModels;
using DropProp = StdMachine.Hmi.DragDrop.Drop;

namespace StdMachine.Hmi.Views;

public partial class FunctionEditorView : UserControl
{
    public FunctionEditorView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not FunctionEditorViewModel vm) return;
            // Suite d'actions : insertion avant ou après la carte survolée, déplacement par la poignée ; chaque dépôt est une modification annulable.
            var target = new ListInsertTarget(StepsList, vm.Steps, FunctionEditorViewModel.ValidateDrop, vm.Materialize, _ => vm.OnListChanged());
            DropProp.SetTarget(StepsList, target);
            DropProp.SetTarget(EmptyZone, target);
        };
    }
}
