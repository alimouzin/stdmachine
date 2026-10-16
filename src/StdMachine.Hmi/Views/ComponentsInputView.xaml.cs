using System.Windows.Controls;
using StdMachine.Hmi.Controls;
using StdMachine.Hmi.Resources;

namespace StdMachine.Hmi.Views;

public partial class ComponentsInputView : UserControl
{
    public ComponentsInputView()
    {
        InitializeComponent();
        // Pavé numérique et clavier posés tels quels dans la page (en service, ils s'ouvrent en surimpression).
        KeypadHost.Content = new NumericKeypad(new NumberEditRequest(Res.Get("Gallery.KeypadTitle"), -31.6, "mm", -60, 0, 0.5, 2));
        KeyboardHost.Content = new Controls.Keyboard(new TextEditRequest(Res.Get("Gallery.KeyboardTitle"), "Pressage 01", Res.Get("Gallery.KeyboardPlaceholder")));
    }
}
