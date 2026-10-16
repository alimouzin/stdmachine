using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StdMachine.Hmi.ViewModels;

namespace StdMachine.Hmi.Views;

public partial class MachineView : UserControl
{
    public MachineView()
    {
        InitializeComponent();
    }

    /// <summary>Une commande indisponible, touchée, explique pourquoi (la bulle est dans la vue-modèle).</summary>
    private void Unavailable_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || DataContext is not MachineViewModel vm) return;
        vm.ExplainUnavailableCommand.Execute(new object?[] { fe, fe.Tag });
        e.Handled = true;
    }
}
