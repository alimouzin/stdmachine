using System.Text.Json.Nodes;

namespace StdMachine.Plant;

/// <summary>
/// Schémas des messages (docs/11 § 4) : identifiant versionné, sujet, conservation, et description des champs de <c>data</c>.
/// Le dictionnaire de données exporté pour l'équipe informatique de l'usine est produit à partir de cette table (JSON Schema 2020-12).
/// </summary>
public sealed record PlantSchema(string Id, string Topic, bool Retain, bool IsEvent, string Family, JsonObject Definition);

public static class Schemas
{
    private static JsonObject T(string type, string? description = null) => Prop(type, description);

    private static JsonObject Prop(string type, string? description = null)
    {
        var o = new JsonObject { ["type"] = type };
        if (description != null) o["description"] = description;
        return o;
    }

    private static JsonObject Nullable(string type) => new JsonObject { ["type"] = new JsonArray(type, "null") };

    private static JsonObject Obj(string[] required, params (string Name, JsonNode Schema)[] props)
    {
        var p = new JsonObject();
        foreach (var (n, s) in props) p[n] = s;
        var req = new JsonArray();
        foreach (var r in required) req.Add(r);
        return new JsonObject { ["type"] = "object", ["required"] = req, ["properties"] = p };
    }

    private static JsonObject Enum(params string[] values)
    {
        var a = new JsonArray();
        foreach (var v in values) a.Add(v);
        return new JsonObject { ["type"] = "string", ["enum"] = a };
    }

    private static JsonObject Arr(JsonNode items) => new JsonObject { ["type"] = "array", ["items"] = items };

    private static readonly string[] States = { "Stopped", "Resetting", "Idle", "Execute", "Held", "Stopping", "Aborted", "Clearing" };
    private static readonly string[] Severities = { "information", "warning", "stationFault", "machineFault", "quality" };

    public static readonly IReadOnlyList<PlantSchema> All = new[]
    {
        new PlantSchema("stdmachine.status/1", "status", true, false, "status", Obj(new[] { "online" }, ("online", Prop("boolean")))),
        new PlantSchema("stdmachine.identity/1", "identity", true, false, "state",
            Obj(new[] { "machineName", "projectHash", "hmiVersion" }, ("machineName", T("string")), ("projectHash", T("string")), ("deployedAt", Nullable("string")), ("hmiVersion", T("string")), ("plcVersion", T("string")))),
        new PlantSchema("stdmachine.state/1", "state", true, false, "state",
            Obj(new[] { "state", "mode", "since", "overridePercent", "loadingLocked", "alarms" },
                ("state", Obj(new[] { "code", "name" }, ("code", T("integer")), ("name", Enum(States)))), ("mode", Enum("production", "maintenance")), ("since", T("string")),
                ("overridePercent", T("number")), ("stopCause", Nullable("string")),
                ("order", new JsonObject { ["type"] = new JsonArray("object", "null") }),
                ("loadingLocked", T("boolean")), ("loadingLockedReason", Nullable("string")),
                ("declaredState", Nullable("string")), ("startBlocked", T("boolean")),
                ("unavailableFunctions", Arr(T("string"))),
                ("alarms", Obj(new[] { "faults", "warnings" }, ("faults", T("integer")), ("warnings", T("integer")))))),
        new PlantSchema("stdmachine.station.state/1", "stations/{n}/state", true, false, "state",
            Obj(new[] { "station", "name", "state", "mode", "since", "qualityHold", "outOfProduction" },
                ("station", T("integer")), ("name", T("string")), ("state", Enum(States)), ("mode", Enum("production", "maintenance")), ("since", T("string")),
                ("operation", Nullable("string")), ("moverNumber", new JsonObject { ["type"] = new JsonArray("integer", "null") }), ("qualityHold", T("boolean")), ("outOfProduction", T("boolean")))),
        new PlantSchema("stdmachine.counters/1", "counters", true, false, "state",
            Obj(new[] { "partsExited", "partsRejected", "inProcess", "timeInState", "cycleTimeSeconds" },
                ("partsExited", T("integer")), ("partsRejected", T("integer")), ("inProcess", T("integer")),
                ("timeInState", Obj(new string[0], ("Stopped", T("number")), ("Resetting", T("number")), ("Idle", T("number")), ("Execute", T("number")), ("Held", T("number")), ("Stopping", T("number")), ("Aborted", T("number")), ("Clearing", T("number")))),
                ("timeInMode", Obj(new string[0], ("production", T("number")), ("maintenance", T("number")))),
                ("cycleTimeSeconds", new JsonObject { ["type"] = new JsonArray("number", "null") }),
                ("order", new JsonObject { ["type"] = new JsonArray("object", "null") }))),
        new PlantSchema("stdmachine.event.alarm/1", "events/alarm", false, true, "alarm",
            Obj(new[] { "event", "severity", "station", "code", "subCode", "text" },
                ("event", Enum("raised", "cleared", "acknowledged")), ("severity", Enum(Severities)), ("station", T("string")), ("object", Nullable("string")),
                ("objectType", Nullable("string")), ("code", T("integer")), ("subCode", T("integer")), ("text", T("string")), ("subText", Nullable("string")),
                ("context", new JsonObject { ["type"] = new JsonArray("object", "null") }))),
        new PlantSchema("stdmachine.event.stop/1", "events/stop", false, true, "stop",
            Obj(new[] { "start", "end", "durationSeconds", "state", "cause" },
                ("start", T("string")), ("end", T("string")), ("durationSeconds", T("number")), ("state", Enum("Stopped", "Held", "Aborted")),
                ("cause", Obj(new[] { "kind" }, ("kind", Enum("alarm", "quality", "orderComplete", "operator")), ("text", Nullable("string")), ("code", new JsonObject { ["type"] = new JsonArray("integer", "null") }), ("station", Nullable("string")))),
                ("declaredState", Nullable("string")), ("user", Nullable("string")))),
        new PlantSchema("stdmachine.event.order/1", "events/order", false, true, "order",
            Obj(new[] { "event", "number", "quantity", "good", "bad", "inProcess" },
                ("event", Enum("started", "suspended", "resumed", "finished", "cancelled", "progress")), ("number", T("string")), ("reference", Nullable("string")),
                ("quantity", T("integer")), ("good", T("integer")), ("bad", T("integer")), ("inProcess", T("integer")))),
        new PlantSchema("stdmachine.event.part/1", "events/part", false, true, "part",
            Obj(new[] { "partId", "createdAt", "closedAt", "finalState", "verdict", "mover", "slot", "operations" },
                ("partId", T("string")), ("order", Nullable("string")), ("reference", Nullable("string")), ("createdAt", T("string")), ("closedAt", T("string")),
                ("finalState", Enum("exited", "rejected", "removed")), ("verdict", Enum("good", "bad", "undefined")), ("mover", T("integer")), ("slot", T("integer")),
                ("operations", Arr(Obj(new[] { "function", "operation", "station", "start", "end", "verdictAfter", "data" },
                    ("function", T("string")), ("operation", T("string")), ("station", T("string")), ("start", T("string")), ("end", T("string")),
                    ("verdictAfter", Enum("good", "bad", "undefined")), ("data", T("object"))))))),
        new PlantSchema("stdmachine.event.quality/1", "events/quality", false, true, "quality",
            Obj(new[] { "event", "scope", "name" },
                ("event", Enum("warning", "action", "released", "reset")), ("scope", Enum("station", "stationImputed", "group", "mover", "machine", "order")), ("name", T("string")),
                ("reason", Nullable("string")), ("consecutive", T("integer")), ("ratePercent", T("number")))),
        new PlantSchema("stdmachine.event.audit/1", "events/audit", false, true, "audit",
            Obj(new[] { "what" }, ("user", Nullable("string")), ("what", T("string")), ("before", Nullable("string")), ("after", Nullable("string")))),
        new PlantSchema("stdmachine.event.force/1", "events/force", false, true, "force",
            Obj(new[] { "event", "kind", "target" },
                ("event", Enum("set", "lifted")), ("kind", Enum("declared", "station", "mover")), ("target", T("string")), ("description", Nullable("string")), ("reason", Nullable("string")), ("by", Nullable("string")))),
        new PlantSchema("stdmachine.maintenance/1", "maintenance", true, false, "maintenance",
            Obj(new[] { "objects" }, ("objects", Arr(Obj(new[] { "station", "object", "actions", "errors", "motionHours" },
                ("station", T("string")), ("object", T("string")), ("actions", T("integer")), ("errors", T("integer")), ("motionHours", T("number"))))))),
        new PlantSchema("stdmachine.health/1", "health", true, false, "health",
            Obj(new[] { "heartbeat", "plcConnection", "outboxPending" }, ("heartbeat", T("integer")), ("plcConnection", T("string")), ("outboxPending", T("integer")), ("diskFreeMb", new JsonObject { ["type"] = new JsonArray("number", "null") }))),
    };

    public static PlantSchema Get(string id) => All.FirstOrDefault(s => s.Id == id) ?? throw new ArgumentException("Schéma inconnu : " + id);

    /// <summary>JSON Schema (2020-12) de l'enveloppe complète d'un message.</summary>
    public static JsonObject EnvelopeSchema(PlantSchema s)
    {
        return new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = "https://stdmachine.local/schemas/" + s.Id,
            ["title"] = s.Id,
            ["type"] = "object",
            ["required"] = new JsonArray("schema", "machineId", "seq", "ts", "data"),
            ["properties"] = new JsonObject
            {
                ["schema"] = new JsonObject { ["const"] = s.Id },
                ["machineId"] = new JsonObject { ["type"] = "string" },
                ["seq"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1 },
                ["ts"] = new JsonObject { ["type"] = "string", ["format"] = "date-time" },
                ["data"] = s.Definition.DeepClone(),
            },
        };
    }

    /// <summary>Valide un message contre son schéma (types, champs obligatoires, énumérations, tableaux) ; renvoie la liste des écarts, vide si valide.</summary>
    public static IReadOnlyList<string> Validate(PlantMessage m)
    {
        var errors = new List<string>();
        var schema = All.FirstOrDefault(s => s.Id == m.Schema);
        if (schema == null) { errors.Add("Schéma inconnu : " + m.Schema); return errors; }
        if (m.Seq < 1) errors.Add("seq doit être ≥ 1");
        if (string.IsNullOrEmpty(m.MachineId)) errors.Add("machineId vide");
        Check(m.Data, schema.Definition, "data", errors);
        return errors;
    }

    private static void Check(JsonNode? value, JsonNode schema, string path, List<string> errors)
    {
        var types = schema["type"] switch
        {
            JsonArray a => a.Select(x => x!.GetValue<string>()).ToList(),
            JsonValue v => new List<string> { v.GetValue<string>() },
            _ => new List<string>(),
        };
        if (types.Count > 0 && !types.Any(t => TypeMatches(value, t))) { errors.Add(path + " : type attendu " + string.Join("|", types)); return; }
        if (value is JsonValue && schema["enum"] is JsonArray en && !en.Any(e => e!.ToString() == value.ToString())) errors.Add(path + " : valeur « " + value + " » hors énumération");
        if (value is JsonObject obj && schema["properties"] is JsonObject props)
        {
            if (schema["required"] is JsonArray req)
                foreach (var r in req) if (!obj.ContainsKey(r!.GetValue<string>())) errors.Add(path + "." + r + " : champ obligatoire absent");
            foreach (var kv in obj)
                if (props[kv.Key] is JsonNode ps) Check(kv.Value, ps, path + "." + kv.Key, errors);
        }
        if (value is JsonArray arr && schema["items"] is JsonNode items)
            for (int i = 0; i < arr.Count; i++) Check(arr[i], items, path + "[" + i + "]", errors);
    }

    private static bool TypeMatches(JsonNode? v, string t)
    {
        switch (t)
        {
            case "null": return v == null;
            case "object": return v is JsonObject;
            case "array": return v is JsonArray;
            case "string": return v is JsonValue s && s.TryGetValue<string>(out _);
            case "boolean": return v is JsonValue b && b.TryGetValue<bool>(out _);
            case "integer": return v is JsonValue i && (i.TryGetValue<long>(out _) || i.TryGetValue<int>(out _));
            case "number": return v is JsonValue n && (n.TryGetValue<double>(out _) || n.TryGetValue<long>(out _) || n.TryGetValue<int>(out _) || n.TryGetValue<decimal>(out _));
            default: return false;
        }
    }

    /// <summary>Dictionnaire de données : un fichier JSON Schema par message, dans <paramref name="directory"/>.</summary>
    public static void Export(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (var s in All)
            File.WriteAllText(Path.Combine(directory, s.Id.Replace('/', '-') + ".schema.json"), EnvelopeSchema(s).ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }
}
