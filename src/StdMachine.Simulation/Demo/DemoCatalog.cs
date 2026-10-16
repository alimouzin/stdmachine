using StdMachine.Core;

namespace StdMachine.Simulation.Demo;

public sealed record ActionDef(int No, string Name, string Icon, IReadOnlyList<ParamDef> Params, double DurationSeconds, bool Motion = false, string[]? Results = null);

public sealed record FunctionDef(string Id, string Name, IReadOnlyList<ParamDef> Params, double DurationSeconds, string[] Inner);

public sealed record TypeDef(string Code, string Name, string Icon, string Category, IReadOnlyList<ActionDef> Actions, IReadOnlyList<FunctionDef> Functions, bool IsModule = false)
{
    public ActionDef? Action(int no) => Actions.FirstOrDefault(a => a.No == no);
    public FunctionDef? Function(string id) => Functions.FirstOrDefault(f => f.Id == id);
}

/// <summary>
/// Catalogue allégé de la maquette (phase 1) : seulement les types et actions utilisés par la machine d'exemple MX-4.
/// Le catalogue complet de docs/03 (catalog.json) arrive en phase 2 ; les numéros d'actions suivent déjà docs/03.
/// </summary>
public static class DemoCatalog
{
    private static ParamDef P(string name, string type, string? unit = null, double def = 0, double? min = null, double? max = null, params string[] values)
        => new ParamDef { Name = name, Type = type, Unit = unit, Default = def, Min = min, Max = max, Values = values.Length == 0 ? null : values };

    private static readonly ParamDef[] None = new ParamDef[0];

    public static readonly IReadOnlyList<TypeDef> Types = new List<TypeDef>
    {
        new TypeDef("CartesianRobot", "Robot cartésien XYZC", "precision_manufacturing", "Modules",
            new ActionDef[0],
            new[]
            {
                new FunctionDef("MoveTo", "Aller à", new[] { P("Position", "Position"), P("Vitesse", "Speed", null, 2) }, 0.9, new[] { "Z → sécurité", "X, Y, C en parallèle", "Z → position" }),
                new FunctionDef("PickPart", "Prendre pièce", new[] { P("Position", "Position"), P("Approche", "Real", "mm", 10, 0, 50), P("Vitesse", "Speed", null, 2) }, 1.6,
                    new[] { "Aller au-dessus", "Approche", "Saisie", "Dégagement" }),
                new FunctionDef("PlacePart", "Poser pièce", new[] { P("Position", "Position"), P("Approche", "Real", "mm", 10, 0, 50), P("Soufflage", "Bool", null, 1), P("Vitesse", "Speed", null, 2) }, 1.5,
                    new[] { "Aller au-dessus", "Approche", "Dépose", "Dégagement" }),
                new FunctionDef("Retract", "Dégager", None, 0.3, new[] { "Z → sécurité" }),
                new FunctionDef("Reference", "Référencer", None, 5.0, new[] { "Power on des axes", "Référencement Z", "Référencement X, Y", "Référencement C" }),
            }, true),
        new TypeDef("LinearAxis", "Axe linéaire", "straighten", "Mouvement",
            new[]
            {
                new ActionDef(1, "Power on", "bolt", None, 0.5),
                new ActionDef(3, "Init", "home", None, 6.0, true),
                new ActionDef(4, "Move absolu", "open_with", new[] { P("Position", "Position"), P("Vitesse", "Speed", null, 2) }, 1.2, true, new[] { "Position atteinte" }),
                new ActionDef(5, "Move relatif", "open_with", new[] { P("Distance", "Real", "mm", 0), P("Vitesse", "Speed", null, 2) }, 1.0, true),
                new ActionDef(6, "Stop", "stop", None, 0.3),
                new ActionDef(8, "Lire position", "visibility", None, 0.0, false, new[] { "Position" }),
            }, new FunctionDef[0]),
        new TypeDef("RotaryAxis", "Axe rotatif", "rotate_right", "Mouvement",
            new[]
            {
                new ActionDef(1, "Power on", "bolt", None, 0.5),
                new ActionDef(3, "Init", "home", None, 4.0, true),
                new ActionDef(4, "Move absolu", "open_with", new[] { P("Position", "Position"), P("Vitesse", "Speed", null, 2) }, 0.8, true),
            }, new FunctionDef[0]),
        new TypeDef("VacuumBlow", "Aspiration et soufflage", "air", "Préhension",
            new[]
            {
                new ActionDef(1, "Aspirer", "air", new[] { P("Attendre le vide", "Bool", null, 1) }, 0.6, false, new[] { "Pièce présente" }),
                new ActionDef(2, "Souffler", "air", new[] { P("Durée", "Duration", "ms", 50) }, 0.1),
                new ActionDef(3, "Couper", "block", None, 0.0),
                new ActionDef(4, "Vérifier présence pièce", "check_circle", None, 0.0, false, new[] { "Pièce présente" }),
            }, new FunctionDef[0]),
        new TypeDef("AsyrilEyePlus", "Vision Asyril Eye+", "center_focus_strong", "Capteurs et mesure",
            new[]
            {
                new ActionDef(1, "Init", "home", None, 4.0),
                new ActionDef(2, "Sélectionner recette", "list_alt", new[] { P("Recette de vision", "Recipe", null, 1) }, 0.3),
                new ActionDef(3, "Chercher pièce", "search", None, 0.5, false, new[] { "Pièces trouvées" }),
                new ActionDef(4, "Obtenir pièce", "gps_fixed", None, 0.1, false, new[] { "Position de la pièce" }),
            }, new FunctionDef[0]),
        new TypeDef("VibratoryFeeder", "Chargeur vibrant", "vibration", "Process",
            new[]
            {
                new ActionDef(1, "Vibrer", "vibration", new[] { P("Motif", "Int", null, 1, 1, 8), P("Durée", "Duration", "ms", 300) }, 0.4),
                new ActionDef(2, "Distribuer pièces", "vibration", new[] { P("Durée", "Duration", "ms", 500) }, 0.6),
                new ActionDef(4, "Lire niveau", "visibility", None, 0.0),
            }, new FunctionDef[0]),
        new TypeDef("Press", "Presse", "compress", "Process",
            new[]
            {
                new ActionDef(1, "Exécuter recette", "compress", new[] { P("Recette de presse", "Recipe", null, 3, 1, 16) }, 2.2, true, new[] { "Jugement", "Force finale", "Course finale", "Force max" }),
                new ActionDef(2, "Retour repos", "north", None, 0.8, true),
                new ActionDef(3, "Tarer la force", "tune", None, 0.4),
                new ActionDef(4, "Lire force et course", "visibility", None, 0.0, false, new[] { "Force", "Course" }),
            }, new FunctionDef[0]),
        new TypeDef("DigitalInput", "Entrée numérique", "login", "Entrées/sorties",
            new[]
            {
                new ActionDef(1, "Lire", "visibility", None, 0.0, false, new[] { "Valeur" }),
                new ActionDef(2, "Attendre actif", "hourglass_empty", None, 0.3),
                new ActionDef(3, "Attendre inactif", "hourglass_empty", None, 0.3),
            }, new FunctionDef[0]),
        new TypeDef("Counter", "Compteur", "exposure_plus_1", "Logique",
            new[]
            {
                new ActionDef(1, "Incrémenter", "exposure_plus_1", new[] { P("Pas", "Int", null, 1) }, 0.0, false, new[] { "Valeur" }),
                new ActionDef(2, "Lire", "visibility", None, 0.0),
                new ActionDef(3, "Remettre à zéro", "restart_alt", None, 0.0),
            }, new FunctionDef[0]),
        new TypeDef("CodeReader", "Lecteur de code", "qr_code_scanner", "Capteurs et mesure",
            new[]
            {
                new ActionDef(1, "Lire code", "qr_code_scanner", None, 0.6, false, new[] { "Contenu", "Qualité" }),
                new ActionDef(2, "Vérifier code", "qr_code_scanner", new[] { P("Motif attendu", "Text") }, 0.6),
            }, new FunctionDef[0]),
        new TypeDef("Cylinder", "Vérin", "swap_vert", "Mouvement",
            new[]
            {
                new ActionDef(1, "Move travail", "south", None, 0.4, true),
                new ActionDef(2, "Move repos", "north", None, 0.4, true),
                new ActionDef(3, "Lire position", "visibility", None, 0.0),
            }, new FunctionDef[0]),
        new TypeDef("KeyenceGT2", "Palpeur Keyence GT2", "height", "Capteurs et mesure",
            new[]
            {
                new ActionDef(1, "Mesurer", "height", new[] { P("Échantillons", "Int", null, 5) }, 0.8, false, new[] { "Valeur moyenne", "Jugement" }),
                new ActionDef(2, "Remise à zéro", "restart_alt", None, 0.2),
            }, new FunctionDef[0]),
        new TypeDef("KeyenceIV", "Vision Keyence IV", "photo_camera", "Capteurs et mesure",
            new[]
            {
                new ActionDef(2, "Déclencher", "photo_camera", None, 0.5, false, new[] { "Jugement", "Valeur mesurée" }),
            }, new FunctionDef[0]),
        new TypeDef("Magazine", "Palettiseur ou magasin", "pallet", "Process",
            new[]
            {
                new ActionDef(1, "Présenter plateau suivant", "pallet", None, 3.0),
                new ActionDef(3, "Position de la case", "gps_fixed", new[] { P("Case", "Int", null, 0) }, 0.0),
                new ActionDef(4, "Marquer case", "check", new[] { P("Case", "Int"), P("Occupée", "Bool", null, 1) }, 0.0),
            }, new FunctionDef[0]),
        new TypeDef("Gripper", "Pince", "back_hand", "Préhension",
            new[]
            {
                new ActionDef(1, "Ouvrir", "back_hand", None, 0.3),
                new ActionDef(2, "Fermer", "back_hand", None, 0.3),
                new ActionDef(4, "Vérifier présence pièce", "check_circle", None, 0.0),
            }, new FunctionDef[0]),
        new TypeDef("Group", "Groupe", "folder", "Modules", new ActionDef[0], new FunctionDef[0], true),
        new TypeDef("Safety", "Sécurités", "shield", "Machine", new ActionDef[0], new FunctionDef[0]),
        new TypeDef("Beacon", "Balise", "emergency_heat", "Machine", new ActionDef[0], new FunctionDef[0]),
        new TypeDef("PlanarSystem", "Système planaire", "grid_view", "Mouvement", new ActionDef[0], new FunctionDef[0]),
        new TypeDef("PlanarMover", "Mover", "crop_square", "Mouvement", new ActionDef[0], new FunctionDef[0]),
    };

    public static TypeDef? Get(string code) => Types.FirstOrDefault(t => t.Code == code);

    /// <summary>Durée estimée d'une étape, en secondes (catalogue + course), pour le pied de carte et le chronogramme.</summary>
    public static double EstimatedSeconds(Step step, StationConfig? station)
    {
        switch (step)
        {
            case ActionStep a:
                var t = Type(a.ObjectId, station);
                return t?.Action(a.ActionNo)?.DurationSeconds ?? 0.5;
            case FunctionStep f:
                return DemoFunctions.FindFunction(station, f.ModuleObjectId, f.FunctionId)?.DurationSeconds ?? 1.0;
            case WaitTimeStep w: return w.DurationSeconds;
            case WaitConditionStep: return 0.4;
            case SetStep: return 0.0;
            case CallStep: return 1.0;
            case AlarmStep: return 0.0;
            default: return 0.0;
        }
    }

    private static TypeDef? Type(Guid objectId, StationConfig? station)
    {
        var node = station?.FindObject(objectId);
        return node == null ? null : Get(node.TypeCode);
    }
}
