using System.Security.Cryptography;
using System.Text;
using StdMachine.Core;

namespace StdMachine.Simulation.Demo;

/// <summary>Identifiants déterministes : le projet de démonstration et le scénario s'accordent par clé, sans table.</summary>
public static class DemoIds
{
    public static Guid Of(string key)
    {
        using (var md5 = MD5.Create())
            return new Guid(md5.ComputeHash(Encoding.UTF8.GetBytes("stdmachine-demo:" + key)));
    }
}

/// <summary>
/// Projet de démonstration construit en code (docs/07 § 9.1) : la machine d'exemple MX-4 de docs/01, avec ses
/// six stations, six fonctions (dont deux partagées par Pressage 01 et 02), le process « Presser pignon » et le
/// process général. L'éditeur le modifie en mémoire (annuler / refaire actifs, pas d'enregistrement).
/// </summary>
public static class DemoProject
{
    public const int Chargement = 1, Pressage01 = 2, Pressage02 = 3, Controle = 4, Dechargement = 5, Rebut = 6;

    public static readonly Guid VerdictId = DemoIds.Of("part/verdict");
    public static readonly Guid ForceFinaleId = DemoIds.Of("part/force");

    public static Guid GroupId(string name) => DemoIds.Of("group/" + name);
    public static Guid StationId(int number) => DemoIds.Of("station/" + number);
    public static Guid ObjectId(int station, string key) => DemoIds.Of("obj/" + station + "/" + key);
    public static Guid StepId(int station, string process, string key) => DemoIds.Of("step/" + station + "/" + process + "/" + key);
    public static Guid ProcessId(int station, string key) => DemoIds.Of("proc/" + station + "/" + key);
    public static Guid VarId(int station, string key) => DemoIds.Of("var/" + station + "/" + key);
    public static Guid PosId(int station, string key) => DemoIds.Of("pos/" + station + "/" + key);

    public static MachineProject Build()
    {
        var p = new MachineProject { Id = DemoIds.Of("project"), Name = "Ligne d'assemblage MX-4" };
        BuildMachine(p);
        BuildStations(p);
        BuildGeneralProcess(p);
        return p;
    }

    // ---------------------------------------------------------------- machine

    private static void BuildMachine(MachineProject p)
    {
        var m = p.Machine;
        m.RejectGroupId = GroupId("Rebut");
        m.PartData.Add(new PartDataDef { Id = DemoIds.Of("part/id"), Name = "Identifiant", Type = "Text", Reserved = true });
        m.PartData.Add(new PartDataDef { Id = VerdictId, Name = "Verdict", Type = "Enum", Reserved = true });
        m.PartData.Add(new PartDataDef { Id = ForceFinaleId, Name = "Force finale", Type = "Real", Unit = "N" });
        m.PartData.Add(new PartDataDef { Id = DemoIds.Of("part/hauteur"), Name = "Hauteur pignon", Type = "Real", Unit = "mm" });

        Group(m, "Chargement", GroupRole.Entry, 1, new[] { Chargement }, "Charger");
        Group(m, "Pressage pignon", GroupRole.Work, 2, new[] { Pressage01, Pressage02 }, "Presser pignon", RecipeParam());
        Group(m, "Contrôle", GroupRole.Work, 3, new[] { Controle }, "Contrôler").ImputesRejects = true;
        Group(m, "Sertissage", GroupRole.Work, 4, new[] { Pressage01, Pressage02 }, "Sertir", RecipeParam());
        Group(m, "Déchargement", GroupRole.Exit, 5, new[] { Dechargement }, "Décharger");
        Group(m, "Rebut", GroupRole.Reject, 5, new[] { Rebut }, "Évacuer");

        // Plan 4 x 3 tuiles de 240 mm (960 x 720) : stations en haut et en bas, tampons au centre.
        var st = new[]
        {
            (Chargement, "Chargement", 120.0, 60.0), (Pressage01, "Pressage 01", 360.0, 60.0), (Pressage02, "Pressage 02", 600.0, 60.0),
            (Controle, "Contrôle", 840.0, 60.0), (Dechargement, "Déchargement", 240.0, 660.0), (Rebut, "Rebut", 720.0, 660.0),
        };
        foreach (var (n, name, x, y) in st)
        {
            bool top = y < 360;
            m.PlanarPositions.Add(new PlanarPosition { Id = DemoIds.Of("pp/work/" + n), Kind = PlanarPositionKind.Work, Name = name, X = x, Y = y, StationId = StationId(n) });
            m.PlanarPositions.Add(new PlanarPosition { Id = DemoIds.Of("pp/queue/" + n), Kind = PlanarPositionKind.Queue, Name = "File " + name, X = x, Y = top ? y + 130 : y - 130, StationId = StationId(n) });
        }
        for (int i = 0; i < 4; i++)
            m.PlanarPositions.Add(new PlanarPosition { Id = DemoIds.Of("pp/buffer/" + i), Kind = PlanarPositionKind.Buffer, Name = "Tampon " + (i + 1), X = 240 + 160 * i, Y = 360 });
        m.PlanarPositions.Add(new PlanarPosition { Id = DemoIds.Of("pp/park/1"), Kind = PlanarPositionKind.Park, Name = "Parcage 1", X = 60, Y = 300 });
        m.PlanarPositions.Add(new PlanarPosition { Id = DemoIds.Of("pp/park/2"), Kind = PlanarPositionKind.Park, Name = "Parcage 2", X = 60, Y = 420 });

        m.MachineObjects.Add(Obj(0, "planar", "PlanarSystem", "Système planaire"));
        m.MachineObjects.Add(Obj(0, "beacon", "Beacon", "Balise machine"));
        m.MachineObjects.Add(Obj(0, "safety", "Safety", "Sécurités"));
    }

    private static ParamDef RecipeParam() => new ParamDef { Name = "Recette de presse", Type = "Recipe", Default = 3 };

    private static GroupConfig Group(MachineConfig m, string name, GroupRole role, int priority, int[] stations, string op, ParamDef? param = null)
    {
        var g = new GroupConfig { Id = GroupId(name), Name = name, Role = role, Priority = priority };
        foreach (var s in stations) g.StationIds.Add(StationId(s));
        var sig = new OperationSignature { Name = op };
        if (param != null) sig.Parameters.Add(param);
        g.Operations.Add(sig);
        m.Groups.Add(g);
        return g;
    }

    // ---------------------------------------------------------------- objets

    private static ObjectNode Obj(int station, string key, string type, string name, string? role = null, bool simulated = false)
        => new ObjectNode { Id = ObjectId(station, key), TypeCode = type, Name = name, ChildRole = role, Simulated = simulated };

    private static ObjectNode Robot(int station, string name, bool withC, string tool = "VacuumBlow")
    {
        var r = Obj(station, "robot", "CartesianRobot", name);
        r.Params.Add(new ParamValue { Name = "Hauteur Z de sécurité", Value = "−5,00", Unit = "mm" });
        r.Params.Add(new ParamValue { Name = "Hauteur d'approche par défaut", Value = "10,00", Unit = "mm" });
        r.Children.Add(Obj(station, "axisx", "LinearAxis", "Axe X", "AxisX"));
        r.Children.Add(Obj(station, "axisy", "LinearAxis", "Axe Y", "AxisY"));
        r.Children.Add(Obj(station, "axisz", "LinearAxis", "Axe Z", "AxisZ"));
        if (withC) r.Children.Add(Obj(station, "axisc", "RotaryAxis", "Axe C", "AxisC"));
        r.Children.Add(Obj(station, "tool", tool, tool == "Gripper" ? "Pince" : "Aspiration", "Tool"));
        return r;
    }

    private static NamedPosition Pos(int station, string key, string name, double x, double y, double z, double c, bool taught = true, double? approach = null)
        => new NamedPosition
        {
            Id = PosId(station, key), Name = name, X = x, Y = y, Z = z, C = c, Taught = taught, Approach = approach,
            TaughtAt = taught ? new DateTime(2026, 10, 14, 15, 40, 0) : (DateTime?)null, TaughtBy = taught ? "A. Martin" : null,
        };

    private static void BuildStations(MachineProject p)
    {
        // Chargement
        var ch = NewStation(p, Chargement, "Chargement", 120, 60);
        ch.Objects.Add(Robot(Chargement, "Robot XYZC", true));
        ch.Objects.Add(Obj(Chargement, "vision", "AsyrilEyePlus", "Vision Asyril"));
        ch.Objects.Add(Obj(Chargement, "feeder", "VibratoryFeeder", "Chargeur vibrant"));
        ch.Objects.Add(Obj(Chargement, "reader", "CodeReader", "Lecteur DataMatrix"));
        ch.Variables.Add(new StationVariable { Id = VarId(Chargement, "pos"), Name = "PosPlatine", Type = "Position" });
        AddPositions(ch, Chargement, true);
        BuildChargerProcess(ch);

        // Pressage 01 et 02 : même configuration
        foreach (var (num, name, x) in new[] { (Pressage01, "Pressage 01", 360.0), (Pressage02, "Pressage 02", 600.0) })
        {
            var s = NewStation(p, num, name, x, 60);
            s.Objects.Add(Robot(num, "Robot XYZC", true));
            s.Objects.Add(Obj(num, "vision", "AsyrilEyePlus", "Vision Asyril"));
            s.Objects.Add(Obj(num, "feeder", "VibratoryFeeder", "Chargeur vibrant"));
            s.Objects.Add(Obj(num, "press", "Press", "Presse"));
            s.Objects.Add(Obj(num, "nest", "DigitalInput", "Présence nid presse"));
            s.Objects.Add(Obj(num, "counter", "Counter", "Compteur de rebuts"));
            s.Variables.Add(new StationVariable { Id = VarId(num, "pospignon"), Name = "PosPignon", Type = "Position" });
            s.Variables.Add(new StationVariable { Id = VarId(num, "jugement"), Name = "JugementPresse", Type = "Number" });
            AddPositions(s, num, true, poseTaught: num != Pressage01);
            BuildPressageProcesses(s, p);
            // Fonction personnalisée d'exemple : copie de « Prendre pièce » à vitesse lente par défaut (docs/02 § 3)
            var robotNode = s.FindObject(ObjectId(num, "robot"))!;
            var douce = DemoFunctions.CopyOf(s, robotNode, DemoCatalog.Get("CartesianRobot")!.Function("PickPart")!, "fn/" + num + "/douce");
            douce.Id = DemoIds.Of("fn/" + num + "/douce");
            douce.Name = "Prendre pièce douce";
            douce.BasedOn = "Prendre pièce";
            douce.Description = "Approche lente, aspiration avant la descente finale ; pour les pièces fragiles.";
            douce.Params.First(q => q.Name == "Vitesse").Default = 1;
            s.CustomFunctions.Add(douce);
        }

        // Contrôle
        var co = NewStation(p, Controle, "Contrôle", 840, 60);
        co.Objects.Add(Obj(Controle, "cyl", "Cylinder", "Descente palpeur"));
        co.Objects.Add(Obj(Controle, "gt2", "KeyenceGT2", "Palpeur hauteur"));
        co.Objects.Add(Obj(Controle, "iv", "KeyenceIV", "Présence pignon"));
        BuildControlerProcess(co);

        // Déchargement
        var de = NewStation(p, Dechargement, "Déchargement", 240, 660);
        de.Objects.Add(Robot(Dechargement, "Robot XYZ", false, "Gripper"));
        de.Objects.Add(Obj(Dechargement, "magazine", "Magazine", "Magasin plateaux sortie"));
        AddPositions(de, Dechargement, false);
        BuildDechargerProcess(de);

        // Rebut
        var re = NewStation(p, Rebut, "Rebut", 720, 660);
        re.Objects.Add(Obj(Rebut, "ejector", "Cylinder", "Éjecteur"));
        re.Objects.Add(Obj(Rebut, "full", "DigitalInput", "Bac rebut plein"));
        BuildEvacuerProcess(re);

        // Positions et mises en page de la vue de dessus
        foreach (var s in p.Stations) Layout(s);
    }

    private static StationConfig NewStation(MachineProject p, int number, string name, double x, double y)
    {
        var s = new StationConfig { Id = StationId(number), Name = name, Number = number, PlanX = x, PlanY = y };
        p.Stations.Add(s);
        return s;
    }

    private static void AddPositions(StationConfig s, int n, bool withRebut, bool poseTaught = true)
    {
        s.Positions.Add(Pos(n, "repos", "Repos", 0, 0, 0, 0));
        s.Positions.Add(Pos(n, "feed", "Zone alimenteur", 210, 120, -24.8, 0, true, 10));
        s.Positions.Add(Pos(n, "nid", "Dépose nid", 150, 60, -18.2, 0, true, 10));
        s.Positions.Add(Pos(n, "presse", "Pose presse", 95, 140, -31.6, 45, poseTaught, 12));
        s.Positions.Add(Pos(n, "mover", "Mover", 268.4, 92.15, -31.6, 0, true, 10));
        if (withRebut) s.Positions.Add(Pos(n, "bac", "Bac rebut", 300, 20, -12, 0));
    }

    private static void Layout(StationConfig s)
    {
        double x = 40, y = 40;
        foreach (var o in s.Objects)
        {
            o.LayoutX = x;
            o.LayoutY = y;
            x += 200;
            if (x > 560) { x = 40; y += 150; }
        }
    }

    // ---------------------------------------------------------------- étapes

    private static ActionStep Act(int station, string process, string key, string objKey, int action, params ValueRef[] args)
    {
        var a = new ActionStep { Id = StepId(station, process, key), ObjectId = ObjectId(station, objKey), ActionNo = action };
        a.Args.AddRange(args);
        return a;
    }

    private static FunctionStep Fn(int station, string process, string key, string function, params ValueRef[] args)
    {
        var f = new FunctionStep { Id = StepId(station, process, key), ModuleObjectId = ObjectId(station, "robot"), FunctionId = function };
        f.Args.AddRange(args);
        return f;
    }

    private static ValueRef Pn(int station, string key, double approach = 0) => ValueRef.NamedPos(PosId(station, key), approach);

    private static Condition Cond(string text, params CondTerm[] terms)
    {
        var c = new Condition { Combine = CombineMode.All };
        c.Terms.AddRange(terms);
        if (terms.Length > 0 && string.IsNullOrEmpty(terms[0].Text)) terms[0].Text = text;
        return c;
    }

    private static CondTerm Term(ValueRef left, CompareOp op, ValueRef right, string text)
        => new CondTerm { Left = left, Op = op, Right = right, Text = text };

    private static void BuildChargerProcess(StationConfig s)
    {
        const string proc = "charger";
        var pr = new ProcessDefinition { Id = ProcessId(s.Number, proc), Kind = ProcessKind.Operation, Name = "Charger" };
        pr.Implements.Add(new OperationRef(GroupId("Chargement"), 1));
        var chercher = Act(s.Number, proc, "1", "vision", 3);
        chercher.Fail = new FailPolicy(FailKind.Retry, 3);
        pr.Body.Steps.Add(chercher);
        pr.Body.Steps.Add(Act(s.Number, proc, "2", "vision", 4));
        pr.Body.Steps.Add(Fn(s.Number, proc, "3", "PickPart", ValueRef.Var(VarId(s.Number, "pos")), ValueRef.Const(10), ValueRef.Speed(2)));
        pr.Body.Steps.Add(Fn(s.Number, proc, "4", "PlacePart", Pn(s.Number, "mover", 10), ValueRef.Const(10), ValueRef.Const(1), ValueRef.Speed(2)));
        pr.Body.Steps.Add(Act(s.Number, proc, "5", "reader", 1));
        s.Processes.Add(pr);
        AddUtilityProcesses(s);
    }

    private static void AddUtilityProcesses(StationConfig s)
    {
        s.Processes.Add(new ProcessDefinition { Id = ProcessId(s.Number, "init"), Kind = ProcessKind.Init, Name = "Initialisation" });
        var vid = new ProcessDefinition { Id = ProcessId(s.Number, "clear"), Kind = ProcessKind.Clear, Name = "Vidage" };
        if (s.FindObject(ObjectId(s.Number, "robot")) != null)
            vid.Body.Steps.Add(Fn(s.Number, "clear", "1", "Retract"));
        s.Processes.Add(vid);
    }

    private static void BuildPressageProcesses(StationConfig s, MachineProject p)
    {
        int n = s.Number;
        const string pp = "presser";
        var pr = new ProcessDefinition { Id = ProcessId(n, pp), Kind = ProcessKind.Operation, Name = "Presser pignon" };
        pr.Implements.Add(new OperationRef(GroupId("Pressage pignon"), 1));

        var chercher = Act(n, pp, "chercher", "vision", 3);
        chercher.Fail = new FailPolicy(FailKind.Retry, 3);
        var obtenir = Act(n, pp, "obtenir", "vision", 4);
        pr.Body.Steps.Add(chercher);
        pr.Body.Steps.Add(obtenir);
        pr.Body.Steps.Add(Fn(n, pp, "prendre1", "PickPart", ValueRef.Var(VarId(n, "pospignon")), ValueRef.Const(10), ValueRef.Speed(2)));
        pr.Body.Steps.Add(Fn(n, pp, "poser1", "PlacePart", Pn(n, "nid", 10), ValueRef.Const(10), ValueRef.Const(1), ValueRef.Speed(2)));
        var attendre = new WaitConditionStep { Id = StepId(n, pp, "attendre"), TimeoutSeconds = 5 };
        attendre.Condition.Terms.Add(Term(ValueRef.ObjValue(ObjectId(n, "nest"), 1), CompareOp.Equal, ValueRef.Const(1), "Pièce présente sur le nid"));
        pr.Body.Steps.Add(attendre);
        pr.Body.Steps.Add(Fn(n, pp, "prendre2", "PickPart", Pn(n, "mover", 10), ValueRef.Const(10), ValueRef.Speed(2)));
        pr.Body.Steps.Add(Fn(n, pp, "poser2", "PlacePart", Pn(n, "presse", 12), ValueRef.Const(12), ValueRef.Const(1), ValueRef.Speed(1)));

        // En parallèle : recette de presse, chercher le pignon suivant, dégager le robot
        var par = new ParallelBlock { Id = StepId(n, pp, "par") };
        par.Branches.Clear();
        var recette = Act(n, pp, "recette", "press", 1, ValueRef.OpParam(1));
        var suivant = Act(n, pp, "suivant", "vision", 3);
        suivant.Fail = new FailPolicy(FailKind.Retry, 3);
        var repos = Fn(n, pp, "repos", "MoveTo", Pn(n, "repos"), ValueRef.Speed(2));
        par.Branches.Add(new Sequence(new Step[] { recette }));
        par.Branches.Add(new Sequence(new Step[] { suivant }));
        par.Branches.Add(new Sequence(new Step[] { repos }));
        pr.Body.Steps.Add(par);

        // Si le jugement est bon et la force finale entre 800 et 900 N : bonne pièce, sinon rebut
        var iff = new IfBlock { Id = StepId(n, pp, "if") };
        iff.Condition.Combine = CombineMode.All;
        iff.Condition.Terms.Add(Term(ValueRef.Var(VarId(n, "jugement")), CompareOp.Equal, ValueRef.Const(1), "Jugement de la presse = Bon"));
        iff.Condition.Terms.Add(Term(ValueRef.PartData(ForceFinaleId), CompareOp.GreaterOrEqual, ValueRef.Const(800), "Force finale ≥ 800 N"));
        iff.Condition.Terms.Add(Term(ValueRef.PartData(ForceFinaleId), CompareOp.LessOrEqual, ValueRef.Const(900), "Force finale ≤ 900 N"));
        iff.ElseLabel = "Sinon (rebut)";
        iff.Then.Steps.Add(Fn(n, pp, "prendre3", "PickPart", Pn(n, "presse", 12), ValueRef.Const(12), ValueRef.Speed(2)));
        iff.Then.Steps.Add(Fn(n, pp, "poser3", "PlacePart", Pn(n, "mover", 10), ValueRef.Const(10), ValueRef.Const(1), ValueRef.Speed(2)));
        iff.Then.Steps.Add(new SetStep { Id = StepId(n, pp, "bon"), Target = SetTarget.Verdict, Operation = SetOperation.Assign, Value = ValueRef.Const(1), Label = "Verdict bon" });
        iff.Else.Steps.Add(Fn(n, pp, "prendre4", "PickPart", Pn(n, "presse", 12), ValueRef.Const(12), ValueRef.Speed(2)));
        iff.Else.Steps.Add(Fn(n, pp, "poser4", "PlacePart", Pn(n, "bac", 10), ValueRef.Const(10), ValueRef.Const(0), ValueRef.Speed(2)));
        iff.Else.Steps.Add(Act(n, pp, "compter", "counter", 1));
        iff.Else.Steps.Add(new SetStep { Id = StepId(n, pp, "mauvais"), Target = SetTarget.Verdict, Operation = SetOperation.Assign, Value = ValueRef.Const(2), Label = "Verdict mauvais" });
        iff.Else.Steps.Add(new SetStep { Id = StepId(n, pp, "retiree"), Target = SetTarget.PartRemoved, Operation = SetOperation.Assign, Value = ValueRef.Const(1), Label = "Pièce retirée" });
        pr.Body.Steps.Add(iff);
        s.Processes.Add(pr);

        // Sertir
        const string se = "sertir";
        var sertir = new ProcessDefinition { Id = ProcessId(n, se), Kind = ProcessKind.Operation, Name = "Sertir" };
        sertir.Implements.Add(new OperationRef(GroupId("Sertissage"), 1));
        sertir.Body.Steps.Add(Fn(n, se, "1", "PickPart", Pn(n, "mover", 10), ValueRef.Const(10), ValueRef.Speed(2)));
        sertir.Body.Steps.Add(Fn(n, se, "2", "PlacePart", Pn(n, "presse", 12), ValueRef.Const(12), ValueRef.Const(1), ValueRef.Speed(1)));
        sertir.Body.Steps.Add(Act(n, se, "3", "press", 1, ValueRef.OpParam(1)));
        sertir.Body.Steps.Add(Fn(n, se, "4", "PickPart", Pn(n, "presse", 12), ValueRef.Const(12), ValueRef.Speed(2)));
        sertir.Body.Steps.Add(Fn(n, se, "5", "PlacePart", Pn(n, "mover", 10), ValueRef.Const(10), ValueRef.Const(1), ValueRef.Speed(2)));
        s.Processes.Add(sertir);

        // Cycle de fond : préparer le pignon suivant
        var fond = new ProcessDefinition { Id = ProcessId(n, "fond"), Kind = ProcessKind.Background, Name = "Cycle de fond" };
        fond.Body.Steps.Add(Act(n, "fond", "1", "feeder", 1, ValueRef.Const(1), ValueRef.Const(300)));
        fond.Body.Steps.Add(Act(n, "fond", "2", "vision", 3));
        s.Processes.Add(fond);
        AddUtilityProcesses(s);
    }

    private static void BuildControlerProcess(StationConfig s)
    {
        const string proc = "controler";
        var pr = new ProcessDefinition { Id = ProcessId(s.Number, proc), Kind = ProcessKind.Operation, Name = "Contrôler" };
        pr.Implements.Add(new OperationRef(GroupId("Contrôle"), 1));
        pr.Body.Steps.Add(Act(s.Number, proc, "1", "cyl", 1));
        pr.Body.Steps.Add(Act(s.Number, proc, "2", "gt2", 1));
        pr.Body.Steps.Add(Act(s.Number, proc, "3", "cyl", 2));
        s.Processes.Add(pr);
        AddUtilityProcesses(s);
    }

    private static void BuildDechargerProcess(StationConfig s)
    {
        const string proc = "decharger";
        var pr = new ProcessDefinition { Id = ProcessId(s.Number, proc), Kind = ProcessKind.Operation, Name = "Décharger" };
        pr.Implements.Add(new OperationRef(GroupId("Déchargement"), 1));
        pr.Body.Steps.Add(Fn(s.Number, proc, "1", "PickPart", Pn(s.Number, "mover", 10), ValueRef.Const(10), ValueRef.Speed(2)));
        pr.Body.Steps.Add(Act(s.Number, proc, "2", "magazine", 3));
        pr.Body.Steps.Add(Fn(s.Number, proc, "3", "PlacePart", Pn(s.Number, "feed", 10), ValueRef.Const(10), ValueRef.Const(1), ValueRef.Speed(2)));
        pr.Body.Steps.Add(Act(s.Number, proc, "4", "magazine", 4));
        s.Processes.Add(pr);
        AddUtilityProcesses(s);
    }

    private static void BuildEvacuerProcess(StationConfig s)
    {
        const string proc = "evacuer";
        var pr = new ProcessDefinition { Id = ProcessId(s.Number, proc), Kind = ProcessKind.Operation, Name = "Évacuer" };
        pr.Implements.Add(new OperationRef(GroupId("Rebut"), 1));
        pr.Body.Steps.Add(Act(s.Number, proc, "1", "ejector", 1));
        pr.Body.Steps.Add(Act(s.Number, proc, "2", "ejector", 2));
        s.Processes.Add(pr);
        AddUtilityProcesses(s);
    }

    // ---------------------------------------------------------------- process général

    private static void BuildGeneralProcess(MachineProject p)
    {
        var g = new ProcessDefinition { Id = DemoIds.Of("process/general"), Kind = ProcessKind.General, Name = "Process général" };
        RouteStep Route(string key, string group, params ValueRef[] args)
        {
            var r = new RouteStep { Id = DemoIds.Of("gstep/" + key), GroupId = GroupId(group), OperationNo = 1 };
            r.Args.AddRange(args);
            return r;
        }
        Condition Bon() => Cond("Verdict = Bon", Term(ValueRef.PartData(VerdictId), CompareOp.Equal, ValueRef.Const(1), "Verdict = Bon"));

        g.Body.Steps.Add(Route("charger", "Chargement"));
        g.Body.Steps.Add(Route("presser", "Pressage pignon", ValueRef.Const(3)));
        var if1 = new IfBlock { Id = DemoIds.Of("gstep/if1"), Condition = Bon() };
        if1.Then.Steps.Add(Route("controler", "Contrôle"));
        g.Body.Steps.Add(if1);
        var if2 = new IfBlock { Id = DemoIds.Of("gstep/if2"), Condition = Bon() };
        if2.Then.Steps.Add(Route("sertir", "Sertissage", ValueRef.Const(5)));
        g.Body.Steps.Add(if2);
        var if3 = new IfBlock { Id = DemoIds.Of("gstep/if3"), Condition = Bon(), ElseLabel = "Sinon (rebut)" };
        if3.Then.Steps.Add(Route("decharger", "Déchargement"));
        if3.Else.Steps.Add(Route("evacuer", "Rebut"));
        g.Body.Steps.Add(if3);
        g.Body.Steps.Add(new EndStep { Id = DemoIds.Of("gstep/end") });
        p.GeneralProcess = g;
    }
}
