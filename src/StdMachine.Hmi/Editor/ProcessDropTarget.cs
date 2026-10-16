using System.Windows;
using StdMachine.Core;
using StdMachine.Hmi.DragDrop;

namespace StdMachine.Hmi.Editor;

/// <summary>
/// Cible de dépôt unique du canevas (docs/08 § 6.9) : le trait bleu d'insertion avant ou après la carte survolée (moitié haute ou basse),
/// la zone vide d'une branche, la fin d'un bloc ou du process ; refus expliqué sous le doigt. Toute la géométrie vient de la mise en page.
/// </summary>
public sealed class ProcessDropTarget : IDropTarget
{
    private readonly Func<ProcessCanvasViewModel> _canvas;
    private readonly Action<object, InsertTarget> _commit;
    private readonly Dictionary<PaletteDrag, Step> _probe = new Dictionary<PaletteDrag, Step>();

    public ProcessDropTarget(Func<ProcessCanvasViewModel> canvas, Action<object, InsertTarget> commit)
    {
        _canvas = canvas;
        _commit = commit;
    }

    public DropPreview CanAccept(object payload, Point p)
    {
        var vm = _canvas();
        var layout = vm.Layout;
        Step? step = payload as Step;
        if (payload is PaletteDrag pd)
        {
            if (!_probe.TryGetValue(pd, out step)) { step = pd.Create(); _probe[pd] = step; }
        }
        if (step == null) return DropPreview.Reject("Élément inconnu.");

        InsertTarget? target = null;
        Rect lineRect = Rect.Empty;
        PlaceholderModel? zone = null;

        // 1. Carte ou en-tête de bloc survolé : avant ou après
        var hit = layout.Nodes
            .Where(n => p.X >= n.X - 4 && p.X <= n.X + n.Width + 4 && p.Y >= n.Y - 6 && p.Y <= n.Y + (n.IsBlock ? ProcessLayoutEngine.BlockHeader : n.Height) + 6)
            .OrderBy(n => n.Width * n.Height)
            .FirstOrDefault();
        if (hit != null && !ReferenceEquals(hit.Step, step))
        {
            double h = hit.IsBlock ? ProcessLayoutEngine.BlockHeader : hit.Height;
            bool before = p.Y < hit.Y + h / 2;
            target = new InsertTarget(hit.Parent, before ? hit.Index : hit.Index + 1);
            double y = before ? hit.Y - ProcessLayoutEngine.GapV / 2 : hit.Y + hit.Height + ProcessLayoutEngine.GapV / 2;
            lineRect = new Rect(hit.X - 6, y - 1.5, hit.Width + 12, 3);
        }

        // 2. Zone vide d'une branche
        if (target == null)
        {
            foreach (var ph in layout.Placeholders)
                if (new Rect(ph.X - 14, ph.Y - 10, ph.Width + 28, ph.Height + 20).Contains(p)) { target = new InsertTarget(ph.Sequence, 0); zone = ph; break; }
        }

        // 3. Intérieur d'une suite : à la fin
        if (target == null)
        {
            var region = layout.Regions.Where(r => new Rect(r.X, r.Y - 6, r.Width, r.Height + 40).Contains(p)).OrderBy(r => r.Width * r.Height).FirstOrDefault();
            if (region == null && layout.Regions.Count > 0 && p.Y > 40) region = layout.Regions.First(r => r.Owner == null);
            if (region != null)
            {
                if (region.Placeholder != null) { target = new InsertTarget(region.Seq, 0); zone = region.Placeholder; }
                else
                {
                    target = new InsertTarget(region.Seq, region.Seq.Steps.Count);
                    double cx = region.X + region.Width / 2, w = Math.Min(260, region.Width - 20);
                    lineRect = new Rect(cx - w / 2, region.Y + region.Height + ProcessLayoutEngine.GapV / 2 - 1.5, w, 3);
                }
            }
        }
        if (target == null) return DropPreview.Reject("Déposez l'étape sur le canevas.", new Rect(0, 0, 0, 0));

        string? reason = ProcessEditing.ValidateInsert(vm.Process.Body, target.Sequence, step, vm.Station);
        if (reason != null)
        {
            var rect = zone != null ? new Rect(zone.X, zone.Y, zone.Width, zone.Height) : lineRect.IsEmpty ? Rect.Empty : new Rect(lineRect.X, lineRect.Y - 20, lineRect.Width, 40);
            return DropPreview.Reject(reason, rect);
        }
        if (zone != null) return DropPreview.Zone(new Rect(zone.X, zone.Y, zone.Width, zone.Height), target);
        return DropPreview.Line(lineRect, target);
    }

    public void Drop(object payload, DropPreview preview)
    {
        if (preview.Tag is InsertTarget t) _commit(payload, t);
        if (payload is PaletteDrag pd) _probe.Remove(pd);
    }
}
