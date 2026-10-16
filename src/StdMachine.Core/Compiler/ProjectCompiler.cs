using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using StdMachine.Core.Catalog;
using StdMachine.Core.Validation;

namespace StdMachine.Core.Compiler;

public sealed record CompileResult(CompiledTables? Project, IReadOnlyList<ValidationIssue> Issues)
{
    public bool Succeeded => Project != null;
}

/// <summary>Une erreur de compilation que la validation n'a pas pu prévoir (limite dépassée, valeur non constante…).</summary>
public sealed class CompileException : Exception
{
    public CompileException(string message) : base(message) { }
}

/// <summary>Identifiant de carte pour le PLC : entier 32 bits non nul dérivé du Guid de la carte (docs/07 § 3.1), stable à chaque compilation.</summary>
public static class StepIds
{
    public static uint Of(Guid id)
    {
        var b = id.ToByteArray();
        uint v = BitConverter.ToUInt32(b, 0) ^ BitConverter.ToUInt32(b, 8);
        return v == 0 ? 1u : v;
    }
}

/// <summary>
/// Compilation d'un projet en tables (docs/06 § 5, docs/07 § 4.1) : validation, résolution des numéros, dépliage des fonctions de module,
/// génération des instructions, vérification des limites, empreinte SHA-256. Pure et rapide.
/// </summary>
public static class ProjectCompiler
{
    public static CompileResult Compile(MachineProject project, CatalogDocument catalog)
    {
        var issues = ProjectValidator.Validate(project, catalog);
        if (issues.Any(i => i.Severity == Severity.Error)) return new CompileResult(null, issues);
        try
        {
            var compiled = new Builder(project, catalog).Build();
            return new CompileResult(compiled, issues);
        }
        catch (CompileException e)
        {
            var all = issues.Concat(new[] { new ValidationIssue("V007", Severity.Error, e.Message, new ValidationLink("ProcessEditor")) }).ToList();
            return new CompileResult(null, all);
        }
    }

    private sealed class Builder
    {
        private readonly MachineProject _p;
        private readonly CatalogDocument _c;
        private readonly CompiledTables _out = new CompiledTables();

        public Builder(MachineProject p, CatalogDocument c) { _p = p; _c = c; }

        public CompiledTables Build()
        {
            BuildMachine();
            foreach (var s in _p.Stations.OrderBy(x => x.Number)) BuildStation(s);
            BuildGeneral();
            foreach (var s in _p.Stations.OrderBy(x => x.Number)) BuildPressRecipes(s);
            _out.Hash = Hash(_out);
            return _out;
        }

        // ------------------------------------------------------------------ machine

        private int StationSlot(Guid id) => _p.Stations.OrderBy(x => x.Number).ToList().FindIndex(x => x.Id == id) + 1;

        private void BuildMachine()
        {
            var m = _out.Machine;
            m.MachineName = _p.Name;
            m.ProjectId = _p.Id.ToString("D");
            m.Movers = _p.Machine.Planar.Movers;
            m.Slots = _p.Machine.Planar.Slots;
            m.NoStationDelayMs = _p.Machine.NoStationDelaySeconds * 1000;
            var groups = _p.Machine.Groups;
            m.RejectGroup = _p.Machine.RejectGroupId == null ? 0 : groups.FindIndex(g => g.Id == _p.Machine.RejectGroupId) + 1;
            foreach (var g in groups)
            {
                uint mask = 0;
                foreach (var sid in g.StationIds) mask |= 1u << (_p.Stations.OrderBy(x => x.Number).ToList().FindIndex(x => x.Id == sid));
                m.Groups.Add(new CGroup(g.Name, g.Role, mask, g.Operations.Count, g.Priority));
            }
            var pos = _p.Machine.PlanarPositions;
            foreach (var pp in pos) m.PlanarPositions.Add(new CPlanarPos(pp.X, pp.Y, pp.Kind, pp.StationId == null ? 0 : _p.Stations.OrderBy(x => x.Number).ToList().FindIndex(x => x.Id == pp.StationId) + 1));
            m.ParkPosition = pos.FindIndex(pp => pp.Kind == PlanarPositionKind.Park) + 1;
            foreach (var s in _p.Stations.OrderBy(x => x.Number))
            {
                uint gm = 0;
                for (int i = 0; i < groups.Count; i++) if (groups[i].StationIds.Contains(s.Id)) gm |= 1u << i;
                int work = pos.FindIndex(pp => pp.StationId == s.Id && pp.Kind == PlanarPositionKind.Work) + 1;
                var queue = new List<int>();
                for (int i = 0; i < pos.Count && queue.Count < 4; i++) if (pos[i].StationId == s.Id && pos[i].Kind == PlanarPositionKind.Queue) queue.Add(i + 1);
                m.Stations.Add(new CStationCfg(s.Name, gm, work, queue, s.OverrideDefault));
            }
            foreach (var pd in _p.Machine.PartData) m.PartData.Add(new CPartData(pd.Name, pd.Type, pd.Reserved));
            m.VerdictIndex = _p.Machine.PartData.FindIndex(x => x.Reserved && x.Name == "Verdict") + 1;
            m.Quality = _p.Machine.Quality;
            var q = _p.Machine.Quality;
            foreach (var st in _p.Stations.OrderBy(x => x.Number)) m.StationRules.Add(q.StationOverrides.FirstOrDefault(o => o.TargetId == st.Id)?.Rule ?? q.StationObserved);
            foreach (var g in groups) m.GroupRules.Add(q.FunctionOverrides.FirstOrDefault(o => o.TargetId == g.Id)?.Rule ?? q.Function);
            foreach (var g in groups)
            {
                uint mask = 0;
                if (g.ImputesRejects)
                    for (int i = 0; i < groups.Count; i++) if (groups[i].Role == GroupRole.Work && groups[i].Priority < g.Priority) mask |= 1u << i;
                m.GroupImputeMask.Add(mask);
            }
            foreach (var cfg in ObjectConfigs(_p.Machine.MachineObjects, out _)) m.Objects.Add(cfg);
        }

        // ------------------------------------------------------------------ objets

        private static List<ObjectNode> Breadth(IEnumerable<ObjectNode> roots)
        {
            var list = new List<ObjectNode>();
            var queue = new Queue<ObjectNode>(roots);
            while (queue.Count > 0)
            {
                var o = queue.Dequeue();
                list.Add(o);
                foreach (var ch in o.Children) queue.Enqueue(ch);
            }
            return list;
        }

        private IEnumerable<CObjConfig> ObjectConfigs(IEnumerable<ObjectNode> roots, out Dictionary<Guid, int> local)
        {
            var ordered = Breadth(roots);
            local = new Dictionary<Guid, int>();
            for (int i = 0; i < ordered.Count; i++) local[ordered[i].Id] = i + 1;
            var parent = new Dictionary<Guid, int>();
            foreach (var o in ordered) foreach (var ch in o.Children) parent[ch.Id] = local[o.Id];
            var result = new List<CObjConfig>();
            foreach (var o in ordered)
            {
                var type = _c.Type(o.TypeCode);
                var module = type == null ? _c.Modules.FirstOrDefault(x => x.Code == o.TypeCode) : null;
                var cfg = new CObjConfig
                {
                    Id = o.Id, Type = type?.Id ?? module?.Id ?? 0, LocalId = local[o.Id], ParentId = parent.TryGetValue(o.Id, out var pr) ? pr : 0,
                    Name = o.Name, Simulated = o.Simulated, Disabled = o.Disabled, Override = o.OverridePercent, Device = o.DeviceIndex ?? 0,
                };
                foreach (var cp in (IEnumerable<CatalogParam>?)type?.Params ?? module?.Params ?? new List<CatalogParam>())
                    if (cp.No >= 1 && cp.No <= 24) cfg.Params[cp.No - 1] = cp.Default;
                foreach (var pv in o.Params.Where(x => x.No >= 1 && x.No <= 24 && x.Number != null)) cfg.Params[pv.No - 1] = pv.Number!.Value;
                foreach (var b in o.Signals.Where(b => b.ChannelId != null && b.No >= 1 && b.No <= 8))
                {
                    var ch = _p.Machine.IoImage.FirstOrDefault(x => x.Id == b.ChannelId);
                    if (ch != null) cfg.Signals[b.No - 1] = new CSignalRef(ch.Kind, ch.Index);
                }
                foreach (var sp in o.Speeds.Where(x => x.No >= 1 && x.No <= 8)) cfg.SpeedPercent[sp.No - 1] = sp.Percent;
                foreach (var t in o.Timeouts.Where(x => x.ActionNo >= 1 && x.ActionNo <= 16)) cfg.TimeoutMs[t.ActionNo - 1] = (uint)t.Ms;
                result.Add(cfg);
            }
            return result;
        }

        // ------------------------------------------------------------------ station

        private void BuildStation(StationConfig s)
        {
            var data = new StationData { Number = s.Number, Name = s.Name };
            _out.Stations.Add(data);
            var configs = ObjectConfigs(s.Objects, out var local);
            foreach (var cfg in configs) data.Objects.Add(cfg);
            if (data.Objects.Count > PlcLimits.ObjectsPerStation) throw new CompileException("Station " + s.Name + " : " + data.Objects.Count + " objets pour une limite de " + PlcLimits.ObjectsPerStation + ".");
            foreach (var pos in s.Positions) data.Positions.Add(new CNamedPos(pos.X, pos.Y, pos.Z, pos.C, pos.Frame, pos.Approach ?? 0, pos.Taught, pos.Id));
            foreach (var v in s.Variables) data.Variables.Add(new CVarInit(v.Type, double.TryParse(v.Initial, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0));

            var ctx = new Ctx(_p, _c, s, data, local, _out, s.Number);
            var procs = s.Processes.Where(p => p.Kind != ProcessKind.General && p.Kind != ProcessKind.GeneralSub).ToList();
            if (procs.Count > 16) throw new CompileException("Station " + s.Name + " : " + procs.Count + " tables pour une limite de 16.");
            for (int i = 0; i < procs.Count; i++) ctx.TableNo[procs[i].Id] = i + 1;
            foreach (var proc in procs) ctx.CompileTable(proc, ToKind(proc.Kind));

            // Table des opérations : (fonction, opération) → table
            for (int g = 0; g < _p.Machine.Groups.Count; g++)
            {
                var group = _p.Machine.Groups[g];
                if (!group.StationIds.Contains(s.Id)) continue;
                for (int op = 0; op < group.Operations.Count; op++)
                {
                    var impl = procs.FirstOrDefault(p => p.Kind == ProcessKind.Operation && p.Implements.Any(o => o.GroupId == group.Id && o.OperationNo == op + 1));
                    if (impl != null) data.OpMap.Add(new COpMap(g + 1, op + 1, ctx.TableNo[impl.Id]));
                }
            }

            // Verrouillages : ceux de l'utilisateur, puis ceux des modules
            foreach (var il in s.Interlocks.OrderBy(x => x.Generated))
            {
                if (!local.TryGetValue(il.ObjectId, out int obj)) continue;
                uint mask = 0;
                if (!il.AllMotion) foreach (int a in il.Actions) if (a >= 1 && a <= 32) mask |= 1u << (a - 1);
                data.Interlocks.Add(new CInterlock(obj, mask, ctx.ConditionIndex(il.Condition)));
            }
            if (data.Interlocks.Count > PlcLimits.Interlocks) throw new CompileException("Station " + s.Name + " : trop de verrouillages.");
        }

        private static TableKind ToKind(ProcessKind k) => k switch
        {
            ProcessKind.Operation => TableKind.Operation,
            ProcessKind.Background => TableKind.Background,
            ProcessKind.Init => TableKind.Init,
            ProcessKind.Stop => TableKind.Stop,
            ProcessKind.Clear => TableKind.Clear,
            ProcessKind.Sub => TableKind.Sub,
            ProcessKind.GeneralSub => TableKind.GeneralSub,
            _ => TableKind.General,
        };

        private void BuildGeneral()
        {
            // Le process général se compile dans MachineData : mêmes règles, avec Route au lieu d'Action.
            var data = new StationData { Number = 0, Name = "Machine" };
            var ctx = new Ctx(_p, _c, null, data, new Dictionary<Guid, int>(), _out, 0);
            ctx.TableNo[_p.GeneralProcess.Id] = 1;
            ctx.CompileTable(_p.GeneralProcess, TableKind.General);
            var m = _out.Machine;
            m.Tables.AddRange(data.Tables);
            m.Instructions.AddRange(data.Instructions);
            m.Conditions.AddRange(data.Conditions);
            m.Texts.AddRange(data.Texts);
        }

        private void BuildPressRecipes(StationConfig s)
        {
            var objects = Breadth(s.Objects);
            foreach (var r in s.PressRecipes.OrderBy(x => x.ObjectId).ThenBy(x => x.No))
            {
                int obj = objects.FindIndex(o => o.Id == r.ObjectId) + 1;
                _out.PressRecipes.Add(new CPressRecipe(s.Number, obj, r.No,
                    r.Segments.Select(g => new CPressSegment(g.Target, g.TargetValue, g.SpeedPercent, g.ForceMin, g.ForceMax, g.StrokeMin, g.StrokeMax)).ToList()));
            }
        }

        private static readonly JsonSerializerOptions HashOptions = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() },
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        private static string Hash(CompiledTables c)
        {
            // La carte de compilation et l'empreinte elle-même n'entrent pas dans l'empreinte.
            var json = JsonSerializer.Serialize(new { c.Machine, c.Stations, c.PressRecipes }, HashOptions);
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(json)).Select(b => b.ToString("x2")));
        }
    }

    // ---------------------------------------------------------------------- compilation d'une table

    private sealed class Ctx
    {
        private readonly MachineProject _p;
        private readonly CatalogDocument _c;
        private readonly StationConfig? _s;
        private readonly StationData _d;
        private readonly Dictionary<Guid, int> _local;
        private readonly CompiledTables _out;
        private readonly int _stationNo;
        private int _hiddenVars;
        private readonly Dictionary<Guid, int> _firstInstr = new Dictionary<Guid, int>();
        private readonly List<(int Instr, Guid Target)> _gotos = new List<(int, Guid)>();
        private ProcessDefinition _proc = null!;
        private readonly List<string> _path = new List<string>();

        public readonly Dictionary<Guid, int> TableNo = new Dictionary<Guid, int>();

        public Ctx(MachineProject p, CatalogDocument c, StationConfig? s, StationData d, Dictionary<Guid, int> local, CompiledTables output, int stationNo)
        {
            _p = p; _c = c; _s = s; _d = d; _local = local; _out = output; _stationNo = stationNo;
        }

        private int Next => _d.Instructions.Count + 1;

        private string Where => (_s?.Name ?? "Process général") + " › " + _proc.Name;

        public void CompileTable(ProcessDefinition proc, TableKind kind)
        {
            _proc = proc;
            _firstInstr.Clear();
            _gotos.Clear();
            _path.Clear();
            _path.Add(proc.Name);
            int start = Next;
            Sequence(proc.Body);
            // Fin de table (docs/06 § 5.3)
            switch (kind)
            {
                case TableKind.Background: Emit(Op.End, 0, args: new[] { 1 }); break;
                case TableKind.Sub:
                case TableKind.GeneralSub: Emit(Op.Return, 0); break;
                default: Emit(Op.End, 0, args: new[] { 0 }); break;
            }
            foreach (var (instr, target) in _gotos)
            {
                if (!_firstInstr.TryGetValue(target, out int idx)) throw new CompileException(Where + " : cible « Aller à l'étape » introuvable.");
                _d.Instructions[instr - 1].FailTarget = idx;
            }
            int length = Next - start;
            _d.Tables.Add(new TableDir(proc.Name, kind, start, length, proc.Id));
            if (_d.Instructions.Count > 512) throw new CompileException(Where + " : " + _d.Instructions.Count + " instructions pour une limite de 512.");
        }

        // ---- émission

        private Instr Emit(Op op, uint stepId, int obj = 0, int action = 0, CValue[]? ps = null, CBind[]? binds = null, int[]? args = null, Guid? stepGuid = null)
        {
            var ins = new Instr { Op = op, StepId = stepId, Object = obj, Action = action, Params = ps ?? new CValue[0], Binds = binds ?? new CBind[0], Args = args ?? new int[0] };
            _d.Instructions.Add(ins);
            if (stepGuid != null)
                _out.Map.Add(new MapEntry(_stationNo, _d.Instructions.Count, stepGuid.Value, stepId, string.Join(" › ", _path)));
            return ins;
        }

        private void Sequence(Sequence seq)
        {
            foreach (var step in seq.Steps) CompileStep(step, false);
        }

        private void CompileStep(Step step, bool inner)
        {
            uint id = StepIds.Of(step.Id);
            if (!_firstInstr.ContainsKey(step.Id)) _firstInstr[step.Id] = Next;
            _path.Add(step.Label ?? Describe(step));
            try
            {
                switch (step)
                {
                    case ActionStep a: Card(step, id, () => EmitAction(a, id)); break;
                    case FunctionStep f: Card(step, id, () => EmitFunction(f, id)); break;
                    case WaitConditionStep w: Card(step, id, () => Emit(Op.WaitCond, id, args: new[] { ConditionIndex(w.Condition) }, stepGuid: step.Id).TimeoutMs = TimeoutMs(w.TimeoutSeconds)); break;
                    case WaitTimeStep t: Card(step, id, () => Emit(Op.WaitTime, id, ps: new[] { new CValue(ValueSource.Const, 0, Math.Round(t.DurationSeconds * 1000)) }, stepGuid: step.Id)); break;
                    case CallStep c: Card(step, id, () => Emit(Op.Call, id, args: new[] { TableNo.TryGetValue(c.ProcessId, out var t) ? t : throw new CompileException(Where + " : sous-process introuvable.") }, stepGuid: step.Id)); break;
                    case SetStep s: Card(step, id, () => EmitSet(s, id)); break;
                    case AlarmStep al: Card(step, id, () => Emit(Op.Alarm, id, args: new[] { TextIndex(al.Message), al.Severity, al.AlarmNo }, stepGuid: step.Id)); break;
                    case EndStep _: Emit(Op.End, id, args: new[] { 0 }, stepGuid: step.Id); break;
                    case RouteStep r: EmitRoute(r, id); break;
                    case IfBlock b: EmitIf(b, id); break;
                    case ParallelBlock par: EmitParallel(par, id); break;
                    case RepeatBlock rep: EmitRepeat(rep, id); break;
                    default: throw new CompileException(Where + " : type d'étape non pris en charge (" + step.GetType().Name + ").");
                }
            }
            finally { _path.RemoveAt(_path.Count - 1); }
        }

        private static string Describe(Step s) => s switch
        {
            ActionStep a => "Action " + a.ActionNo,
            FunctionStep f => f.FunctionId,
            WaitConditionStep _ => "Attendre condition",
            WaitTimeStep _ => "Attendre durée",
            CallStep _ => "Sous-process",
            SetStep _ => "Définir",
            AlarmStep _ => "Défaut",
            EndStep _ => "Fin",
            RouteStep _ => "Opération",
            IfBlock _ => "Si / Sinon",
            ParallelBlock _ => "En parallèle",
            RepeatBlock _ => "Répéter",
            _ => s.GetType().Name,
        };

        private void Card(Step step, uint id, Action body)
        {
            Emit(Op.StepBegin, id, args: new[] { 1 }, stepGuid: step.Id);
            body();
            Emit(Op.StepEnd, id, args: new[] { 1 }, stepGuid: step.Id);
        }

        // ---- valeurs

        public CValue Resolve(ValueRef r)
        {
            switch (r.Kind)
            {
                case ValueKind.Const: return new CValue(ValueSource.Const, 0, r.Number + r.Offset);
                case ValueKind.Var: return new CValue(ValueSource.Var, VarIndex(r.Ref), r.Offset);
                case ValueKind.PartData: return new CValue(ValueSource.PartData, _p.Machine.PartData.FindIndex(x => x.Id == r.Ref) + 1, 0);
                case ValueKind.OpParam: return new CValue(ValueSource.OpParam, r.Index, 0);
                case ValueKind.NamedPos:
                    if (_s == null) throw new CompileException(Where + " : une position nommée n'existe pas au niveau machine.");
                    int pi = _s.Positions.FindIndex(x => x.Id == r.Ref) + 1;
                    if (pi == 0) throw new CompileException(Where + " : position nommée introuvable.");
                    return new CValue(ValueSource.NamedPos, pi, r.Offset);
                case ValueKind.Speed: return r.Index > 0 ? new CValue(ValueSource.Speed, r.Index, 0) : new CValue(ValueSource.Speed, 0, r.Number);
                case ValueKind.Text: return new CValue(ValueSource.Text, TextIndex(r.Text ?? string.Empty), 0);
                case ValueKind.ObjValue:
                    if (!_local.TryGetValue(r.Ref ?? Guid.Empty, out int obj)) throw new CompileException(Where + " : objet introuvable pour une valeur d'objet.");
                    return new CValue(ValueSource.ObjValue, obj, r.Index);
                case ValueKind.System: return new CValue(ValueSource.System, r.Index, 0);
                default: throw new CompileException(Where + " : valeur de type " + r.Kind + " non résolue.");
            }
        }

        private int VarIndex(Guid? id)
        {
            int i = _s == null ? -1 : _s.Variables.FindIndex(x => x.Id == id);
            if (i < 0) throw new CompileException(Where + " : variable introuvable.");
            return i + 1;
        }

        private int TextIndex(string text)
        {
            if (text.Length > 80) text = text.Substring(0, 80);
            int i = _d.Texts.IndexOf(text);
            if (i >= 0) return i + 1;
            if (_d.Texts.Count >= 32) throw new CompileException(Where + " : plus de 32 textes.");
            _d.Texts.Add(text);
            return _d.Texts.Count;
        }

        public int ConditionIndex(Condition cond)
        {
            var terms = cond.Terms.Select(t => new CCondTerm(Resolve(t.Left), Map(t.Op), Resolve(t.Right))).ToList();
            var c = new CCondition(cond.Combine, terms);
            for (int i = 0; i < _d.Conditions.Count; i++)
                if (_d.Conditions[i].Combine == c.Combine && _d.Conditions[i].Terms.SequenceEqual(c.Terms)) return i + 1;
            if (_d.Conditions.Count >= 64) throw new CompileException(Where + " : plus de 64 conditions.");
            _d.Conditions.Add(c);
            return _d.Conditions.Count;
        }

        private static Cmp Map(CompareOp op) => op switch
        {
            CompareOp.Equal => Cmp.Eq,
            CompareOp.NotEqual => Cmp.Ne,
            CompareOp.Less => Cmp.Lt,
            CompareOp.LessOrEqual => Cmp.Le,
            CompareOp.Greater => Cmp.Gt,
            _ => Cmp.Ge,
        };

        private int HiddenVar()
        {
            _hiddenVars++;
            int v = 64 - _hiddenVars + 1;
            if (_s != null && _s.Variables.Count + _hiddenVars > 64) throw new CompileException(Where + " : variables visibles et cachées au-delà de 64.");
            return v;
        }

        // ---- cartes

        private static uint TimeoutMs(double? seconds) => seconds == null ? 0u : (uint)Math.Round(seconds.Value * 1000);

        private void EmitAction(ActionStep a, uint id)
        {
            if (!_local.TryGetValue(a.ObjectId, out int obj)) throw new CompileException(Where + " : objet de l'action introuvable.");
            var binds = new CBind[8];
            for (int i = 0; i < 8; i++) binds[i] = new CBind();
            foreach (var b in a.Bindings.Where(b => b.ResultNo >= 1 && b.ResultNo <= 8))
                binds[b.ResultNo - 1] = Bind(b.Target, b.Ref);
            var ins = Emit(Op.Action, id, obj, a.ActionNo, a.Args.Select(Resolve).ToArray(), binds, stepGuid: a.Id);
            ins.TimeoutMs = TimeoutMs(a.TimeoutSeconds);
            ApplyFail(ins, a.Fail);
        }

        private CBind Bind(BindTarget target, Guid? reference) => target switch
        {
            BindTarget.Var => new CBind(BindKind.Var, VarIndex(reference)),
            BindTarget.PartData => new CBind(BindKind.PartData, _p.Machine.PartData.FindIndex(x => x.Id == reference) + 1),
            BindTarget.PartId => new CBind(BindKind.PartId, 0),
            BindTarget.Verdict => new CBind(BindKind.Verdict, 0),
            _ => new CBind(BindKind.PartRemoved, 0),
        };

        private void ApplyFail(Instr ins, FailPolicy f)
        {
            ins.OnFail = f.Kind;
            ins.Retries = f.Retries;
            if (f.Kind == FailKind.Goto && f.GotoStepId != null) _gotos.Add((_d.Instructions.Count, f.GotoStepId.Value));
        }

        private void EmitSet(SetStep s, uint id)
        {
            CBind target = s.Target switch
            {
                SetTarget.Var => new CBind(BindKind.Var, VarIndex(s.TargetRef)),
                SetTarget.PartData => new CBind(BindKind.PartData, _p.Machine.PartData.FindIndex(x => x.Id == s.TargetRef) + 1),
                SetTarget.Verdict => new CBind(BindKind.Verdict, 0),
                _ => new CBind(BindKind.PartRemoved, 0),
            };
            Emit(Op.SetVar, id, ps: new[] { Resolve(s.Value) }, binds: new[] { target }, args: new[] { (int)s.Operation }, stepGuid: s.Id);
        }

        private void EmitRoute(RouteStep r, uint id)
        {
            int g = _p.Machine.Groups.FindIndex(x => x.Id == r.GroupId) + 1;
            Emit(Op.Route, id, ps: r.Args.Select(Resolve).ToArray(), args: new[] { g, r.OperationNo, r.Slot }, stepGuid: r.Id);
        }

        // ---- blocs (docs/06 § 5.2)

        private void EmitIf(IfBlock b, uint id)
        {
            int cond = ConditionIndex(b.Condition);
            var jif = Emit(Op.JumpIfFalse, id, args: new[] { cond, 0 }, stepGuid: b.Id);
            _path.Add("Alors");
            Sequence(b.Then);
            _path.RemoveAt(_path.Count - 1);
            if (b.Else.Steps.Count == 0)
            {
                jif.Args[1] = Next;
                return;
            }
            var jump = Emit(Op.Jump, id, args: new[] { 0 }, stepGuid: b.Id);
            jif.Args[1] = Next;
            _path.Add("Sinon");
            Sequence(b.Else);
            _path.RemoveAt(_path.Count - 1);
            jump.Args[0] = Next;
        }

        private void EmitParallel(ParallelBlock par, uint id)
        {
            var branches = par.Branches.Where(x => x.Steps.Count > 0).ToList();
            if (branches.Count == 0) return;
            if (branches.Count == 1) { Sequence(branches[0]); return; }
            int fork = Next;
            var forkIns = Emit(Op.Fork, id, args: new int[6], stepGuid: par.Id);
            var jumps = new List<Instr>();
            for (int i = 0; i < branches.Count; i++)
            {
                forkIns.Args[1 + i] = Next;
                _path.Add("Branche " + (i + 1));
                Sequence(branches[i]);
                _path.RemoveAt(_path.Count - 1);
                if (i < branches.Count - 1) jumps.Add(Emit(Op.Jump, id, args: new[] { 0 }, stepGuid: par.Id));
            }
            int join = Next;
            foreach (var j in jumps) j.Args[0] = join;
            forkIns.Args[0] = join;
            Emit(Op.Join, id, args: new[] { fork }, stepGuid: par.Id);
        }

        private void EmitRepeat(RepeatBlock rep, uint id)
        {
            int counter = HiddenVar();
            if (rep.Mode == RepeatMode.Count)
            {
                Emit(Op.LoopInit, id, ps: new[] { new CValue(ValueSource.Const, 0, rep.Count) }, args: new[] { counter }, stepGuid: rep.Id);
                int start = Next;
                Sequence(rep.Body);
                Emit(Op.LoopNext, id, args: new[] { counter, start }, stepGuid: rep.Id);
                return;
            }
            Emit(Op.LoopInit, id, ps: new[] { new CValue(ValueSource.Const, 0, rep.Max) }, args: new[] { counter }, stepGuid: rep.Id);
            int loop = Next;
            var jif = Emit(Op.JumpIfFalse, id, args: new[] { ConditionIndex(rep.Condition), 0 }, stepGuid: rep.Id);
            Sequence(rep.Body);
            Emit(Op.LoopNext, id, args: new[] { counter, loop }, stepGuid: rep.Id);
            Emit(Op.Alarm, id, args: new[] { TextIndex("Nombre maximal de répétitions atteint"), 2, 5 }, stepGuid: rep.Id);
            jif.Args[1] = Next;
        }

        // ---- fonctions de module et fonctions personnalisées

        private void EmitFunction(FunctionStep f, uint id)
        {
            var node = _s?.FindObject(f.ModuleObjectId) ?? throw new CompileException(Where + " : module introuvable.");
            IReadOnlyList<Step> inner;
            if (f.FunctionId.StartsWith("custom:", StringComparison.Ordinal))
            {
                var custom = _s!.CustomFunctions.FirstOrDefault(x => x.FunctionId == f.FunctionId) ?? throw new CompileException(Where + " : fonction personnalisée introuvable.");
                inner = Substitute(custom, f);
            }
            else
            {
                var module = _c.Modules.FirstOrDefault(m => m.Code == node.TypeCode) ?? throw new CompileException(Where + " : « " + node.TypeCode + " » n'est pas un module.");
                var fn = module.Functions.FirstOrDefault(x => x.Id == f.FunctionId) ?? throw new CompileException(Where + " : fonction « " + f.FunctionId + " » inconnue pour " + module.Name + ".");
                inner = new ModuleExpander(_c, node, module, fn, f, Where).Expand();
            }
            foreach (var s in inner)
            {
                Emit(Op.StepBegin, id, args: new[] { 2 }, stepGuid: f.Id);
                CompileInner(s, id, f);
                Emit(Op.StepEnd, id, args: new[] { 2 }, stepGuid: f.Id);
            }
        }

        // Étapes internes : elles portent l'identifiant de la carte de la fonction (la carte de compilation les rattache à elle).
        private void CompileInner(Step step, uint cardId, FunctionStep card)
        {
            switch (step)
            {
                case ActionStep a:
                    var copy = (ActionStep)a.Clone();
                    EmitActionAs(copy, cardId, card.Id, card.Fail);
                    break;
                case ParallelBlock par:
                    var branches = par.Branches.Where(x => x.Steps.Count > 0).ToList();
                    if (branches.Count == 0) return;
                    if (branches.Count == 1) { foreach (var s in branches[0].Steps) CompileInner(s, cardId, card); return; }
                    int fork = Next;
                    var forkIns = Emit(Op.Fork, cardId, args: new int[6], stepGuid: card.Id);
                    var jumps = new List<Instr>();
                    for (int i = 0; i < branches.Count; i++)
                    {
                        forkIns.Args[1 + i] = Next;
                        foreach (var s in branches[i].Steps) CompileInner(s, cardId, card);
                        if (i < branches.Count - 1) jumps.Add(Emit(Op.Jump, cardId, args: new[] { 0 }, stepGuid: card.Id));
                    }
                    int join = Next;
                    foreach (var j in jumps) j.Args[0] = join;
                    forkIns.Args[0] = join;
                    Emit(Op.Join, cardId, args: new[] { fork }, stepGuid: card.Id);
                    break;
                default: throw new CompileException(Where + " : étape interne " + step.GetType().Name + " non prise en charge.");
            }
        }

        private void EmitActionAs(ActionStep a, uint cardId, Guid cardGuid, FailPolicy fail)
        {
            if (!_local.TryGetValue(a.ObjectId, out int obj)) throw new CompileException(Where + " : objet de la fonction introuvable.");
            var binds = Enumerable.Range(0, 8).Select(_ => new CBind()).ToArray();
            var ins = Emit(Op.Action, cardId, obj, a.ActionNo, a.Args.Select(Resolve).ToArray(), binds, stepGuid: cardGuid);
            ApplyFail(ins, fail);
        }

        /// <summary>Corps d'une fonction personnalisée avec ses paramètres remplacés par les arguments de l'appel.</summary>
        private IReadOnlyList<Step> Substitute(CustomFunction custom, FunctionStep call)
        {
            var values = new Dictionary<Guid, ValueRef>();
            for (int i = 0; i < custom.Params.Count; i++)
                values[custom.Params[i].Id] = i < call.Args.Count ? call.Args[i] : ValueRef.Const(custom.Params[i].Default);

            ValueRef Sub(ValueRef r)
            {
                if (r.Kind != ValueKind.FuncParam) return r;
                var v = values.TryGetValue(r.Ref ?? Guid.Empty, out var found) ? found : throw new CompileException(Where + " : paramètre de fonction introuvable.");
                double extra = r.Offset;
                if (r.Text != null && Guid.TryParseExact(r.Text, "N", out var other) && values.TryGetValue(other, out var o) && o.Kind == ValueKind.Const) extra += o.Number;
                return extra == 0 ? v : v with { Offset = v.Offset + extra };
            }

            var result = new List<Step>();
            foreach (var s in custom.Body.Steps)
            {
                if (!(s is ActionStep a)) throw new CompileException(Where + " : une fonction personnalisée ne contient que des actions de base.");
                var copy = (ActionStep)a.Clone();
                copy.Args.Clear();
                foreach (var arg in a.Args) copy.Args.Add(Sub(arg));
                result.Add(copy);
            }
            return result;
        }
    }

    // ---------------------------------------------------------------------- dépliage d'une fonction de module

    /// <summary>Transforme le corps JSON d'une fonction du catalogue (étapes sur des rôles) en étapes sur les objets du module.</summary>
    private sealed class ModuleExpander
    {
        private readonly CatalogDocument _c;
        private readonly ObjectNode _module;
        private readonly CatalogModule _def;
        private readonly CatalogFunction _fn;
        private readonly FunctionStep _call;
        private readonly string _where;

        public ModuleExpander(CatalogDocument c, ObjectNode module, CatalogModule def, CatalogFunction fn, FunctionStep call, string where)
        {
            _c = c; _module = module; _def = def; _fn = fn; _call = call; _where = where;
        }

        private double ModuleParam(int no)
            => _module.Params.FirstOrDefault(x => x.No == no)?.Number ?? _def.Params.FirstOrDefault(x => x.No == no)?.Default ?? 0;

        private ObjectNode? Child(string role) => _module.Children.FirstOrDefault(c => c.ChildRole == role);

        private ValueRef? Arg(string name)
        {
            int i = _fn.Params.FindIndex(p => p.Name == name);
            // Une fonction sans paramètre de vitesse (Dégager, Référencer) se déplace à la vitesse rapide V2.
            if (i < 0) return name == "v" ? ValueRef.Speed(2) : null;
            if (i < _call.Args.Count) return _call.Args[i];
            var d = _fn.Params[i].Default;
            if (d == null) return null;
            var e = d.Value;
            if (e.ValueKind == JsonValueKind.Number) return ValueRef.Const(e.GetDouble());
            if (e.ValueKind == JsonValueKind.String && e.GetString()!.StartsWith("P", StringComparison.Ordinal)) return ValueRef.Const(ModuleParam(int.Parse(e.GetString()!.Substring(1))));
            return null;
        }

        public IReadOnlyList<Step> Expand()
        {
            var steps = new List<Step>();
            foreach (var item in _fn.Body.EnumerateArray()) Add(item, steps);
            return steps;
        }

        private void Add(JsonElement e, List<Step> into)
        {
            string kind = e.GetProperty("step").GetString()!;
            switch (kind)
            {
                case "action": { var a = Action(e); if (a != null) into.Add(a); break; }
                case "ifAbove": foreach (var t in e.GetProperty("then").EnumerateArray()) Add(t, into); break; // « si en dessous » : l'action est émise sans condition (docs/06 § 5.6)
                case "ifRole":
                    if (Child(e.GetProperty("role").GetString()!) != null) foreach (var t in e.GetProperty("then").EnumerateArray()) Add(t, into);
                    break;
                case "parallel":
                    var par = new ParallelBlock();
                    par.Branches.Clear();
                    foreach (var br in e.GetProperty("branches").EnumerateArray())
                    {
                        var seq = new Sequence();
                        foreach (var t in br.EnumerateArray()) Add(t, seq.Steps);
                        par.Branches.Add(seq);
                    }
                    into.Add(par);
                    break;
                case "toolOr": ToolOr(e, into); break;
                case "toolRelease": ToolRelease(e, into); break;
                case "initByOrder": InitByOrder(e, into); break;
                default: throw new CompileException(_where + " : étape de fonction de module inconnue (" + kind + ").");
            }
        }

        private ActionStep? Action(JsonElement e)
        {
            var role = e.GetProperty("role").GetString()!;
            var child = Child(role);
            if (child == null) return null;
            var a = new ActionStep { ObjectId = child.Id, ActionNo = e.GetProperty("action").GetInt32() };
            foreach (var arg in e.GetProperty("args").EnumerateArray()) a.Args.Add(ArgValue(arg, role));
            return a;
        }

        private ValueRef ArgValue(JsonElement arg, string role)
        {
            switch (arg.GetProperty("ref").GetString())
            {
                case "const": return ValueRef.Const(arg.GetProperty("value").GetDouble());
                case "safeZ": return ValueRef.Const(ModuleParam(1));
                case "moduleParam": return ValueRef.Speed((int)ModuleParam(arg.GetProperty("no").GetInt32()));
                case "param":
                    var v = Arg(arg.GetProperty("name").GetString()!) ?? throw new CompileException(_where + " : argument « " + arg.GetProperty("name").GetString() + " » manquant.");
                    // La hauteur d'approche est un paramètre propre de la fonction : le décalage éventuel de la position passée en argument est ignoré.
                    v = v.Kind == ValueKind.NamedPos || v.Kind == ValueKind.Var ? v with { Offset = 0 } : v;
                    if (arg.TryGetProperty("plus", out var plus))
                    {
                        var ap = Arg(plus.GetString()!);
                        if (ap == null || ap.Kind != ValueKind.Const) throw new CompileException(_where + " : la hauteur d'approche doit être une constante.");
                        // Z négatif vers le bas (P4 = 0) : « au-dessus » = Z plus grand.
                        double sign = ModuleParam(4) == 0 ? 1 : -1;
                        v = v with { Offset = sign * ap.Number };
                    }
                    return v;
                default: throw new CompileException(_where + " : référence d'argument inconnue.");
            }
        }

        private void ToolOr(JsonElement e, List<Step> into)
        {
            var tool = Child("Tool") ?? throw new CompileException(_where + " : pas d'outil.");
            foreach (var t in e.GetProperty("tool").EnumerateArray())
            {
                if (_c.Type(t.GetProperty("typeCode").GetString()!)?.Id != _c.Type(tool.TypeCode)?.Id) continue;
                var a = new ActionStep { ObjectId = tool.Id, ActionNo = t.GetProperty("action").GetInt32() };
                foreach (var arg in t.GetProperty("args").EnumerateArray()) a.Args.Add(ArgValue(arg, "Tool"));
                into.Add(a);
                return;
            }
            throw new CompileException(_where + " : type d'outil non pris en charge (" + tool.TypeCode + ").");
        }

        private void ToolRelease(JsonElement e, List<Step> into)
        {
            var tool = Child("Tool") ?? throw new CompileException(_where + " : pas d'outil.");
            if (tool.TypeCode == "Gripper") { into.Add(new ActionStep { ObjectId = tool.Id, ActionNo = 1 }); return; }
            var blow = Arg(e.GetProperty("blowParam").GetString()!);
            if (blow == null || blow.Kind != ValueKind.Const) throw new CompileException(_where + " : le paramètre de soufflage doit être une constante.");
            if (blow.Number != 0) into.Add(new ActionStep { ObjectId = tool.Id, ActionNo = 2 });
            into.Add(new ActionStep { ObjectId = tool.Id, ActionNo = 3 });
        }

        private void InitByOrder(JsonElement e, List<Step> into)
        {
            string first = e.GetProperty("firstRole").GetString()!;
            var axes = _module.Children.Where(c => c.TypeCode == "LinearAxis" || c.TypeCode == "RotaryAxis")
                .OrderBy(c => c.ChildRole == first ? 0 : 1).ThenBy(c => c.Params.FirstOrDefault(x => x.No == 10)?.Number ?? 1).ToList();
            foreach (var a in axes) into.Add(new ActionStep { ObjectId = a.Id, ActionNo = 3 });
        }
    }
}
