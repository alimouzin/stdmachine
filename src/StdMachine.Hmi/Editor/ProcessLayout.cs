using StdMachine.Core;

namespace StdMachine.Hmi.Editor;

public enum ProcessLevel
{
    Station,
    Machine,
}

/// <summary>Une étape ou un bloc dans sa suite : sert à calculer où un dépôt s'insère.</summary>
public sealed record NodeRect(Step Step, Sequence Parent, int Index, double X, double Y, double Width, double Height, bool IsBlock);

/// <summary>Zone occupée par une suite d'étapes (corps du process ou branche d'un bloc).</summary>
public sealed record SeqRegion(Sequence Seq, Block? Owner, double X, double Y, double Width, double Height, PlaceholderModel? Placeholder);

public sealed class ProcessLayout
{
    public List<StepCardModel> Cards { get; } = new List<StepCardModel>();
    public List<BlockFrameModel> Blocks { get; } = new List<BlockFrameModel>();
    public List<PlaceholderModel> Placeholders { get; } = new List<PlaceholderModel>();
    public List<ConnectorModel> Connectors { get; } = new List<ConnectorModel>();
    public List<LaneModel> Lanes { get; } = new List<LaneModel>();
    public List<BranchLabelModel> BranchLabels { get; } = new List<BranchLabelModel>();
    public List<NodeRect> Nodes { get; } = new List<NodeRect>();
    public List<SeqRegion> Regions { get; } = new List<SeqRegion>();
    public EndMarkerModel? End { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

public sealed class ProcessLayoutOptions
{
    public ProcessLevel Level { get; set; }
    public StationConfig? Station { get; set; }
    public MachineProject Project { get; set; } = null!;
    public double ViewportWidth { get; set; } = 1100;
    public Func<Guid, (string? Error, string? Warning)> Validation { get; set; } = _ => (null, null);
    public string EndText { get; set; } = "Fin d'opération";
}

/// <summary>
/// Calcule la mise en page de l'éditeur de process (docs/08 § 6.9, maquette « Process ») : un couloir par objet de premier niveau
/// (niveau station) ou par fonction (niveau machine), cartes empilées dans le couloir de leur objet, blocs encadrés dont les
/// branches se placent côte à côte (alignées sur les couloirs quand elles n'en partagent aucun, sinon réparties à parts égales).
/// Pure : le même process donne la même mise en page.
/// </summary>
public sealed class ProcessLayoutEngine
{
    public const double CardWidth = 236;
    public const double LaneMinWidth = 262;
    private double _maxRight;
    public const double GapV = 34;
    public const double BlockHeader = 46;
    public const double BlockFooter = 16;
    public const double PlaceholderHeight = 64;

    private sealed class Exit
    {
        public Exit(double x, double y, Guid? stepId) { X = x; Y = y; StepId = stepId; }
        public double X { get; }
        public double Y { get; }
        public Guid? StepId { get; }
    }

    private ProcessLayout _l = null!;
    private ProcessLayoutOptions _o = null!;
    private StepPresenter _presenter = null!;
    private readonly Dictionary<string, int> _laneIndex = new Dictionary<string, int>();
    private readonly Dictionary<Guid, Guid> _topObject = new Dictionary<Guid, Guid>();
    private int _number;

    public ProcessLayout Build(ProcessDefinition process, ProcessLayoutOptions options)
    {
        _l = new ProcessLayout();
        _o = options;
        _presenter = new StepPresenter(options.Project, options.Station, process);
        _number = 0;
        _laneIndex.Clear();
        _topObject.Clear();
        BuildLanes(process);

        // Largeur : chaque couloir au moins LaneMinWidth, réparti sur la largeur disponible
        int n = Math.Max(1, _l.Lanes.Count);
        double laneW = Math.Max(LaneMinWidth, options.ViewportWidth / n);
        var lanes = new List<LaneModel>();
        for (int i = 0; i < _l.Lanes.Count; i++) lanes.Add(_l.Lanes[i] with { X = i * laneW, Width = laneW });
        _l.Lanes.Clear();
        _l.Lanes.AddRange(lanes);
        _l.Width = n * laneW;

        Exit? prev = null;
        double y = 62;
        var steps = process.Body;
        var trailingEnd = steps.Steps.Count > 0 && steps.Steps[steps.Steps.Count - 1] is EndStep ? steps.Steps[steps.Steps.Count - 1] : null;
        _maxRight = 0;
        double bottom = PlaceSequence(steps, 0, _l.Width, y, true, ref prev, trailingEnd, true);
        if (_maxRight > _l.Width) _l.Width = _maxRight;
        // Marqueur de fin
        double ey = bottom + 6;
        if (prev != null)
        {
            var endConn = Connect(prev, new Exit(_l.Width / 2, ey, null));
            if (endConn != null) _l.Connectors.Add(endConn);
        }
        _l.End = new EndMarkerModel(_l.Width / 2 - 170, ey, 340, _o.EndText);
        _l.Height = ey + 90;
        return _l;
    }

    // ---------------------------------------------------------------- couloirs

    private void BuildLanes(ProcessDefinition process)
    {
        if (_o.Level == ProcessLevel.Machine)
        {
            var used = new List<Guid>();
            foreach (var s in process.AllSteps().OfType<RouteStep>()) if (!used.Contains(s.GroupId)) used.Add(s.GroupId);
            foreach (var gid in used)
            {
                var g = _o.Project.Group(gid);
                _laneIndex[gid.ToString()] = _l.Lanes.Count;
                _l.Lanes.Add(new LaneModel(gid.ToString(), g?.Name ?? "?", "build", 0, 0, true));
            }
            if (_l.Lanes.Count == 0) _l.Lanes.Add(new LaneModel("logic", "Logique", "alt_route", 0, 0, true));
            return;
        }

        var station = _o.Station;
        if (station != null)
            foreach (var root in station.Objects)
                foreach (var o in root.SelfAndDescendants())
                    _topObject[o.Id] = root.Id;

        var usedRoots = new List<Guid>();
        bool logic = false;
        foreach (var s in process.AllSteps())
        {
            Guid? obj = s is ActionStep a ? a.ObjectId : s is FunctionStep f ? f.ModuleObjectId : (Guid?)null;
            if (obj.HasValue && _topObject.TryGetValue(obj.Value, out var root)) { if (!usedRoots.Contains(root)) usedRoots.Add(root); }
            else if (!(s is Block) && !(s is EndStep)) logic = true;
        }
        if (station != null)
            foreach (var root in station.Objects)
                if (usedRoots.Contains(root.Id))
                {
                    _laneIndex[root.Id.ToString()] = _l.Lanes.Count;
                    var def = StdMachine.Simulation.Demo.DemoCatalog.Get(root.TypeCode);
                    _l.Lanes.Add(new LaneModel(root.Id.ToString(), root.Name, def?.Icon ?? "folder", 0, 0, true));
                }
        _laneIndex["logic"] = _l.Lanes.Count;
        _l.Lanes.Add(new LaneModel("logic", "Logique", "alt_route", 0, 0, logic));
    }

    private int LaneOf(Step s)
    {
        if (_o.Level == ProcessLevel.Machine)
        {
            if (s is RouteStep r && _laneIndex.TryGetValue(r.GroupId.ToString(), out var gi)) return gi;
            return 0;
        }
        Guid? obj = s is ActionStep a ? a.ObjectId : s is FunctionStep f ? f.ModuleObjectId : (Guid?)null;
        if (obj.HasValue && _topObject.TryGetValue(obj.Value, out var root) && _laneIndex.TryGetValue(root.ToString(), out var i)) return i;
        return _laneIndex["logic"];
    }

    /// <summary>Couloirs (indices) utilisés par une suite d'étapes, blocs imbriqués compris.</summary>
    private SortedSet<int> LanesOf(Sequence seq)
    {
        var set = new SortedSet<int>();
        foreach (var s in ProcessTree.Flatten(seq))
            if (!(s is Block)) set.Add(LaneOf(s));
        return set;
    }

    private double LaneLeft(int i) => _l.Lanes[i].X;
    private double LaneRight(int i) => _l.Lanes[i].X + _l.Lanes[i].Width;
    private double LaneCenter(int i) => _l.Lanes[i].X + _l.Lanes[i].Width / 2;

    // ---------------------------------------------------------------- suites

    private double PlaceSequence(Sequence seq, double x0, double x1, double y, bool aligned, ref Exit? prev, Step? skip, bool isRoot, Block? owner = null)
    {
        double regionTop = y;
        int index = 0;
        int visible = 0;
        for (int i = 0; i < seq.Steps.Count; i++)
        {
            var step = seq.Steps[i];
            index = i;
            if (ReferenceEquals(step, skip)) continue;
            visible++;
            if (step is Block b)
            {
                y = PlaceBlock(b, seq, index, x0, x1, y, aligned, ref prev);
                continue;
            }
            double cw = Math.Min(CardWidth, x1 - x0 - 12);
            double cx = aligned ? LaneCenter(LaneOf(step)) : (x0 + x1) / 2;
            var card = MakeCard(step, cx - cw / 2, y, cw);
            _l.Cards.Add(card);
            _l.Nodes.Add(new NodeRect(step, seq, index, card.X, card.Y, card.Width, card.Height, false));
            if (prev != null)
            {
                var conn = Connect(prev, new Exit(cx, y, step.Id));
                if (conn != null) _l.Connectors.Add(conn);
            }
            prev = new Exit(cx, y + card.Height, step.Id);
            y += card.Height + GapV;
        }
        PlaceholderModel? ph = null;
        if (visible == 0)
        {
            double pw = Math.Min(CardWidth, x1 - x0 - 12);
            double cx = (x0 + x1) / 2;
            ph = new PlaceholderModel(seq, cx - pw / 2, y, pw, PlaceholderHeight);
            _l.Placeholders.Add(ph);
            if (prev != null)
            {
                var conn = Connect(prev, new Exit(cx, y, null));
                if (conn != null) _l.Connectors.Add(conn);
            }
            prev = new Exit(cx, y + PlaceholderHeight, null);
            y += PlaceholderHeight + GapV;
        }
        double bottom = y - GapV;
        _l.Regions.Add(new SeqRegion(seq, owner, x0, regionTop, x1 - x0, Math.Max(24, bottom - regionTop), ph));
        return bottom;
    }

    private double PlaceBlock(Block b, Sequence parent, int index, double x0, double x1, double y, bool aligned, ref Exit? prev)
    {
        var branches = b.Sequences;
        BlockKind kind = b is ParallelBlock ? BlockKind.Parallel : b is IfBlock ? BlockKind.If : BlockKind.Repeat;
        y += 14; // l'étiquette ronde déborde de 12 px au-dessus du cadre
        double frameTop = y;

        // Plages horizontales des branches
        var ranges = new double[branches.Count][];
        bool childAligned = false;
        double fx0 = x0 + 4, fx1 = x1 - 4;
        if (aligned)
        {
            var sets = branches.Select(LanesOf).ToList();
            var all = new SortedSet<int>();
            foreach (var s in sets) foreach (var i in s) all.Add(i);
            if (all.Count == 0) all.Add(0);
            fx0 = LaneLeft(all.Min) + 6;
            fx1 = LaneRight(all.Max) - 6;
            bool disjoint = branches.Count == 1;
            if (!disjoint)
            {
                disjoint = sets.All(s => s.Count > 0);
                var seen = new HashSet<int>();
                foreach (var s in sets) foreach (var i in s) if (!seen.Add(i)) disjoint = false;
            }
            if (disjoint && branches.Count > 1)
            {
                childAligned = true;
                for (int k = 0; k < branches.Count; k++)
                    ranges[k] = new[] { LaneLeft(sets[k].Min), LaneRight(sets[k].Max) };
            }
            else if (branches.Count == 1) { childAligned = true; ranges[0] = new[] { LaneLeft(all.Min), LaneRight(all.Max) }; }
        }
        if (!childAligned)
        {
            // Un bloc dont les branches se partagent un seul couloir déborde du couloir : chaque branche garde la largeur d'une carte.
            double need = branches.Count * (CardWidth + 20);
            if (fx1 - fx0 < need)
            {
                double mid = (fx0 + fx1) / 2;
                fx0 = mid - need / 2; fx1 = mid + need / 2;
                if (fx0 < 4) { fx1 += 4 - fx0; fx0 = 4; }
            }
            _maxRight = Math.Max(_maxRight, fx1 + 8);
            double w = (fx1 - fx0) / branches.Count;
            for (int k = 0; k < branches.Count; k++) ranges[k] = new[] { fx0 + k * w, fx0 + (k + 1) * w };
        }

        double frameCx = (fx0 + fx1) / 2;
        var enter = prev;
        if (enter != null)
        {
            var c = Connect(enter, new Exit(frameCx, frameTop, b.Id));
            if (c != null) _l.Connectors.Add(c);
        }

        double maxBottom = frameTop + BlockHeader;
        var exits = new List<Exit>();
        for (int k = 0; k < branches.Count; k++)
        {
            var seq = branches[k];
            double bx0 = ranges[k][0], bx1 = ranges[k][1];
            Exit? bprev = new Exit(frameCx, frameTop + 12, b.Id);
            double top = frameTop + BlockHeader;
            if (b is IfBlock ib)
            {
                string text = k == 0 ? "Si " + _presenter.ConditionText(ib.Condition) : (ib.ElseLabel ?? "Sinon");
                _l.BranchLabels.Add(new BranchLabelModel(bx0 + 10, frameTop + 22, bx1 - bx0 - 20, text, k == 1));
            }
            double bottom = PlaceSequence(seq, bx0, bx1, top, childAligned, ref bprev, null, false, b);
            maxBottom = Math.Max(maxBottom, bottom);
            if (bprev != null) exits.Add(bprev);
        }
        double frameBottom = maxBottom + BlockFooter;
        foreach (var e in exits)
        {
            var c = Connect(e, new Exit(frameCx, frameBottom, null));
            if (c != null) _l.Connectors.Add(c);
        }

        var frame = new BlockFrameModel(b, kind)
        {
            X = fx0 - 4, Y = frameTop, Width = fx1 - fx0 + 8, Height = frameBottom - frameTop,
            Label = kind == BlockKind.Parallel ? "En parallèle" : kind == BlockKind.If ? "Si / Sinon" : "Répéter",
            LabelSub = kind == BlockKind.Parallel ? branches.Count + " branches" : kind == BlockKind.Repeat ? ((RepeatBlock)b).Count + " fois" : string.Empty,
            Icon = kind == BlockKind.Parallel ? "call_split" : kind == BlockKind.If ? "alt_route" : "repeat",
            CanAddBranch = kind == BlockKind.Parallel && branches.Count < 4,
            HasError = _o.Validation(b.Id).Error != null,
        };
        _l.Blocks.Add(frame);
        _l.Nodes.Add(new NodeRect(b, parent, index, frame.X, frame.Y, frame.Width, frame.Height, true));
        prev = new Exit(frameCx, frameBottom, b.Id);
        return frameBottom + GapV;
    }

    // ---------------------------------------------------------------- cartes et connecteurs

    private StepCardModel MakeCard(Step step, double x, double y, double w)
    {
        var text = _presenter.Present(step);
        var (err, warn) = _o.Validation(step.Id);
        var card = new StepCardModel(step)
        {
            Number = ++_number,
            Title = text.Title,
            Subtitle = text.Subtitle ?? string.Empty,
            Icon = text.Icon,
            Params = text.Params,
            DurationText = text.DurationSeconds > 0 ? "≈ " + StdMachine.Core.DisplayFormat.Duration(text.DurationSeconds) : "—",
            X = x, Y = y, Width = w,
            ErrorMessage = err, WarnMessage = warn,
            IsLogic = !(step is ActionStep || step is FunctionStep || step is RouteStep),
        };
        card.Height = Math.Max(112, 92 + 20 * text.Params.Count) + (err != null || warn != null ? 44 : 0);
        return card;
    }

    /// <summary>Connecteur orthogonal à angles arrondis : descend, traverse à mi-hauteur, redescend.</summary>
    private static ConnectorModel? Connect(Exit a, Exit b)
    {
        if (b.Y <= a.Y - 1) return null;
        var pts = new List<(double X, double Y)>();
        pts.Add((a.X, a.Y));
        if (Math.Abs(a.X - b.X) < 0.5) pts.Add((b.X, b.Y));
        else
        {
            double my = a.Y + Math.Max(10, (b.Y - a.Y) / 2);
            pts.Add((a.X, my));
            pts.Add((b.X, my));
            pts.Add((b.X, b.Y));
        }
        return new ConnectorModel(pts, a.StepId, b.StepId);
    }
}
