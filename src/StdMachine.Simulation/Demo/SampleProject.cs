using System.Globalization;
using StdMachine.Core;
using StdMachine.Core.Catalog;

namespace StdMachine.Simulation.Demo;

/// <summary>
/// Projet d'exemple complet MX-4 (<c>samples/MX-4.stdm.json</c>) : le projet de démonstration complété selon le catalogue
/// (paramètres numérotés, raccordements de l'image E/S, pools matériels, 6 movers, recettes de presse, verrouillages générés,
/// toutes les positions apprises). Le fichier JSON est produit par ce code ; un test vérifie qu'il est à jour.
/// </summary>
public static class SampleProject
{
    private static readonly DateTime Stamp = new DateTime(2026, 10, 16, 8, 0, 0, DateTimeKind.Utc);

    // Signaux raccordés en plus des signaux obligatoires (types dont les signaux sont tous sans condition).
    private static readonly Dictionary<string, int[]> ExtraSignals = new Dictionary<string, int[]>
    {
        ["VacuumBlow"] = new[] { 2, 3 },
        ["Cylinder"] = new[] { 3, 4 },
        ["Safety"] = new[] { 1, 2, 3, 4 },
        ["Magazine"] = new[] { 1, 2, 3, 4, 5 },
        ["Gripper"] = new[] { 2, 3, 4, 5 },
        ["Beacon"] = new[] { 1, 2, 3, 4, 5 },
        ["VibratoryFeeder"] = new[] { 1 },
    };

    public static MachineProject Build(CatalogDocument catalog)
    {
        var p = DemoProject.Build();
        p.CreatedAt = Stamp;
        p.ModifiedAt = Stamp;
        p.CatalogVersion = catalog.CatalogVersion;

        foreach (var s in p.Stations)
        {
            foreach (var pos in s.Positions) { pos.Taught = true; pos.TaughtAt = new DateTime(2026, 10, 14, 15, 40, 0); pos.TaughtBy = "A. Martin"; }
            // La position Repos du robot est à la hauteur Z de sécurité (V108).
            if (s.AllObjects().Any(o => o.TypeCode == "CartesianRobot"))
                foreach (var repos in s.Positions.Where(x => x.Name == "Repos")) repos.Z = -5;
        }

        for (int i = 1; i <= p.Machine.Planar.Movers; i++)
        {
            var mover = new ObjectNode { Id = DemoIds.Of("obj/0/mover" + i), TypeCode = "PlanarMover", Name = "Mover " + i };
            p.Machine.MachineObjects.Add(mover);
        }

        var nodes = new List<(string Owner, ObjectNode Node)>();
        foreach (var o in p.Machine.MachineObjects.SelectMany(o => o.SelfAndDescendants())) nodes.Add(("Machine", o));
        foreach (var s in p.Stations)
            foreach (var o in s.AllObjects()) nodes.Add((s.Name, o));

        var counters = new Dictionary<string, int>();
        int Next(string key) { counters.TryGetValue(key, out int n); counters[key] = n + 1; return n; }
        foreach (var (owner, node) in nodes)
        {
            var type = catalog.Type(node.TypeCode);
            if (type != null) Complete(p, owner, node, type, Next);
            else if (catalog.Modules.Any(m => m.Code == node.TypeCode)) CompleteModule(catalog, node);
        }

        AddPressRecipes(p);
        AddGeneratedInterlocks(p);
        return p;
    }

    private static void Complete(MachineProject p, string owner, ObjectNode node, CatalogType type, Func<string, int> next)
    {
        node.Params.Clear();
        foreach (var cp in type.Params)
            node.Params.Add(new ParamValue
            {
                No = cp.No, Name = cp.Name, Number = cp.Default, Unit = cp.Unit,
                Value = cp.Default.ToString("0.###", CultureInfo.InvariantCulture),
            });

        node.Speeds.Clear();
        if (type.Id == 11 || type.Id == 12)
        {
            node.Speeds.Add(new SpeedDef(1, "Lente", 10));
            node.Speeds.Add(new SpeedDef(2, "Rapide", 80));
        }

        // Signaux : obligatoires + extras, sans condition de visibilité.
        node.Signals.Clear();
        ExtraSignals.TryGetValue(type.Code, out var extra);
        foreach (var sig in type.Signals.Where(s => s.VisibleWhen == null && (s.Required || (extra != null && extra.Contains(s.No)))))
        {
            var kind = (IoKind)Enum.Parse(typeof(IoKind), sig.Kind);
            int index = next("io/" + sig.Kind);
            var ch = new IoChannel
            {
                Id = DemoIds.Of("io/" + node.Id.ToString("N") + "/" + sig.No), Kind = kind, Index = index,
                Name = owner + " · " + node.Name + " · " + sig.Name, Terminal = "T" + (index + 1),
            };
            p.Machine.IoImage.Add(ch);
            node.Signals.Add(new SignalBinding(sig.No, ch.Id));
        }

        if (type.Hardware is { Required: true, VisibleWhen: null } hw)
        {
            var kind = (HardwareKind)Enum.Parse(typeof(HardwareKind), hw.Kind);
            // Un mover occupe son propre élément du groupe planaire, le système planaire est l'élément 1 du groupe.
            string pool = type.Code == "PlanarMover" ? "Planar/Mover" : kind.ToString();
            int index = type.Code == "PlanarSystem" ? 1 : next("hw/" + pool) + 1;
            node.DeviceIndex = index;
            p.Machine.HardwarePools.Add(new HardwareItem { Id = DemoIds.Of("hw/" + node.Id.ToString("N")), Kind = kind, Index = index, Name = owner + " · " + node.Name });
        }
    }

    private static void CompleteModule(CatalogDocument catalog, ObjectNode node)
    {
        var module = catalog.Modules.First(m => m.Code == node.TypeCode);
        node.Params.Clear();
        foreach (var cp in module.Params)
            node.Params.Add(new ParamValue { No = cp.No, Name = cp.Name, Number = cp.Default, Unit = cp.Unit, Value = cp.Default.ToString("0.###", CultureInfo.InvariantCulture) });
    }

    private static void AddPressRecipes(MachineProject p)
    {
        foreach (var s in p.Stations)
        {
            var press = s.AllObjects().FirstOrDefault(o => o.TypeCode == "Press");
            if (press == null) continue;
            s.PressRecipes.Add(Recipe(press, 3, "Pressage pignon", 800, 900, 4.2));
            s.PressRecipes.Add(Recipe(press, 5, "Sertissage", 600, 700, 2.0));
        }
    }

    private static PressRecipeConfig Recipe(ObjectNode press, int no, string name, double fMin, double fMax, double stroke)
    {
        var r = new PressRecipeConfig { Id = DemoIds.Of("recipe/" + press.Id.ToString("N") + "/" + no), ObjectId = press.Id, No = no, Name = name, Verified = true };
        r.Segments.Add(new PressSegmentConfig { Target = PressTarget.Stroke, TargetValue = stroke - 1, SpeedPercent = 80, StrokeMin = 0, StrokeMax = stroke });
        r.Segments.Add(new PressSegmentConfig { Target = PressTarget.Force, TargetValue = (fMin + fMax) / 2, SpeedPercent = 20, ForceMin = fMin, ForceMax = fMax, StrokeMin = stroke - 0.5, StrokeMax = stroke + 0.5 });
        return r;
    }

    /// <summary>Verrouillage généré du robot cartésien : X, Y et C ne bougent que si Z ≥ hauteur de sécurité − fenêtre (docs/03 § 9.2).</summary>
    private static void AddGeneratedInterlocks(MachineProject p)
    {
        foreach (var s in p.Stations)
            foreach (var robot in s.AllObjects().Where(o => o.TypeCode == "CartesianRobot"))
            {
                var z = robot.Children.First(c => c.ChildRole == "AxisZ");
                double safeZ = robot.Params.First(x => x.No == 1).Number ?? -5;
                double window = z.Params.FirstOrDefault(x => x.No == 4)?.Number ?? 0.02;
                foreach (var role in new[] { "AxisX", "AxisY", "AxisC" })
                {
                    var axis = robot.Children.FirstOrDefault(c => c.ChildRole == role);
                    if (axis == null) continue;
                    var il = new Interlock { Id = DemoIds.Of("il/" + axis.Id.ToString("N")), ObjectId = axis.Id, AllMotion = true, Generated = true };
                    il.Condition.Terms.Add(new CondTerm { Left = ValueRef.ObjValue(z.Id, 1), Op = CompareOp.GreaterOrEqual, Right = ValueRef.Const(Math.Round(safeZ - window, 4)), Text = "Z au-dessus de la hauteur de sécurité" });
                    s.Interlocks.Add(il);
                }
            }
    }

    /// <summary>Scénario <c>validation-errors</c> : une position non apprise (V001) et un objet utilisé dans deux branches parallèles (V004).</summary>
    public static MachineProject BuildValidationErrors(CatalogDocument catalog)
    {
        var p = Build(catalog);
        p.Name += " (erreurs de validation)";
        var s = p.Station(DemoProject.Pressage01)!;
        s.Positions.First(x => x.Name == "Pose presse").Taught = false;
        var proc = s.Processes.First(x => x.Name == "Presser pignon");
        var par = proc.AllSteps().OfType<ParallelBlock>().First();
        var vision = s.AllObjects().First(o => o.TypeCode == "AsyrilEyePlus");
        // La vision est déjà utilisée par la branche « pignon suivant » : on l'ajoute aussi à la branche de dégagement.
        par.Branches[par.Branches.Count - 1].Steps.Add(new ActionStep { Id = DemoIds.Of("step/validation-errors/vision"), ObjectId = vision.Id, ActionNo = 3 });
        return p;
    }
}
