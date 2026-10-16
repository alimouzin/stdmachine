using System.ComponentModel;
using System.Windows.Threading;

namespace StdMachine.Hmi.Infrastructure;

/// <summary>
/// Horloge unique de clignotement (docs/07 § 6) : période = paramètre de la balise (800 ms par défaut),
/// tout ce qui clignote est en phase. Jamais plus de 3 clignotements par seconde (docs/08 § 7.9).
/// </summary>
public sealed class BlinkClock : INotifyPropertyChanged
{
    public static BlinkClock Instance { get; } = new BlinkClock();

    private readonly DispatcherTimer _timer;
    private bool _isOn = true;
    private int _periodMs = 800;

    private BlinkClock()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(_periodMs / 2.0) };
        _timer.Tick += (_, _) => { IsOn = !_isOn; };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Phase allumée (vrai) ou éteinte (faux).</summary>
    public bool IsOn
    {
        get => _isOn;
        private set
        {
            if (_isOn == value) return;
            _isOn = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsOn)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Opacity)));
        }
    }

    /// <summary>1 en phase allumée, 0,25 en phase éteinte : pour lier directement l'opacité d'un élément qui clignote.</summary>
    public double Opacity => _isOn ? 1.0 : 0.25;

    /// <summary>Période complète du clignotement, en ms (au moins 334 ms : 3 Hz au plus).</summary>
    public int PeriodMs
    {
        get => _periodMs;
        set
        {
            _periodMs = Math.Max(334, value);
            _timer.Interval = TimeSpan.FromMilliseconds(_periodMs / 2.0);
        }
    }

    public void Start()
    {
        if (AnimationPolicy.Capture) { IsOn = true; return; }
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
        IsOn = true;
    }
}
