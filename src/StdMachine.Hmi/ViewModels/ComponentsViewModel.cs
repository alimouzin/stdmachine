using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StdMachine.Core;
using StdMachine.Hmi.Resources;
using StdMachine.Hmi.Services;
using System.Windows;
using System.Windows.Controls;

namespace StdMachine.Hmi.ViewModels;

/// <summary>Page de démonstration des composants dans tous leurs états (critère de la phase 0).</summary>
public partial class ComponentsViewModel : ViewModelBase
{
    [ObservableProperty] private double _overrideValue = 80;
    [ObservableProperty] private double _overrideZero = 0;
    [ObservableProperty] private double _numberValue = -31.6;
    [ObservableProperty] private double _forceValue = 850;
    [ObservableProperty] private string _textValue = "OF 24-117";
    [ObservableProperty] private bool _toggleA = true;
    [ObservableProperty] private bool _toggleB;
    [ObservableProperty] private int _tabIndex;
    [ObservableProperty] private int _segmentIndex = 1;
    [ObservableProperty] private string _holdResult = "";

    public PackMlState[] States { get; } = (PackMlState[])Enum.GetValues(typeof(PackMlState));

    [RelayCommand]
    private void ShowToast(string kind)
    {
        var k = (ToastKind)Enum.Parse(typeof(ToastKind), kind);
        AppServices.Toast.Show(Res.Get("Gallery.Toast." + kind), k);
    }

    [RelayCommand]
    private void OpenDrawer()
    {
        var body = new StackPanel { Margin = new Thickness(24, 0, 24, 24) };
        body.Children.Add(new TextBlock { Text = Res.Get("Gallery.DrawerBody"), TextWrapping = TextWrapping.Wrap, Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Text.Secondary") });
        AppServices.Drawer.Show(Res.Get("Gallery.DrawerTitle"), body);
    }

    [RelayCommand]
    private void Held() => HoldResult = Res.Format("Gallery.HoldDone", DisplayFormat.Clock(DateTime.Now));

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        var ok = await AppServices.Dialogs.ConfirmAsync(Res.Get("Gallery.ConfirmTitle"), Res.Get("Gallery.ConfirmBody"), Res.Get("Gallery.ConfirmOk"), danger: true);
        AppServices.Toast.Show(ok ? Res.Get("Gallery.Confirmed") : Res.Get("Gallery.Cancelled"), ok ? ToastKind.Success : ToastKind.Info);
    }
}

/// <summary>Seconde page de démonstration : saisie, balises, progression et surimpressions.</summary>
public sealed class ComponentsInputViewModel : ComponentsViewModel
{
}
