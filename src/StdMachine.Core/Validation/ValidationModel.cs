namespace StdMachine.Core.Validation;

public enum Severity
{
    Information,
    Warning,
    Error,
}

/// <summary>Lien vers l'élément en cause : page à ouvrir, élément à sélectionner, champ à mettre en surbrillance (docs/07 § 4.2).</summary>
public sealed record ValidationLink(string Page, int? StationNumber = null, Guid? ObjectId = null, Guid? ProcessId = null, Guid? StepId = null, string? Field = null);

public sealed record ValidationIssue(string Code, Severity Severity, string Message, ValidationLink Link);

/// <summary>Limites du PLC (docs/05 § 11), V007.</summary>
public static class PlcLimits
{
    public const int Stations = 16;
    public const int Groups = 16;
    public const int GroupsPerStation = 8;
    public const int OpMapPerStation = 32;
    public const int ObjectsPerStation = 64;
    public const int MachineObjects = 32;
    public const int Movers = 32;
    public const int Slots = 4;
    public const int NamedPositions = 64;
    public const int PlanarPositions = 128;
    public const int Variables = 64;
    public const int PartData = 32;
    public const int Interlocks = 32;
    public const int ForkBranches = 4;
    public const int ConditionTerms = 4;
    public const int PressRecipes = 16;
    public const int PressSegments = 8;
}
