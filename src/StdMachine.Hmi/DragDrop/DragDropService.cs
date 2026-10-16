using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace StdMachine.Hmi.DragDrop;

/// <summary>
/// Service de glisser-déposer tactile (docs/07 § 8.3). Même code pour souris, stylet et toucher (capture du pointeur) ;
/// démarrage depuis une poignée dès 8 px, ou après un appui long de 300 ms sans bouger sur le reste d'une source
/// (pour ne pas voler le défilement) ; fantôme, aperçu de dépôt, défilement automatique à 48 px du bord, annulation
/// par un relâchement hors cible, un second doigt ou Échap.
/// </summary>
public sealed class DragDropService
{
    public static DragDropService Instance { get; } = new DragDropService();

    private const double StartDistance = 8;
    private const int LongPressMs = 300;
    private const double EdgeZone = 48;
    private const double MaxScrollSpeed = 1100; // px/s

    private sealed class Arm
    {
        public FrameworkElement Element = null!;
        public Window Window = null!;
        public UIElement Root = null!;
        public Point Start;
        public bool ViaHandle;
        public TouchDevice? Touch;
        public DispatcherTimer? Timer;
    }

    private sealed class Session
    {
        public FrameworkElement Source = null!;
        public Window Window = null!;
        public UIElement Root = null!;
        public object Payload = null!;
        public TouchDevice? Touch;
        public AdornerLayer Layer = null!;
        public GhostAdorner Ghost = null!;
        public DropIndicatorAdorner Indicator = null!;
        public Vector GrabOffset;
        public Point Pointer;
        public FrameworkElement? TargetElement;
        public IDropTarget? Target;
        public DropPreview? Preview;
        public DispatcherTimer ScrollTimer = null!;
        public List<ScrollViewer> Scrollers = new List<ScrollViewer>();
        public DateTime LastTick;
    }

    private Arm? _arm;
    private Session? _session;

    /// <summary>Vrai pendant un glisser-déposer.</summary>
    public bool IsDragging => _session != null;

    public event EventHandler? DragStarted;
    public event EventHandler? DragEnded;

    // ------------------------------------------------------------------ branchement

    internal void Attach(FrameworkElement element)
    {
        element.PreviewMouseLeftButtonDown += OnMouseDown;
        element.PreviewTouchDown += OnTouchDown;
    }

    internal void Detach(FrameworkElement element)
    {
        element.PreviewMouseLeftButtonDown -= OnMouseDown;
        element.PreviewTouchDown -= OnTouchDown;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.StylusDevice != null) return; // toucher promu en souris : traité par OnTouchDown
        var el = (FrameworkElement)sender;
        if (_arm != null || _session != null || !CanArm(el, e.OriginalSource as DependencyObject, out var viaHandle)) return;
        var window = Window.GetWindow(el);
        if (window?.Content is not UIElement root) return;
        ArmAt(el, window, root, e.GetPosition(root), viaHandle, null);
    }

    private void OnTouchDown(object? sender, TouchEventArgs e)
    {
        var el = (FrameworkElement)sender!;
        var window = Window.GetWindow(el);
        if (window?.Content is not UIElement root) return;
        if (_session != null && _session.Touch != null && e.TouchDevice != _session.Touch) { Cancel(); return; }
        if (_arm != null || _session != null || !CanArm(el, e.OriginalSource as DependencyObject, out var viaHandle)) return;
        ArmAt(el, window, root, e.GetTouchPoint(root).Position, viaHandle, e.TouchDevice);
        // Depuis une poignée, on prend le toucher tout de suite : le ScrollViewer ne doit pas démarrer son panoramique.
        // Le flux continue alors sous forme de souris promue (StylusDevice non nul), traitée par OnWindowMouseMove.
        if (viaHandle) e.Handled = true;
    }

    /// <summary>Un élément interne qui est lui-même une source est prioritaire sur ses parents.</summary>
    private static bool CanArm(FrameworkElement el, DependencyObject? original, out bool viaHandle)
    {
        viaHandle = false;
        for (var d = original; d != null && d != el; d = Parent(d))
        {
            if (Drag.GetSource(d) != null) return false;
            if (Drag.GetHandle(d)) viaHandle = true;
        }
        if (Drag.GetHandle(el)) viaHandle = true;
        return true;
    }

    private void ArmAt(FrameworkElement el, Window window, UIElement root, Point start, bool viaHandle, TouchDevice? touch)
    {
        var arm = new Arm { Element = el, Window = window, Root = root, Start = start, ViaHandle = viaHandle, Touch = touch };
        _arm = arm;
        if (!viaHandle)
        {
            arm.Timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(LongPressMs) };
            arm.Timer.Tick += (_, _) =>
            {
                arm.Timer!.Stop();
                if (_arm == arm) Begin(arm, arm.Start);
            };
            arm.Timer.Start();
        }
        window.AddHandler(UIElement.PreviewMouseMoveEvent, new MouseEventHandler(OnWindowMouseMove), true);
        window.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnWindowMouseUp), true);
        window.AddHandler(UIElement.PreviewTouchMoveEvent, new EventHandler<TouchEventArgs>(OnWindowTouchMove), true);
        window.AddHandler(UIElement.PreviewTouchUpEvent, new EventHandler<TouchEventArgs>(OnWindowTouchUp), true);
        window.AddHandler(UIElement.PreviewTouchDownEvent, new EventHandler<TouchEventArgs>(OnWindowTouchDown), true);
        window.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(OnWindowKeyDown), true);
    }

    private void Disarm()
    {
        var arm = _arm;
        if (arm == null) return;
        _arm = null;
        arm.Timer?.Stop();
        var w = arm.Window;
        w.RemoveHandler(UIElement.PreviewMouseMoveEvent, new MouseEventHandler(OnWindowMouseMove));
        w.RemoveHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnWindowMouseUp));
        w.RemoveHandler(UIElement.PreviewTouchMoveEvent, new EventHandler<TouchEventArgs>(OnWindowTouchMove));
        w.RemoveHandler(UIElement.PreviewTouchUpEvent, new EventHandler<TouchEventArgs>(OnWindowTouchUp));
        w.RemoveHandler(UIElement.PreviewTouchDownEvent, new EventHandler<TouchEventArgs>(OnWindowTouchDown));
        w.RemoveHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(OnWindowKeyDown));
    }

    // ------------------------------------------------------------------ événements de fenêtre

    private void OnWindowMouseMove(object sender, MouseEventArgs e)
    {
        // Un toucher promu en souris porte un StylusDevice : on l'attribue au toucher actif (même flux de pointeur).
        Moved(e.GetPosition(Root()), e.StylusDevice != null ? ActiveTouch() : null);
    }

    private void OnWindowMouseUp(object sender, MouseButtonEventArgs e)
    {
        Released(e.StylusDevice != null ? ActiveTouch() : null);
    }

    private void OnWindowTouchMove(object? sender, TouchEventArgs e) => Moved(e.GetTouchPoint(Root()).Position, e.TouchDevice);
    private void OnWindowTouchUp(object? sender, TouchEventArgs e) => Released(e.TouchDevice);

    private void OnWindowTouchDown(object? sender, TouchEventArgs e)
    {
        // Un second doigt annule.
        var touch = _session?.Touch ?? _arm?.Touch;
        if (touch != null && e.TouchDevice != touch) { if (_session != null) Cancel(); else Disarm(); }
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { if (_session != null) Cancel(); else Disarm(); }
    }

    private TouchDevice? ActiveTouch() => _session?.Touch ?? _arm?.Touch;

    private UIElement Root() => (_session?.Root ?? _arm?.Root)!;

    private void Moved(Point pos, TouchDevice? device)
    {
        if (_session != null)
        {
            if (_session.Touch == device) { _session.Pointer = pos; Update(); }
            return;
        }
        var arm = _arm;
        if (arm == null || arm.Touch != device) return;
        if ((pos - arm.Start).Length < StartDistance) return;
        if (arm.ViaHandle) Begin(arm, pos);
        else Disarm(); // mouvement avant l'appui long : c'est un défilement, on s'efface
    }

    private void Released(TouchDevice? device)
    {
        if (_session != null)
        {
            if (_session.Touch == device) End(drop: true);
            return;
        }
        if (_arm != null && _arm.Touch == device) Disarm();
    }

    // ------------------------------------------------------------------ session

    private void Begin(Arm arm, Point pos)
    {
        var payload = Drag.GetSource(arm.Element);
        var layer = AdornerLayer.GetAdornerLayer(arm.Root);
        if (payload == null || layer == null) { Disarm(); return; }
        Disarm();

        var el = arm.Element;
        var size = new Size(el.ActualWidth, el.ActualHeight);
        var topLeft = el.TranslatePoint(new Point(0, 0), arm.Root);
        var s = new Session
        {
            Source = el, Window = arm.Window, Root = arm.Root, Payload = payload, Touch = arm.Touch,
            Layer = layer, Pointer = pos, GrabOffset = arm.Start - topLeft,
        };
        s.Ghost = new GhostAdorner(arm.Root, el, size, GhostTopLeft(s));
        s.Indicator = new DropIndicatorAdorner(arm.Root);
        layer.Add(s.Indicator);
        layer.Add(s.Ghost);
        _session = s;

        // Capture du pointeur : on reçoit mouvement et relâchement même hors de l'élément.
        if (arm.Touch != null)
        {
            // Depuis une poignée, l'élément détient déjà le toucher : on le garde (le transférer interrompt le flux).
            arm.Root.CaptureTouch(arm.Touch);
        }
        else Mouse.Capture(arm.Root, CaptureMode.SubTree);

        CollectScrollers(arm.Root, s.Scrollers);
        Drag.SetIsDragged(el, true);
        s.LastTick = DateTime.UtcNow;
        s.ScrollTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        s.ScrollTimer.Tick += (_, _) => AutoScroll(s);
        s.ScrollTimer.Start();

        // Les événements fenêtre restent branchés : on les remet pour la durée de la session.
        s.Window.AddHandler(UIElement.PreviewMouseMoveEvent, new MouseEventHandler(OnWindowMouseMove), true);
        s.Window.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnWindowMouseUp), true);
        s.Window.AddHandler(UIElement.PreviewTouchMoveEvent, new EventHandler<TouchEventArgs>(OnWindowTouchMove), true);
        s.Window.AddHandler(UIElement.PreviewTouchUpEvent, new EventHandler<TouchEventArgs>(OnWindowTouchUp), true);
        s.Window.AddHandler(UIElement.PreviewTouchDownEvent, new EventHandler<TouchEventArgs>(OnWindowTouchDown), true);
        s.Window.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(OnWindowKeyDown), true);

        DragStarted?.Invoke(this, EventArgs.Empty);
        Update();
    }

    private static Point GhostTopLeft(Session s)
        => new Point(s.Pointer.X - s.GrabOffset.X + 12, s.Pointer.Y - s.GrabOffset.Y + 12);

    private void Update()
    {
        var s = _session;
        if (s == null) return;
        s.Ghost.MoveTo(GhostTopLeft(s));

        FindTarget(s, out var targetEl, out var target);
        s.TargetElement = targetEl;
        s.Target = target;
        s.Preview = null;
        if (target != null && targetEl != null)
        {
            var local = s.Root.TranslatePoint(s.Pointer, targetEl);
            s.Preview = target.CanAccept(s.Payload, local);
        }
        UpdateIndicator(s);
    }

    private void UpdateIndicator(Session s)
    {
        var p = s.Preview;
        if (p == null || s.TargetElement == null || p.Kind == DropKind.None) { s.Indicator.Clear(); return; }
        var bounds = p.Bounds;
        if (bounds.IsEmpty) bounds = new Rect(0, 0, s.TargetElement.ActualWidth, s.TargetElement.ActualHeight);
        var transform = s.TargetElement.TransformToVisual(s.Root);
        var rootBounds = transform.TransformBounds(bounds);
        s.Indicator.Set(rootBounds, p.Kind, p.Accepted, p.Reason, s.Pointer);
    }

    private static void FindTarget(Session s, out FrameworkElement? element, out IDropTarget? target)
    {
        element = null; target = null;
        var hit = VisualTreeHelper.HitTest(s.Root, s.Pointer);
        for (var d = hit?.VisualHit; d != null; d = Parent(d))
        {
            var t = Drop.GetTarget(d);
            if (t != null && d is FrameworkElement fe) { element = fe; target = t; return; }
        }
    }

    private static DependencyObject? Parent(DependencyObject d)
    {
        if (d is Visual || d is System.Windows.Media.Media3D.Visual3D)
        {
            var p = VisualTreeHelper.GetParent(d);
            if (p != null) return p;
        }
        return LogicalTreeHelper.GetParent(d);
    }

    private void AutoScroll(Session s)
    {
        var now = DateTime.UtcNow;
        var dt = Math.Min(0.05, (now - s.LastTick).TotalSeconds);
        s.LastTick = now;

        for (int i = s.Scrollers.Count - 1; i >= 0; i--) // le plus interne d'abord
        {
            var sv = s.Scrollers[i];
            if (!sv.IsVisible || (sv.ScrollableHeight <= 0 && sv.ScrollableWidth <= 0)) continue;
            var p = s.Root.TranslatePoint(s.Pointer, sv);
            if (p.X < -EdgeZone || p.Y < -EdgeZone || p.X > sv.ActualWidth + EdgeZone || p.Y > sv.ActualHeight + EdgeZone) continue;
            double dy = sv.ScrollableHeight > 0 ? Edge(p.Y, sv.ActualHeight) * MaxScrollSpeed * dt : 0;
            double dx = sv.ScrollableWidth > 0 ? Edge(p.X, sv.ActualWidth) * MaxScrollSpeed * dt : 0;
            if (Math.Abs(dx) < 0.01 && Math.Abs(dy) < 0.01) continue;
            if (Math.Abs(dy) >= 0.01) sv.ScrollToVerticalOffset(sv.VerticalOffset + dy);
            if (Math.Abs(dx) >= 0.01) sv.ScrollToHorizontalOffset(sv.HorizontalOffset + dx);
            sv.UpdateLayout();
            Update();
            return;
        }
    }

    private static void CollectScrollers(DependencyObject node, List<ScrollViewer> list)
    {
        int n = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(node, i);
            if (c is ScrollViewer sv) list.Add(sv);
            CollectScrollers(c, list);
        }
    }

    /// <summary>−1 à +1 : proportionnel à la proximité du bord (zone de 48 px).</summary>
    private static double Edge(double pos, double size)
    {
        if (pos < EdgeZone && pos > -EdgeZone) return -(EdgeZone - pos) / EdgeZone;
        if (pos > size - EdgeZone && pos < size + EdgeZone) return (pos - (size - EdgeZone)) / EdgeZone;
        return 0;
    }

    private void Cancel() => End(drop: false);

    private void End(bool drop)
    {
        var s = _session;
        if (s == null) return;
        _session = null;
        s.ScrollTimer.Stop();
        var w = s.Window;
        w.RemoveHandler(UIElement.PreviewMouseMoveEvent, new MouseEventHandler(OnWindowMouseMove));
        w.RemoveHandler(UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnWindowMouseUp));
        w.RemoveHandler(UIElement.PreviewTouchMoveEvent, new EventHandler<TouchEventArgs>(OnWindowTouchMove));
        w.RemoveHandler(UIElement.PreviewTouchUpEvent, new EventHandler<TouchEventArgs>(OnWindowTouchUp));
        w.RemoveHandler(UIElement.PreviewTouchDownEvent, new EventHandler<TouchEventArgs>(OnWindowTouchDown));
        w.RemoveHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(OnWindowKeyDown));

        if (s.Touch != null) s.Root.ReleaseTouchCapture(s.Touch);
        else if (Mouse.Captured == s.Root) Mouse.Capture(null);

        s.Indicator.Clear();
        s.Layer.Remove(s.Indicator);
        Drag.SetIsDragged(s.Source, false);

        var accepted = drop && s.Preview != null && s.Preview.Accepted && s.Target != null;
        if (accepted)
        {
            s.Layer.Remove(s.Ghost);
            try { s.Target!.Drop(s.Payload, s.Preview!); }
            finally { DragEnded?.Invoke(this, EventArgs.Empty); }
        }
        else
        {
            var origin = s.Source.TranslatePoint(new Point(0, 0), s.Root);
            s.Ghost.ReturnTo(origin, () => s.Layer.Remove(s.Ghost));
            DragEnded?.Invoke(this, EventArgs.Empty);
        }
    }
}
