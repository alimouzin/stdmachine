using System.Windows;

namespace StdMachine.Hmi.DragDrop;

/// <summary>Propriétés attachées côté source : <c>dd:Drag.Source="{Binding Payload}"</c>, <c>dd:Drag.Handle="True"</c>.</summary>
public static class Drag
{
    /// <summary>Charge utile de l'élément glissable ; non nulle = l'élément est une source.</summary>
    public static readonly DependencyProperty SourceProperty = DependencyProperty.RegisterAttached(
        "Source", typeof(object), typeof(Drag), new PropertyMetadata(null, OnSourceChanged));
    public static object? GetSource(DependencyObject o) => o.GetValue(SourceProperty);
    public static void SetSource(DependencyObject o, object? v) => o.SetValue(SourceProperty, v);

    /// <summary>Élément qui démarre le glisser dès 8 px de déplacement (sinon : appui long de 300 ms sur la source).</summary>
    public static readonly DependencyProperty HandleProperty = DependencyProperty.RegisterAttached(
        "Handle", typeof(bool), typeof(Drag), new PropertyMetadata(false));
    public static bool GetHandle(DependencyObject o) => (bool)o.GetValue(HandleProperty);
    public static void SetHandle(DependencyObject o, bool v) => o.SetValue(HandleProperty, v);

    /// <summary>Vrai pendant que l'élément est glissé (pour l'atténuer par un style).</summary>
    public static readonly DependencyProperty IsDraggedProperty = DependencyProperty.RegisterAttached(
        "IsDragged", typeof(bool), typeof(Drag), new PropertyMetadata(false));
    public static bool GetIsDragged(DependencyObject o) => (bool)o.GetValue(IsDraggedProperty);
    public static void SetIsDragged(DependencyObject o, bool v) => o.SetValue(IsDraggedProperty, v);

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe) return;
        if (e.NewValue != null && e.OldValue == null) DragDropService.Instance.Attach(fe);
        else if (e.NewValue == null && e.OldValue != null) DragDropService.Instance.Detach(fe);
    }
}

/// <summary>Propriété attachée côté cible : <c>dd:Drop.Target="{Binding DropHandler}"</c>.</summary>
public static class Drop
{
    public static readonly DependencyProperty TargetProperty = DependencyProperty.RegisterAttached(
        "Target", typeof(IDropTarget), typeof(Drop), new PropertyMetadata(null));
    public static IDropTarget? GetTarget(DependencyObject o) => (IDropTarget?)o.GetValue(TargetProperty);
    public static void SetTarget(DependencyObject o, IDropTarget? v) => o.SetValue(TargetProperty, v);
}
