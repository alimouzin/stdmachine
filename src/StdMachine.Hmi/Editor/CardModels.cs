using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using StdMachine.Core;
using StdMachine.Hmi.Theme;

namespace StdMachine.Hmi.Editor;

/// <summary>Ligne de condition en attente, avec sa coche ou sa croix (docs/08 § 7.2).</summary>
public sealed record TermLine(string Text, bool Satisfied);

/// <summary>Carte d'étape de l'éditeur : géométrie, texte et état en direct. Les modèles sont modifiés sur place (pas de reconstruction) quand seul l'état d'exécution change.</summary>
public sealed partial class StepCardModel : ObservableObject
{
    public StepCardModel(Step step) { Step = step; Id = step.Id; }

    public Step Step { get; }
    public Guid Id { get; }

    public int Number { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Icon { get; set; } = "help";
    public IReadOnlyList<ParamLine> Params { get; set; } = new ParamLine[0];
    public string DurationText { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string? ErrorMessage { get; set; }
    public string? WarnMessage { get; set; }
    public bool HasError => ErrorMessage != null;
    public bool HasWarning => WarnMessage != null && ErrorMessage == null;
    public bool IsLogic { get; set; }
    public int LaneIndex { get; set; }

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isHighlighted;

    // ---- exécution en direct (mode « Suivre l'exécution »)
    [ObservableProperty] private StepRunState _state;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _timeText = string.Empty;
    [ObservableProperty] private string? _detail;
    [ObservableProperty] private string? _attemptText;
    [ObservableProperty] private string? _blockResult;
    [ObservableProperty] private bool _showRuntime;
    public ObservableCollection<TermLine> Terms { get; } = new ObservableCollection<TermLine>();

    public void Apply(StepRuntime? r, string estimatedText)
    {
        if (r == null)
        {
            State = StepRunState.Inactive; Progress = 0; TimeText = string.Empty; Detail = null; AttemptText = null; BlockResult = null;
            Terms.Clear();
            return;
        }
        State = r.State;
        Progress = r.Progress;
        TimeText = r.State == StepRunState.Running || r.State == StepRunState.WaitingCondition || r.State == StepRunState.Retry
            ? DisplayFormat.Duration(r.ElapsedSeconds) + " / ≈ " + estimatedText
            : r.State == StepRunState.Done ? DisplayFormat.Duration(r.EstimatedSeconds) : string.Empty;
        Detail = r.Detail;
        AttemptText = r.State == StepRunState.Retry ? "Essai " + r.Attempt + "/" + r.MaxAttempts : null;
        BlockResult = r.BlockResult;
        Terms.Clear();
        if (r.Terms != null) foreach (var t in r.Terms) Terms.Add(new TermLine(t.Text, t.Satisfied));
    }
}

public enum BlockKind
{
    Parallel,
    If,
    Repeat,
}

/// <summary>Cadre d'un bloc (En parallèle, Si / Sinon, Répéter) avec son étiquette ronde (docs/08 § 6.9).</summary>
public sealed partial class BlockFrameModel : ObservableObject
{
    public BlockFrameModel(Block block, BlockKind kind) { Block = block; Kind = kind; Id = block.Id; }

    public Block Block { get; }
    public Guid Id { get; }
    public BlockKind Kind { get; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string Label { get; set; } = string.Empty;
    public string LabelSub { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public bool CanAddBranch { get; set; }
    public bool HasError { get; set; }
    /// <summary>Position de l'étiquette (centrée en haut du cadre).</summary>
    public double LabelX => X + Width / 2;

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private StepRunState _state;
    [ObservableProperty] private string? _result;
    [ObservableProperty] private bool _isDropTarget;
}

/// <summary>Titre de branche d'un Si / Sinon (« Si pièce bonne », « Sinon (rebut) »).</summary>
public sealed record BranchLabelModel(double X, double Y, double Width, string Text, bool IsElse);

/// <summary>Zone vide d'une branche : « Déposez une action ici ».</summary>
public sealed class PlaceholderModel
{
    public PlaceholderModel(Sequence sequence, double x, double y, double width, double height)
    {
        Sequence = sequence; X = x; Y = y; Width = width; Height = height;
    }

    public Sequence Sequence { get; }
    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }
}

public enum ConnectorState
{
    Idle,
    Done,
    Active,
}

/// <summary>Connecteur en angles arrondis entre deux étapes (gris, accent pâle si parcouru, tirets animés si actif).</summary>
public sealed partial class ConnectorModel : ObservableObject
{
    public ConnectorModel(IReadOnlyList<(double X, double Y)> points, Guid? from, Guid? to)
    {
        Points = points; From = from; To = to;
        Data = BuildGeometry(points);
    }

    public IReadOnlyList<(double X, double Y)> Points { get; }
    public Guid? From { get; }
    public Guid? To { get; }
    public System.Windows.Media.Geometry Data { get; }

    [ObservableProperty] private ConnectorState _state;

    /// <summary>Polyligne à angles arrondis (rayon 8).</summary>
    private static System.Windows.Media.Geometry BuildGeometry(IReadOnlyList<(double X, double Y)> pts)
    {
        var g = new System.Windows.Media.StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new System.Windows.Point(pts[0].X, pts[0].Y), false, false);
            const double r = 8;
            for (int i = 1; i < pts.Count - 1; i++)
            {
                var a = pts[i - 1]; var b = pts[i]; var c = pts[i + 1];
                double l1 = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                double l2 = Math.Sqrt((c.X - b.X) * (c.X - b.X) + (c.Y - b.Y) * (c.Y - b.Y));
                double rr = Math.Min(r, Math.Min(l1, l2) / 2);
                if (rr < 0.5) { ctx.LineTo(new System.Windows.Point(b.X, b.Y), true, false); continue; }
                var p1 = new System.Windows.Point(b.X - (b.X - a.X) / l1 * rr, b.Y - (b.Y - a.Y) / l1 * rr);
                var p2 = new System.Windows.Point(b.X + (c.X - b.X) / l2 * rr, b.Y + (c.Y - b.Y) / l2 * rr);
                ctx.LineTo(p1, true, false);
                ctx.QuadraticBezierTo(new System.Windows.Point(b.X, b.Y), p2, true, false);
            }
            ctx.LineTo(new System.Windows.Point(pts[pts.Count - 1].X, pts[pts.Count - 1].Y), true, false);
        }
        g.Freeze();
        return g;
    }
}

public sealed record LaneModel(string Key, string Name, string Icon, double X, double Width, bool Active);

public sealed record EndMarkerModel(double X, double Y, double Width, string Text);
