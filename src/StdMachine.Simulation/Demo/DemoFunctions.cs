using StdMachine.Core;

namespace StdMachine.Simulation.Demo;

/// <summary>
/// Fonctions personnalisées de la maquette : recherche d'une fonction (catalogue ou personnalisée), copie d'une fonction de module
/// en suite d'actions de base modifiable, création d'une fonction vide. Le vrai catalogue (phase 2) décrira les corps de fonction en JSON.
/// </summary>
public static class DemoFunctions
{
    public static FunctionDef? FindFunction(StationConfig? station, Guid moduleId, string functionId)
    {
        var node = station?.FindObject(moduleId);
        if (station == null || node == null) return null;
        if (functionId.StartsWith("custom:", StringComparison.Ordinal))
        {
            var c = station.CustomFunctions.FirstOrDefault(x => x.FunctionId == functionId);
            return c == null ? null : FromCustom(c, station);
        }
        return DemoCatalog.Get(node.TypeCode)?.Function(functionId);
    }

    public static FunctionDef FromCustom(CustomFunction c, StationConfig station)
        => new FunctionDef(c.FunctionId, c.Name, c.Params, c.Body.Steps.Sum(s => DemoCatalog.EstimatedSeconds(s, station)),
            c.Body.Steps.Select(s => DemoText.Title(s, station)).ToArray());

    /// <summary>Copie d'une fonction de module en fonction personnalisée : mêmes paramètres, suite d'actions de base équivalente.</summary>
    public static CustomFunction CopyOf(StationConfig station, ObjectNode module, FunctionDef def, string? idSeed = null)
    {
        var c = new CustomFunction { ModuleObjectId = module.Id, Name = def.Name + " (copie)", BasedOn = def.Name, Description = "Copie de la fonction « " + def.Name + " » du module." };
        foreach (var p in def.Params)
            c.Params.Add(new ParamDef { Id = idSeed == null ? Guid.NewGuid() : DemoIds.Of(idSeed + "/" + p.Name), Name = p.Name, Type = p.Type, Unit = p.Unit, Default = p.Default, Min = p.Min, Max = p.Max, Values = p.Values });
        c.Body = Seed(station, module, def.Id, c);
        if (idSeed != null)
            for (int i = 0; i < c.Body.Steps.Count; i++) c.Body.Steps[i].Id = DemoIds.Of(idSeed + "/step/" + i);
        return c;
    }

    public static CustomFunction NewEmpty(StationConfig station, ObjectNode module)
    {
        string name = "Nouvelle fonction";
        int n = 1;
        while (station.CustomFunctions.Any(f => f.Name == name)) name = "Nouvelle fonction " + (++n);
        return new CustomFunction { ModuleObjectId = module.Id, Name = name };
    }

    private static ObjectNode? Child(ObjectNode module, string role) => module.Children.FirstOrDefault(c => c.ChildRole == role);

    private static ParamDef? ParamNamed(CustomFunction c, string name) => c.Params.FirstOrDefault(p => p.Name == name);

    private static Sequence Seed(StationConfig station, ObjectNode module, string functionId, CustomFunction c)
    {
        var seq = new Sequence();
        var z = Child(module, "AxisZ"); var x = Child(module, "AxisX"); var y = Child(module, "AxisY");
        var tool = Child(module, "Tool"); var rot = Child(module, "AxisC");
        var rest = station.Positions.FirstOrDefault(p => p.Name == "Repos");
        var pos = ParamNamed(c, "Position"); var speed = ParamNamed(c, "Vitesse"); var approach = ParamNamed(c, "Approche");
        ValueRef Spd() => speed != null ? ValueRef.FuncParam(speed.Id) : ValueRef.Speed(2);
        ValueRef Target(ParamDef? plus = null) => pos != null ? ValueRef.FuncParam(pos.Id, 0, plus?.Id) : ValueRef.Const(0);
        ValueRef Safe() => rest != null ? ValueRef.NamedPos(rest.Id) : ValueRef.Const(0);
        void Move(ObjectNode? axis, ValueRef target)
        {
            if (axis == null) return;
            var a = new ActionStep { ObjectId = axis.Id, ActionNo = 4 };
            a.Args.Add(target); a.Args.Add(Spd());
            seq.Steps.Add(a);
        }
        void Tool(int action, params ValueRef[] args)
        {
            if (tool == null) return;
            var a = new ActionStep { ObjectId = tool.Id, ActionNo = action };
            a.Args.AddRange(args);
            seq.Steps.Add(a);
        }

        switch (functionId)
        {
            case "MoveTo":
                Move(z, Safe()); Move(x, Target()); Move(y, Target()); if (rot != null) Move(rot, Target()); Move(z, Target());
                break;
            case "PickPart":
                Move(z, Safe()); Move(x, Target()); Move(y, Target());
                Move(z, Target(approach));
                Tool(1, ValueRef.Const(1));
                Move(z, Target());
                Tool(4);
                Move(z, Safe());
                break;
            case "PlacePart":
                Move(z, Safe()); Move(x, Target()); Move(y, Target());
                Move(z, Target(approach));
                Tool(3);
                Tool(2, ValueRef.Const(50));
                Move(z, Safe());
                break;
            case "Retract":
                Move(z, Safe());
                break;
            default:
                foreach (var axis in new[] { z, x, y, rot })
                    if (axis != null) seq.Steps.Add(new ActionStep { ObjectId = axis.Id, ActionNo = 3 });
                break;
        }
        return seq;
    }
}
