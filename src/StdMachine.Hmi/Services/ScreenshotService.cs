using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using StdMachine.Hmi.Infrastructure;

namespace StdMachine.Hmi.Services;

/// <summary>Rendu d'une page hors écran en PNG (docs/07 § 10) : sert à vérifier le design sans écran.</summary>
public sealed class ScreenshotService
{
    private readonly Window _window;
    private readonly NavigationService _navigation;

    public ScreenshotService(Window window, NavigationService navigation)
    {
        _window = window;
        _navigation = navigation;
    }

    /// <summary>Prépare la fenêtre pour un rendu hors écran de taille fixe (animations déjà figées par <see cref="AnimationPolicy.Capture"/>).</summary>
    public void Prepare(int width, int height)
    {
        _window.WindowStyle = WindowStyle.None;
        _window.ResizeMode = ResizeMode.NoResize;
        _window.ShowInTaskbar = false;
        _window.ShowActivated = false;
        _window.WindowStartupLocation = WindowStartupLocation.Manual;
        _window.Left = -32000;
        _window.Top = -32000;
        _window.Width = width;
        _window.Height = height;
        if (!_window.IsVisible) _window.Show();
    }

    public async Task CaptureAsync(string pageKey, string file, int width, int height, string? scenario = null)
    {
        Prepare(width, height);
        _window.Width = width;
        _window.Height = height;
        _navigation.Navigate(pageKey);
        await SettleAsync();
        if (scenario != null)
        {
            ScenarioActions.Apply(scenario, _window);
            await SettleAsync();
        }

        var root = (FrameworkElement)_window.Content;
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();

        var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        using (var fs = File.Create(file)) encoder.Save(fs);
    }

    /// <summary>Laisse la mise en page, les liaisons et les animations d'entrée se stabiliser.</summary>
    private static async Task SettleAsync()
    {
        for (int i = 0; i < 4; i++)
        {
            await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            await Task.Delay(120);
        }
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }
}
