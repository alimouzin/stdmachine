using System.Windows;
using System.Windows.Controls;
using StdMachine.Hmi.DragDrop;
using DropProp = StdMachine.Hmi.DragDrop.Drop;
using StdMachine.Hmi.Resources;
using StdMachine.Hmi.ViewModels;

namespace StdMachine.Hmi.Views;

public partial class DragDropTestView : UserControl
{
    public DragDropTestView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>Cible qui refuse tout, avec une raison (essai du refus expliqué sous le doigt).</summary>
    private sealed class RefuseAll : IDropTarget
    {
        public DropPreview CanAccept(object payload, Point point) => DropPreview.Reject(Res.Get("Test.RefusedReason"));
        public void Drop(object payload, DropPreview preview) { }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DragDropTestViewModel vm) return;
        Loaded -= OnLoaded;

        // Un élément de la palette (ou d'une autre liste) est copié ; dans la même liste il est déplacé.
        Func<object, object> copy = p => ((TestItem)p).Clone();
        DropProp.SetTarget(SequenceList, new ListInsertTarget(SequenceList, vm.Sequence, materialize: copy));
        DropProp.SetTarget(LongList, new ListInsertTarget(LongList, vm.Long, materialize: copy));
        DropProp.SetTarget(BranchAList, new ListInsertTarget(BranchAList, vm.BranchA, p => Conflict((TestItem)p, vm.BranchB), copy));
        DropProp.SetTarget(BranchBList, new ListInsertTarget(BranchBList, vm.BranchB, p => Conflict((TestItem)p, vm.BranchA), copy));
        DropProp.SetTarget(RefusedZone, new RefuseAll());
    }

    /// <summary>Règle de validation V004 : un objet ne peut pas être dans deux branches d'un même bloc En parallèle.</summary>
    private static string? Conflict(TestItem item, System.Collections.Generic.IEnumerable<TestItem> otherBranch)
    {
        foreach (var other in otherBranch)
            if (other.ObjectName == item.ObjectName && !ReferenceEquals(other, item))
                return Res.Format("Test.BranchConflict", item.ObjectName);
        return null;
    }
}
