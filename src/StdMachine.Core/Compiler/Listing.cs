using System.Globalization;
using System.Text;

namespace StdMachine.Core.Compiler;

/// <summary>Listing lisible d'une table compilée (onglet « Table compilée » de l'éditeur, docs/06 § 5.6).</summary>
public static class Listing
{
    public static string Of(CompiledTables project, int station, string tableName)
    {
        var instrs = station == 0 ? project.Machine.Instructions : project.Station(station)?.Instructions ?? new List<Instr>();
        var tables = station == 0 ? project.Machine.Tables : project.Station(station)?.Tables ?? new List<TableDir>();
        var dir = tables.FirstOrDefault(t => t.Name == tableName) ?? throw new ArgumentException("Table inconnue : " + tableName);
        var sb = new StringBuilder();
        sb.AppendLine("Table « " + dir.Name + " » (" + dir.Kind + "), instructions " + dir.Start + " à " + (dir.Start + dir.Length - 1));
        int depth = 0;
        for (int i = dir.Start; i < dir.Start + dir.Length; i++)
        {
            var ins = instrs[i - 1];
            if (ins.Op == Op.StepEnd) depth = Math.Max(0, depth - 1);
            var origin = project.OriginOf(station, i);
            sb.Append(i.ToString(CultureInfo.InvariantCulture).PadLeft(4)).Append("  ").Append(new string(' ', depth * 2)).Append(Describe(ins));
            if (ins.StepId != 0) sb.Append("   step=").Append(ins.StepId);
            if (origin != null && (ins.Op == Op.StepBegin || ins.Op == Op.Fork)) sb.Append("  ").Append(origin.Path);
            sb.AppendLine();
            if (ins.Op == Op.StepBegin) depth++;
        }
        return sb.ToString();
    }

    private static string Describe(Instr i)
    {
        string a(int n) => n < i.Args.Length ? i.Args[n].ToString(CultureInfo.InvariantCulture) : "-";
        switch (i.Op)
        {
            case Op.Action:
                var sb = new StringBuilder("Action obj " + i.Object + " act " + i.Action);
                for (int k = 0; k < i.Params.Length; k++) sb.Append(" a").Append(k + 1).Append('=').Append(Value(i.Params[k]));
                for (int k = 0; k < i.Binds.Length; k++)
                    if (i.Binds[k].Target != BindKind.None) sb.Append(" r").Append(k + 1).Append('→').Append(i.Binds[k].Target).Append(i.Binds[k].Index);
                if (i.OnFail != FailKind.StopStation) sb.Append(" onFail=").Append(i.OnFail).Append(i.OnFail == FailKind.Retry ? " ×" + i.Retries : string.Empty);
                return sb.ToString();
            case Op.StepBegin: return "StepBegin(" + a(0) + ")";
            case Op.StepEnd: return "StepEnd(" + a(0) + ")";
            case Op.WaitCond: return "WaitCond(" + a(0) + ")";
            case Op.WaitTime: return "WaitTime(" + Value(i.Params[0]) + ")";
            case Op.Jump: return "Jump(" + a(0) + ")";
            case Op.JumpIfFalse: return "JumpIfFalse(cond " + a(0) + ", " + a(1) + ")";
            case Op.Fork: return "Fork(J=" + a(0) + ", " + string.Join(", ", i.Args.Skip(1).Where(x => x != 0)) + ")";
            case Op.Join: return "Join(" + a(0) + ")";
            case Op.LoopInit: return "LoopInit(var " + a(0) + ", " + Value(i.Params[0]) + ")";
            case Op.LoopNext: return "LoopNext(var " + a(0) + ", " + a(1) + ")";
            case Op.Call: return "Call(table " + a(0) + ")";
            case Op.Return: return "Return";
            case Op.SetVar: return "SetVar target=" + i.Binds[0].Target + (i.Binds[0].Index > 0 ? i.Binds[0].Index.ToString(CultureInfo.InvariantCulture) : string.Empty) + " op=" + a(0) + " value=" + Value(i.Params[0]);
            case Op.Alarm: return "Alarm(text " + a(0) + ", gravité " + a(1) + ", n° " + a(2) + ")";
            case Op.End: return "End(" + a(0) + ")";
            case Op.Route: return "Route(fonction " + a(0) + ", opération " + a(1) + ", emplacement " + a(2) + ")" + (i.Params.Length > 0 ? " " + string.Join(" ", i.Params.Select((p, k) => "a" + (k + 1) + "=" + Value(p))) : string.Empty);
            default: return i.Op.ToString();
        }
    }

    public static string Value(CValue v)
    {
        string off = v.Real != 0 && v.Source != ValueSource.Const && v.Source != ValueSource.ObjValue ? "(" + (v.Real > 0 ? "+" : string.Empty) + v.Real.ToString("0.###", CultureInfo.InvariantCulture) + ")" : string.Empty;
        switch (v.Source)
        {
            case ValueSource.Const: return "Const(" + v.Real.ToString("0.###", CultureInfo.InvariantCulture) + ")";
            case ValueSource.Var: return "Var" + v.Index + off;
            case ValueSource.PartData: return "PartData" + v.Index;
            case ValueSource.OpParam: return "OpParam" + v.Index;
            case ValueSource.NamedPos: return "Pos" + v.Index + off;
            case ValueSource.Speed: return v.Index > 0 ? "Speed(" + v.Index + ")" : "Speed(" + v.Real.ToString("0.###", CultureInfo.InvariantCulture) + "%)";
            case ValueSource.Text: return "Text" + v.Index;
            case ValueSource.ObjValue: return "Obj" + v.Index + ".E" + v.Real.ToString("0", CultureInfo.InvariantCulture);
            case ValueSource.System: return "Sys" + v.Index;
            default: return "-";
        }
    }
}
