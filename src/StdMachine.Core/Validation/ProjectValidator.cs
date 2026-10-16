using StdMachine.Core.Catalog;

namespace StdMachine.Core.Validation;

/// <summary>
/// Règles de validation V001 à V113 de docs/07 § 4.2 (et V019 de docs/10). Pure : aucun effet de bord.
/// Non couvertes, faute de modèle : V014 (résultats liés), V106 (durée estimée des mouvements), V114 (réaction qualité configurée),
/// V201 (empreinte du PLC) ; voir le journal des décisions.
/// </summary>
public sealed class ProjectValidator
{
    private readonly MachineProject _p;
    private readonly CatalogDocument _c;
    private readonly List<ValidationIssue> _issues = new List<ValidationIssue>();

    private ProjectValidator(MachineProject project, CatalogDocument catalog)
    {
        _p = project;
        _c = catalog;
    }

    public static IReadOnlyList<ValidationIssue> Validate(MachineProject project, CatalogDocument catalog)
    {
        var v = new ProjectValidator(project, catalog);
        v.Run();
        return v._issues;
    }

    private void Add(string code, Severity sev, string message, ValidationLink link) => _issues.Add(new ValidationIssue(code, sev, message, link));

    private void Error(string code, string message, ValidationLink link) => Add(code, Severity.Error, message, link);

    private void Warn(string code, string message, ValidationLink link) => Add(code, Severity.Warning, message, link);

    private static ValidationLink Studio(StationConfig s, Guid? obj = null, Guid? proc = null, Guid? step = null, string? field = null)
        => new ValidationLink(proc != null ? "ProcessEditor" : "StationConfig", s.Number, obj, proc, step, field);

    private static ValidationLink MachineStudio(string? field = null) => new ValidationLink("MachineConfig", Field: field);

    private void Run()
    {
        Limits();
        Stations();
        Groups();
        foreach (var s in _p.Stations) StationRules(s);
        GeneralProcess();
        Quality();
        Planar();
    }

    // ------------------------------------------------------------------ V007, V013, V018

    private void Limits()
    {
        void Check(int value, int limit, string what, ValidationLink link)
        {
            if (value > limit) Error("V007", "Limite dépassée : " + what + " (" + value + " pour une limite de " + limit + ").", link);
        }
        Check(_p.Stations.Count, PlcLimits.Stations, "stations", MachineStudio());
        Check(_p.Machine.Groups.Count, PlcLimits.Groups, "fonctions", MachineStudio());
        Check(_p.Machine.MachineObjects.SelectMany(o => o.SelfAndDescendants()).Count(), PlcLimits.MachineObjects, "objets de la machine", MachineStudio());
        Check(_p.Machine.Planar.Movers, PlcLimits.Movers, "movers", MachineStudio("movers"));
        Check(_p.Machine.Planar.Slots, PlcLimits.Slots, "emplacements par mover", MachineStudio("slots"));
        Check(_p.Machine.PlanarPositions.Count, PlcLimits.PlanarPositions, "positions planaires", MachineStudio());
        Check(_p.Machine.PartData.Count, PlcLimits.PartData, "données de pièce", MachineStudio());
        foreach (var s in _p.Stations)
        {
            Check(s.AllObjects().Count(), PlcLimits.ObjectsPerStation, "objets de " + s.Name, Studio(s));
            Check(s.Positions.Count, PlcLimits.NamedPositions, "positions nommées de " + s.Name, Studio(s));
            Check(s.Variables.Count, PlcLimits.Variables, "variables de " + s.Name, Studio(s));
            Check(s.Interlocks.Count, PlcLimits.Interlocks, "verrouillages de " + s.Name, Studio(s));
            foreach (var press in s.PressRecipes.GroupBy(r => r.ObjectId))
                Check(press.Count(), PlcLimits.PressRecipes, "recettes de presse de " + s.Name, Studio(s, press.Key));
            foreach (var r in s.PressRecipes) Check(r.Segments.Count, PlcLimits.PressSegments, "segments de la recette " + r.No, Studio(s, r.ObjectId));
            foreach (var proc in s.Processes)
                foreach (var par in proc.AllSteps().OfType<ParallelBlock>())
                    Check(par.Branches.Count, PlcLimits.ForkBranches, "branches d'un bloc En parallèle (" + proc.Name + ")", Studio(s, proc: proc.Id, step: par.Id));
        }
    }

    private void Stations()
    {
        foreach (var dup in _p.Stations.GroupBy(s => s.Number).Where(g => g.Count() > 1))
            Error("V013", "Deux stations ont le n° " + dup.Key + " : " + string.Join(", ", dup.Select(s => s.Name)) + ".", Studio(dup.First()));
        foreach (var s in _p.Stations)
            if (!_p.Machine.PlanarPositions.Any(pp => pp.StationId == s.Id && pp.Kind == PlanarPositionKind.Work))
                Error("V013", "La station " + s.Name + " n'a pas de position de travail planaire.", Studio(s));
    }

    private void Groups()
    {
        foreach (var s in _p.Stations)
        {
            var groups = _p.GroupsOf(s).ToList();
            int pairs = groups.Sum(g => g.Operations.Count);
            if (groups.Count > PlcLimits.GroupsPerStation || pairs > PlcLimits.OpMapPerStation)
                Error("V018", "La station " + s.Name + " appartient à " + groups.Count + " fonctions et " + pairs + " couples (fonction, opération) (limites " + PlcLimits.GroupsPerStation + " et " + PlcLimits.OpMapPerStation + ").", Studio(s));
            if (groups.Count == 0)
                Warn("V111", "La station " + s.Name + " n'appartient à aucune fonction : elle ne sera jamais utilisée en production.", Studio(s));
            else if (groups.Count > 1 && !_p.Machine.PlanarPositions.Any(pp => pp.StationId == s.Id && pp.Kind == PlanarPositionKind.Queue)
                     && _p.Machine.PlanarPositions.Count(pp => pp.Kind == PlanarPositionKind.Buffer) < 2)
                Warn("V112", "La station " + s.Name + " est partagée par plusieurs fonctions sans position de file et il y a moins de 2 positions tampon : blocage possible.", Studio(s));
        }

        foreach (var g in _p.Machine.Groups)
        {
            foreach (var sid in g.StationIds)
            {
                var s = _p.StationById(sid);
                if (s == null) continue;
                for (int i = 0; i < g.Operations.Count; i++)
                    if (!s.Processes.Any(pr => pr.Kind == ProcessKind.Operation && pr.Implements.Any(o => o.GroupId == g.Id && o.OperationNo == i + 1)))
                        Error("V006", "La station " + s.Name + ", membre de la fonction " + g.Name + ", n'implémente pas l'opération " + g.Operations[i].Name + ".", Studio(s));
            }
            if (g.ImputesRejects && !_p.Machine.Groups.Any(o => o.Role == GroupRole.Work && o.Priority < g.Priority))
                Warn("V113", "La fonction " + g.Name + " impute ses rebuts sans fonction amont de rôle Travail.", MachineStudio());
        }

        if (_p.Machine.Planar.Movers > _p.Machine.PlanarPositions.Count(pp => pp.Kind != PlanarPositionKind.Park) - 1)
            Warn("V107", "Il y a " + _p.Machine.Planar.Movers + " movers pour " + (_p.Machine.PlanarPositions.Count(pp => pp.Kind != PlanarPositionKind.Park) - 1) + " places (travail + files + tampons − 1) : blocage possible.", MachineStudio("movers"));
    }

    // ------------------------------------------------------------------ station

    private void StationRules(StationConfig s)
    {
        var all = s.AllObjects().ToList();
        var byId = all.ToDictionary(o => o.Id);
        var usedByProcess = new HashSet<Guid>();
        foreach (var proc in s.Processes) foreach (var o in ProcessTree.ObjectsUsed(proc.Body)) usedByProcess.Add(o);
        foreach (var f in s.CustomFunctions) foreach (var o in ProcessTree.ObjectsUsed(f.Body)) usedByProcess.Add(o);
        // Un module utilisé utilise ses enfants.
        var used = new HashSet<Guid>(usedByProcess);
        foreach (var o in all.Where(o => usedByProcess.Contains(o.Id))) foreach (var d in o.SelfAndDescendants()) used.Add(d.Id);
        // Les verrouillages et conditions lisent aussi des objets : ils comptent comme utilisés.
        foreach (var il in s.Interlocks) used.Add(il.ObjectId);

        foreach (var o in all)
        {
            var type = _c.Type(o.TypeCode);
            var link = Studio(s, o.Id);
            if (o.Simulated) Warn("V101", "L'objet " + o.Name + " (" + s.Name + ") est en simulation.", link);
            if (!used.Contains(o.Id) && !o.SelfAndDescendants().Skip(1).Any(d => used.Contains(d.Id)) && !(IsChildOfUsed(o, all, used)))
                Warn("V109", "L'objet " + o.Name + " (" + s.Name + ") est déclaré mais jamais utilisé.", link);
            if (type != null) ObjectRules(s, o, type, used.Contains(o.Id), link);
            else if (_c.Modules.FirstOrDefault(m => m.Code == o.TypeCode) is { } module) ModuleRules(s, o, module);
        }

        SharedResources(s, all);
        Positions(s, all);
        foreach (var proc in s.Processes) ProcessRules(s, proc, byId, s.Processes);
        foreach (var f in s.CustomFunctions) CheckSteps(s, null, f.Body, byId, s.Processes);
        Operations(s);
        Recipes(s);
    }

    private static bool IsChildOfUsed(ObjectNode o, List<ObjectNode> all, HashSet<Guid> used)
        => all.Any(p => used.Contains(p.Id) && p.Children.Any(c => c.Id == o.Id));

    private void ObjectRules(StationConfig s, ObjectNode o, CatalogType type, bool isUsed, ValidationLink link)
    {
        if (isUsed && !o.Disabled)
        {
            foreach (var sig in type.Signals.Where(x => x.Required && x.VisibleWhen == null))
                if (!o.Signals.Any(b => b.No == sig.No && b.ChannelId != null))
                    Error("V002", "Le signal obligatoire « " + sig.Name + " » de l'objet " + o.Name + " (" + s.Name + ") n'est pas raccordé.", link);
            if (type.Hardware is { Required: true, VisibleWhen: null } && o.DeviceIndex == null)
                Error("V002", "L'objet " + o.Name + " (" + s.Name + ") n'a pas d'élément matériel (" + type.Hardware.Kind + ").", link);
        }
        foreach (var pv in o.Params.Where(x => x.No > 0 && x.Number != null))
        {
            var cp = type.Params.FirstOrDefault(x => x.No == pv.No);
            if (cp == null) { Error("V005", "Le paramètre P" + pv.No + " n'existe pas pour le type " + type.Name + " (" + o.Name + ").", link); continue; }
            double v = pv.Number!.Value;
            bool bad = (cp.Min != null && v < cp.Min) || (cp.Max != null && v > cp.Max)
                       || (cp.Type == "Enum" && cp.Values != null && (v < 0 || v >= cp.Values.Count || v != Math.Floor(v)))
                       || (cp.Type == "Bool" && v != 0 && v != 1) || (cp.Type == "Int" && v != Math.Floor(v));
            if (bad) Error("V005", "Le paramètre « " + cp.Name + " » de " + o.Name + " (" + s.Name + ") est hors bornes ou de mauvais type (" + v + ").", link);
        }
        if (type.Code == "VacuumBlow")
        {
            double p1 = o.Params.FirstOrDefault(x => x.No == 1)?.Number ?? -60, p2 = o.Params.FirstOrDefault(x => x.No == 2)?.Number ?? -52;
            if (p2 - p1 < 10) Warn("V105", "Les seuils de vide de " + o.Name + " (" + s.Name + ") sont trop proches (" + (p2 - p1) + " kPa < 10).", link);
        }
    }

    private void ModuleRules(StationConfig s, ObjectNode o, CatalogModule module)
    {
        foreach (var role in module.Roles.Where(r => r.Required))
            if (!o.Children.Any(c => c.ChildRole == role.Role))
                Error("V010", "Le rôle obligatoire " + role.Role + " du module " + o.Name + " (" + s.Name + ") est vide.", Studio(s, o.Id));
    }

    private void SharedResources(StationConfig s, List<ObjectNode> all)
    {
        // Sorties : une voie d'image E/S commandée par deux objets. Matériel : un même élément pour deux objets.
        var writers = new Dictionary<Guid, ObjectNode>();
        foreach (var o in _p.Machine.MachineObjects.SelectMany(x => x.SelfAndDescendants()).Concat(_p.Stations.SelectMany(st => st.AllObjects())))
        {
            var type = _c.Type(o.TypeCode);
            if (type == null || o.Disabled) continue;
            foreach (var b in o.Signals.Where(b => b.ChannelId != null))
            {
                var kind = type.Signals.FirstOrDefault(x => x.No == b.No)?.Kind;
                if (kind != "DO" && kind != "AO") continue;
                if (writers.TryGetValue(b.ChannelId!.Value, out var other) && other != o && s.AllObjects().Contains(o) && !ReportedPair(o, other))
                    Error("V003", "Les objets " + other.Name + " et " + o.Name + " écrivent la même sortie.", Studio(s, o.Id));
                else writers[b.ChannelId!.Value] = o;
            }
        }
        var hw = new Dictionary<(HardwareKind, int, bool), ObjectNode>();
        foreach (var o in _p.Machine.MachineObjects.SelectMany(x => x.SelfAndDescendants()).Concat(_p.Stations.SelectMany(st => st.AllObjects())))
        {
            var type = _c.Type(o.TypeCode);
            if (type?.Hardware == null || o.DeviceIndex == null || !Enum.TryParse(type.Hardware.Kind, out HardwareKind kind)) continue;
            var key = (kind, o.DeviceIndex.Value, type.Code == "PlanarMover");
            if (hw.TryGetValue(key, out var other) && other != o && s.AllObjects().Contains(o))
                Error("V003", "Les objets " + other.Name + " et " + o.Name + " utilisent le même élément matériel (" + kind + " " + key.Item2 + ").", Studio(s, o.Id));
            else hw[key] = o;
        }
    }

    private readonly HashSet<(Guid, Guid)> _reported = new HashSet<(Guid, Guid)>();

    private bool ReportedPair(ObjectNode a, ObjectNode b) => !_reported.Add((a.Id, b.Id));

    private void Positions(StationConfig s, List<ObjectNode> all)
    {
        var posRefs = new HashSet<Guid>();
        foreach (var proc in s.Processes) foreach (var r in AllValueRefs(proc.Body)) if (r.Kind == ValueKind.NamedPos && r.Ref != null) posRefs.Add(r.Ref.Value);
        foreach (var f in s.CustomFunctions) foreach (var r in AllValueRefs(f.Body)) if (r.Kind == ValueKind.NamedPos && r.Ref != null) posRefs.Add(r.Ref.Value);
        foreach (var pos in s.Positions)
            if (posRefs.Contains(pos.Id) && !pos.Taught)
                Error("V001", "La position « " + pos.Name + " » (" + s.Name + ") est utilisée mais n'est pas apprise.", Studio(s, field: "position:" + pos.Name));

        var robot = all.FirstOrDefault(o => o.TypeCode == "CartesianRobot");
        if (robot == null) return;
        double safeZ = robot.Params.FirstOrDefault(x => x.No == 1)?.Number ?? -5;
        bool zDown = (robot.Params.FirstOrDefault(x => x.No == 4)?.Number ?? 0) == 0;
        foreach (var pos in s.Positions.Where(x => posRefs.Contains(x.Id)))
            if (zDown ? pos.Z > safeZ + 1e-9 : pos.Z < safeZ - 1e-9)
                Warn("V108", "La position « " + pos.Name + " » (" + s.Name + ") est au-dessus de la hauteur de sécurité du robot (" + pos.Z + " mm pour " + safeZ + " mm).", Studio(s, field: "position:" + pos.Name));
    }

    private void Operations(StationConfig s)
    {
        var ops = s.Processes.Where(p => p.Kind == ProcessKind.Operation).ToList();
        var seen = new Dictionary<(Guid, int), ProcessDefinition>();
        foreach (var proc in ops)
            foreach (var o in proc.Implements)
            {
                if (seen.TryGetValue((o.GroupId, o.OperationNo), out var other) && other != proc)
                    Error("V017", "L'opération n° " + o.OperationNo + " est implémentée par deux process de " + s.Name + " (" + other.Name + ", " + proc.Name + ").", Studio(s, proc: proc.Id));
                else seen[(o.GroupId, o.OperationNo)] = proc;
            }
        foreach (var proc in ops.Where(p => p.Implements.Count > 1))
        {
            var sigs = proc.Implements.Select(o => _p.Group(o.GroupId)?.Operations.ElementAtOrDefault(o.OperationNo - 1)).Where(x => x != null).ToList();
            if (sigs.Any(x => !SameParams(x!, sigs[0]!)))
                Error("V017", "Le process " + proc.Name + " (" + s.Name + ") implémente des opérations dont les paramètres diffèrent.", Studio(s, proc: proc.Id));
        }
    }

    private static bool SameParams(OperationSignature a, OperationSignature b)
        => a.Parameters.Count == b.Parameters.Count && a.Parameters.Zip(b.Parameters, (x, y) => x.Type == y.Type).All(t => t);

    private void Recipes(StationConfig s)
    {
        foreach (var r in s.PressRecipes.Where(r => !r.Verified))
            Warn("V102", "La recette de presse " + r.No + " (" + r.Name + ", " + s.Name + ") n'est pas vérifiée sur la presse.", Studio(s, r.ObjectId));
    }

    // ------------------------------------------------------------------ process

    private void ProcessRules(StationConfig s, ProcessDefinition proc, Dictionary<Guid, ObjectNode> objects, List<ProcessDefinition> all)
    {
        CheckSteps(s, proc, proc.Body, objects, all);
        // Récursion de sous-process
        if (Reaches(proc, proc.Id, all, new HashSet<Guid>()))
            Error("V009", "Le process " + proc.Name + " (" + s.Name + ") s'appelle lui-même, directement ou non.", Studio(s, proc: proc.Id));
    }

    private static bool Reaches(ProcessDefinition from, Guid target, List<ProcessDefinition> all, HashSet<Guid> seen)
    {
        foreach (var call in from.AllSteps().OfType<CallStep>())
        {
            if (call.ProcessId == target) return true;
            var next = all.FirstOrDefault(p => p.Id == call.ProcessId);
            if (next != null && seen.Add(next.Id) && Reaches(next, target, all, seen)) return true;
        }
        return false;
    }

    private void CheckSteps(StationConfig s, ProcessDefinition? proc, Sequence body, Dictionary<Guid, ObjectNode> objects, List<ProcessDefinition> procs)
    {
        ValidationLink L(Step st) => Studio(s, proc: proc?.Id ?? Guid.Empty, step: st.Id);
        foreach (var step in ProcessTree.Flatten(body))
        {
            Guid? objId = step is ActionStep a ? a.ObjectId : step is FunctionStep f ? f.ModuleObjectId : (Guid?)null;
            if (objId != null && objects.TryGetValue(objId.Value, out var obj) && obj.Disabled)
                Error("V011", "L'étape utilise l'objet désactivé " + obj.Name + " (" + s.Name + ").", L(step));
            if (step is CallStep call && !procs.Any(p => p.Id == call.ProcessId))
                Error("V009", "Le sous-process appelé est introuvable (" + s.Name + ").", L(step));
            foreach (var cond in ConditionsOf(step))
                if (cond.Terms.Count == 0 || cond.Terms.Count > PlcLimits.ConditionTerms)
                    Error("V008", "Condition incomplète : " + cond.Terms.Count + " terme(s) (1 à " + PlcLimits.ConditionTerms + " attendus).", L(step));
            if (step is ParallelBlock par) ParallelRules(s, par, objects, L(step));
            if (step is ActionStep act && objects.TryGetValue(act.ObjectId, out var target) && target.TypeCode == "AsyrilEyePlus" && act.ActionNo == 3 && act.Fail.Kind != FailKind.Retry)
                Warn("V110", "L'étape Chercher pièce (" + s.Name + ") n'a pas de politique « Refaire ».", L(step));
        }
        GotoRules(s, proc, body, L0: st => Studio(s, proc: proc?.Id ?? Guid.Empty, step: st.Id));
    }

    private static IEnumerable<Condition> ConditionsOf(Step step)
    {
        switch (step)
        {
            case WaitConditionStep w: yield return w.Condition; break;
            case IfBlock i: yield return i.Condition; break;
            case RepeatBlock r when r.Mode == RepeatMode.While: yield return r.Condition; break;
        }
    }

    private void ParallelRules(StationConfig s, ParallelBlock par, Dictionary<Guid, ObjectNode> objects, ValidationLink link)
    {
        var sets = par.Branches.Select(b =>
        {
            var set = ProcessTree.ObjectsUsed(b);
            foreach (var id in set.ToList()) if (objects.TryGetValue(id, out var o)) foreach (var d in o.SelfAndDescendants()) set.Add(d.Id);
            return set;
        }).ToList();
        for (int i = 0; i < sets.Count; i++)
            for (int j = i + 1; j < sets.Count; j++)
                foreach (var shared in sets[i].Intersect(sets[j]))
                    Error("V004", "L'objet " + (objects.TryGetValue(shared, out var o) ? o.Name : shared.ToString()) + " est utilisé dans deux branches d'un même bloc En parallèle (" + s.Name + ").", link);
    }

    private void GotoRules(StationConfig s, ProcessDefinition? proc, Sequence root, Func<Step, ValidationLink> L0)
    {
        void Walk(Sequence seq, HashSet<Guid> visible)
        {
            var here = new HashSet<Guid>(visible);
            foreach (var st in seq.Steps) here.Add(st.Id);
            foreach (var st in seq.Steps)
            {
                Guid? target = st is ActionStep a ? a.Fail.GotoStepId : st is FunctionStep f ? f.Fail.GotoStepId : null;
                var kind = st is ActionStep a2 ? a2.Fail.Kind : st is FunctionStep f2 ? f2.Fail.Kind : FailKind.Ignore;
                if (kind == FailKind.Goto && (target == null || !here.Contains(target.Value)))
                    Error("V016", "La politique « Aller à l'étape » vise une étape hors de la branche de l'étape (" + s.Name + ").", L0(st));
                if (st is Block b)
                    foreach (var branch in b.Sequences) Walk(branch, here);
            }
        }
        Walk(root, new HashSet<Guid>());
    }

    private static IEnumerable<ValueRef> AllValueRefs(Sequence body)
    {
        foreach (var st in ProcessTree.Flatten(body))
        {
            switch (st)
            {
                case ActionStep a: foreach (var r in a.Args) yield return r; break;
                case FunctionStep f: foreach (var r in f.Args) yield return r; break;
                case RouteStep r: foreach (var v in r.Args) yield return v; break;
                case SetStep set: yield return set.Value; break;
            }
            foreach (var cond in ConditionsOf(st))
                foreach (var t in cond.Terms) { yield return t.Left; yield return t.Right; }
        }
    }

    // ------------------------------------------------------------------ process général

    private void GeneralProcess()
    {
        var gp = _p.GeneralProcess;
        var steps = gp.AllSteps().ToList();
        var routes = steps.OfType<RouteStep>().ToList();
        foreach (var r in routes)
        {
            var link = new ValidationLink("ProcessEditor", Field: "general", StepId: r.Id, ProcessId: gp.Id);
            var group = _p.Group(r.GroupId);
            if (group == null) { Error("V015", "L'étape Route vise une fonction inexistante.", link); continue; }
            if (group.StationIds.Count == 0) Error("V015", "La fonction " + group.Name + " est utilisée par le process général sans aucune station.", link);
            var op = group.Operations.ElementAtOrDefault(r.OperationNo - 1);
            if (op == null) Error("V012", "L'opération n° " + r.OperationNo + " n'existe pas dans la fonction " + group.Name + ".", link);
            else if (op.Parameters.Count != r.Args.Count)
                Error("V012", "L'appel de " + op.Name + " passe " + r.Args.Count + " paramètre(s) pour " + op.Parameters.Count + " attendu(s).", link);
        }
        foreach (var call in steps.OfType<CallStep>())
            if (call.ProcessId != gp.Id) Error("V009", "Sous-process appelé introuvable dans le process général.", new ValidationLink("ProcessEditor", StepId: call.Id));
        foreach (var cond in steps.SelectMany(ConditionsOf))
            if (cond.Terms.Count == 0 || cond.Terms.Count > PlcLimits.ConditionTerms) Error("V008", "Condition incomplète dans le process général.", new ValidationLink("ProcessEditor", Field: "general"));

        bool hasExit = routes.Any(r => _p.Group(r.GroupId)?.Role == GroupRole.Exit);
        bool hasReject = routes.Any(r => _p.Group(r.GroupId)?.Role == GroupRole.Reject);
        if (_p.Machine.RejectGroupId == null && !hasReject)
            Warn("V103", "Aucun chemin ne traite le verdict mauvais et aucune fonction de rebut n'existe.", new ValidationLink("ProcessEditor", Field: "general"));
        if (routes.Count > 0 && !hasExit)
            Warn("V104", "Aucun chemin du process général ne passe par une fonction de sortie avant la fin.", new ValidationLink("ProcessEditor", Field: "general"));
    }

    // ------------------------------------------------------------------ qualité, plan

    private void Quality()
    {
        var q = _p.Machine.Quality;
        void Rule(string name, QualityRule r, ValidationLink link)
        {
            bool bad = (r.ConsecWarn > 0 && r.ConsecAction > 0 && r.ConsecWarn >= r.ConsecAction)
                       || (r.RateWarnPercent > 0 && r.RateActionPercent > 0 && r.RateWarnPercent >= r.RateActionPercent)
                       || r.MinSample > r.Window || r.Window < 1 || r.Window > 255;
            if (bad) Error("V019", "Règle qualité incohérente (" + name + ") : avertissement ≥ action, échantillon minimal > fenêtre ou fenêtre hors 1..255.", link);
        }
        var l = new ValidationLink("Quality", Field: "rules");
        Rule("station constatée", q.StationObserved, l); Rule("station imputée", q.StationImputed, l); Rule("fonction", q.Function, l);
        Rule("mover", q.Mover, l); Rule("machine", q.Machine, l);
        foreach (var o in q.StationOverrides) Rule("surcharge de station", o.Rule, l);
        foreach (var o in q.FunctionOverrides) Rule("surcharge de fonction", o.Rule, l);
    }

    private void Planar()
    {
        foreach (var bad in _p.Machine.PlanarPositions.Where(pp => pp.StationId != null && _p.StationById(pp.StationId.Value) == null))
            Error("V013", "La position planaire « " + bad.Name + " » vise une station inexistante.", MachineStudio());
    }
}
