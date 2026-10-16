using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StdMachine.Hmi.Editor;
using StdMachine.Hmi.ViewModels;
using DropProp = StdMachine.Hmi.DragDrop.Drop;

namespace StdMachine.Hmi.Views;

public partial class ProcessEditorView : UserControl
{
    public ProcessEditorView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Hook();
        // Toucher une carte la sélectionne ; toucher le fond désélectionne (le glisser ne produit pas de relâchement ici).
        CanvasView.SurfaceElement.MouseLeftButtonUp += Surface_MouseLeftButtonUp;
    }

    private ProcessEditorViewModel? Vm => DataContext as ProcessEditorViewModel;

    private void Hook()
    {
        if (Vm == null) return;
        // Cible de dépôt unique : toute la géométrie vient de la mise en page.
        DropProp.SetTarget(CanvasView.SurfaceElement, Vm.DropHandler);
    }

    private void Surface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var vm = Vm;
        if (vm == null) return;
        var p = e.GetPosition(CanvasView.SurfaceElement);
        var layout = vm.Canvas.Layout;
        var card = layout.Cards.Where(c => p.X >= c.X && p.X <= c.X + c.Width && p.Y >= c.Y && p.Y <= c.Y + c.Height).FirstOrDefault();
        if (card != null) { vm.Canvas.SelectedStepId = card.Id; return; }
        // Étiquette ronde d'un bloc : régler le bloc
        var block = layout.Blocks.Where(b => p.X >= b.X && p.X <= b.X + b.Width && Math.Abs(p.Y - b.Y) <= 16).FirstOrDefault();
        if (block != null && Math.Abs(p.X - (block.X + block.Width / 2)) < 110) { vm.Canvas.SelectedStepId = block.Id; return; }
        vm.Canvas.SelectedStepId = null;
    }
}
