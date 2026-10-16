using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace StdMachine.Hmi.DragDrop;

/// <summary>
/// Cible de dépôt pour une liste verticale d'éléments : trait d'insertion avant ou après la carte survolée,
/// zone en surbrillance si la liste est vide, refus expliqué. Sert à l'essai tactile ; l'éditeur de process
/// a ses propres cibles sur le même modèle.
/// </summary>
public sealed class ListInsertTarget : IDropTarget
{
    private readonly ItemsControl _list;
    private readonly IList _items;
    private readonly Func<object, string?> _validate;
    private readonly Func<object, object> _materialize;
    private readonly Action<IList>? _changed;

    /// <param name="validate">Renvoie la raison du refus, ou null si le dépôt est accepté.</param>
    /// <param name="materialize">Transforme la charge utile en élément à insérer (copie depuis une palette).</param>
    public ListInsertTarget(ItemsControl list, IList items, Func<object, string?>? validate = null,
                            Func<object, object>? materialize = null, Action<IList>? changed = null)
    {
        _list = list;
        _items = items;
        _validate = validate ?? (_ => null);
        _materialize = materialize ?? (p => p);
        _changed = changed;
    }

    public DropPreview CanAccept(object payload, Point point)
    {
        var reason = _validate(payload);
        if (reason != null) return DropPreview.Reject(reason);
        if (_items.Count == 0) return DropPreview.Zone(0);

        int index = _items.Count;
        double y = 0;
        double x = 0, w = _list.ActualWidth;
        for (int i = 0; i < _items.Count; i++)
        {
            if (_list.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement c) continue;
            var r = c.TransformToVisual(_list).TransformBounds(new Rect(0, 0, c.ActualWidth, c.ActualHeight));
            x = r.X; w = r.Width;
            if (point.Y < r.Top + r.Height / 2) { index = i; y = r.Top - 2; break; }
            y = r.Bottom + 2;
        }
        return DropPreview.Line(new Rect(x, y - 1.5, w, 3), index);
    }

    public void Drop(object payload, DropPreview preview)
    {
        int index = preview.Tag is int i ? i : _items.Count;
        if (_items.Contains(payload))
        {
            int old = _items.IndexOf(payload);
            _items.RemoveAt(old);
            if (old < index) index--;
            _items.Insert(Math.Min(index, _items.Count), payload);
        }
        else
        {
            _items.Insert(Math.Min(index, _items.Count), _materialize(payload));
        }
        _changed?.Invoke(_items);
    }
}
