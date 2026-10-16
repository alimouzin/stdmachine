namespace StdMachine.Core.Catalog;

// Modèle du fichier catalog/catalog.json (docs/03, docs/07 § 3.3). Source unique des numéros de types,
// d'actions, de paramètres et d'erreurs : aucun numéro en dur ailleurs.

public sealed class CatalogSignal
{
    public int No { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>DI, DO, AI, AO.</summary>
    public string Kind { get; set; } = string.Empty;
    public bool Required { get; set; }
    public string? VisibleWhen { get; set; }
}

public sealed class CatalogParam
{
    public int No { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "Real";
    public double Default { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public string? Unit { get; set; }
    public List<string>? Values { get; set; }
    public string? Note { get; set; }
}

public sealed class CatalogDisplay
{
    public int No { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "Real";
    public List<string>? Values { get; set; }
}

public sealed class CatalogActionParam
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "Real";
    /// <summary>Valeur par défaut : un nombre, ou une référence « Pn » à un paramètre de l'objet (lue comme texte).</summary>
    public System.Text.Json.JsonElement? Default { get; set; }
    public List<string>? Values { get; set; }
    public bool Optional { get; set; }
    public string? Unit { get; set; }
}

public sealed class CatalogResult
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "Real";
    public List<string>? Values { get; set; }
}

public sealed class CatalogAction
{
    public int No { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public bool Motion { get; set; }
    public bool ManualOnly { get; set; }
    public bool Immediate { get; set; }
    public int? TimeoutMs { get; set; }
    public bool AddsDuration { get; set; }
    public string? TimeoutCalc { get; set; }
    public int? TimeoutParam { get; set; }
    public bool NoTimeout { get; set; }
    public List<CatalogActionParam> Params { get; set; } = new List<CatalogActionParam>();
    public List<CatalogResult> Results { get; set; } = new List<CatalogResult>();
}

public sealed class CatalogError
{
    public int Code { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? Subcode { get; set; }
}

public sealed class CatalogHardware
{
    /// <summary>Axis, EtherCat, Network, Planar.</summary>
    public string Kind { get; set; } = string.Empty;
    public bool Required { get; set; }
    public string? VisibleWhen { get; set; }
}

public sealed class CatalogType
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public bool HasMotion { get; set; }
    public string? Scope { get; set; }
    public bool InterfaceToDefine { get; set; }
    public CatalogHardware? Hardware { get; set; }
    public List<CatalogSignal> Signals { get; set; } = new List<CatalogSignal>();
    public List<CatalogParam> Params { get; set; } = new List<CatalogParam>();
    public List<CatalogDisplay> Display { get; set; } = new List<CatalogDisplay>();
    public List<CatalogAction> Actions { get; set; } = new List<CatalogAction>();
    public List<CatalogError> Errors { get; set; } = new List<CatalogError>();

    public CatalogAction? Action(int no) => Actions.FirstOrDefault(a => a.No == no);
}

public sealed class CatalogRole
{
    public string Role { get; set; } = string.Empty;
    public List<int> Types { get; set; } = new List<int>();
    public string? Component { get; set; }
    public bool Required { get; set; }
}

public sealed class CatalogFunction
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public List<CatalogActionParam> Params { get; set; } = new List<CatalogActionParam>();
    public List<CatalogResult> Results { get; set; } = new List<CatalogResult>();
    /// <summary>Arbre d'étapes sur les rôles du module (même vocabulaire que les process, dépliage en phase 3).</summary>
    public System.Text.Json.JsonElement Body { get; set; }
}

public sealed class CatalogModule
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public List<CatalogRole> Roles { get; set; } = new List<CatalogRole>();
    public List<CatalogParam> Params { get; set; } = new List<CatalogParam>();
    public List<CatalogFunction> Functions { get; set; } = new List<CatalogFunction>();
}

public sealed class CatalogDocument
{
    public int CatalogVersion { get; set; }
    public List<string> ObjectStates { get; set; } = new List<string>();
    public List<string> ActionStatus { get; set; } = new List<string>();
    public List<CatalogError> CommonErrors { get; set; } = new List<CatalogError>();
    public List<CatalogType> Types { get; set; } = new List<CatalogType>();
    public List<CatalogModule> Modules { get; set; } = new List<CatalogModule>();

    public CatalogType? Type(int id) => Types.FirstOrDefault(t => t.Id == id);

    public CatalogType? Type(string code) => Types.FirstOrDefault(t => t.Code == code);

    public CatalogModule? Module(int id) => Modules.FirstOrDefault(m => m.Id == id);
}
