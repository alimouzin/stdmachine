using System.Windows;

namespace StdMachine.Hmi.DragDrop;

public enum DropKind
{
    None,
    /// <summary>Trait bleu d'insertion avant ou après une carte.</summary>
    Line,
    /// <summary>Zone en surbrillance (branche vide, emplacement, liste).</summary>
    Zone,
}

/// <summary>
/// Résultat de <see cref="IDropTarget.CanAccept"/> : ce que la cible accepterait, et comment l'afficher.
/// <see cref="Bounds"/> est exprimé dans le repère de l'élément cible (vide = toute la cible).
/// </summary>
public sealed class DropPreview
{
    private DropPreview(bool accepted, DropKind kind, Rect bounds, string? reason, object? tag)
    {
        Accepted = accepted; Kind = kind; Bounds = bounds; Reason = reason; Tag = tag;
    }

    public bool Accepted { get; }
    public DropKind Kind { get; }
    public Rect Bounds { get; }
    /// <summary>Raison du refus, affichée sous le doigt.</summary>
    public string? Reason { get; }
    /// <summary>Donnée libre transmise à <see cref="IDropTarget.Drop"/> (par exemple l'index d'insertion).</summary>
    public object? Tag { get; }

    public static DropPreview Line(Rect bounds, object? tag = null) => new DropPreview(true, DropKind.Line, bounds, null, tag);
    public static DropPreview Zone(Rect bounds, object? tag = null) => new DropPreview(true, DropKind.Zone, bounds, null, tag);
    public static DropPreview Zone(object? tag = null) => new DropPreview(true, DropKind.Zone, Rect.Empty, null, tag);
    public static DropPreview Reject(string reason, Rect? bounds = null) => new DropPreview(false, DropKind.Zone, bounds ?? Rect.Empty, reason, null);
}

/// <summary>Cible de dépôt (docs/07 § 8.3). Chaque dépôt doit être une commande annulable côté appelant.</summary>
public interface IDropTarget
{
    /// <summary>Appelée à chaque déplacement au-dessus de la cible ; <paramref name="point"/> est dans le repère de l'élément.</summary>
    DropPreview CanAccept(object payload, Point point);

    void Drop(object payload, DropPreview preview);
}
