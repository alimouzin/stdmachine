using System.Text.Json.Nodes;

namespace StdMachine.Plant;

// Interface avec le système de l'usine, première version (docs/11 § 7.1) : contrats, modèle des messages, canal Journal de messages.

public enum PlantChannelState
{
    Disabled,
    Planned,
    Starting,
    Connected,
    Degraded,
    Error,
}

public sealed record PlantChannelStatus(PlantChannelState State, string Detail, string? LastError, long MessagesSent)
{
    public static PlantChannelStatus Of(PlantChannelState state, string detail = "", string? error = null, long sent = 0) => new PlantChannelStatus(state, detail, error, sent);
}

/// <summary>Réglages d'un canal : activation, paramètres libres (texte), secrets (chiffrés au repos, jamais affichés).</summary>
public sealed class ChannelSettings
{
    public bool Enabled { get; set; }
    public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>();
    public Dictionary<string, string> Secrets { get; set; } = new Dictionary<string, string>();

    public string Get(string key, string fallback = "") => Values.TryGetValue(key, out var v) ? v : fallback;
}

public sealed record ChannelTestResult(bool Success, string Message);

public interface IPlantChannel
{
    string Id { get; }
    string DisplayName { get; }
    /// <summary>Faux : réglages seulement, activation « Prévu ».</summary>
    bool IsImplemented { get; }
    PlantChannelStatus Status { get; }
    event EventHandler<PlantChannelStatus>? StatusChanged;
    Task StartAsync(ChannelSettings settings, CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    Task<ChannelTestResult> TestAsync(ChannelSettings settings, CancellationToken ct);
}

/// <summary>Un message de la passerelle : enveloppe commune (docs/11 § 4.2).</summary>
public sealed record PlantMessage(string Schema, string MachineId, long Seq, DateTimeOffset Ts, string Topic, JsonObject Data)
{
    /// <summary>L'enveloppe complète en JSON : schema, machineId, seq, ts, data.</summary>
    public JsonObject ToEnvelope() => new JsonObject
    {
        ["schema"] = Schema,
        ["machineId"] = MachineId,
        ["seq"] = Seq,
        ["ts"] = Ts.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture),
        ["data"] = Data.DeepClone(),
    };

    public string ToJson() => ToEnvelope().ToJsonString();
}

/// <summary>Canal qui reçoit les messages de la passerelle.</summary>
public interface IPlantEventChannel : IPlantChannel
{
    /// <summary>Événements (un message par événement, ordre conservé).</summary>
    Task PublishEventAsync(PlantMessage message, CancellationToken ct);

    /// <summary>États conservés (seul le dernier compte).</summary>
    Task PublishStateAsync(PlantMessage message, CancellationToken ct);
}

public sealed class CommunicationSettings
{
    public string MachineId { get; set; } = "MX-4";
    /// <summary>Racine des sujets selon la hiérarchie ISA-95 : entreprise/site/zone/ligne/machine.</summary>
    public string TopicRoot { get; set; } = "entreprise/site-1/assemblage/ligne-1/mx-4";
    public Dictionary<string, ChannelSettings> Channels { get; set; } = new Dictionary<string, ChannelSettings>();
    /// <summary>Sujets désactivés un à un.</summary>
    public List<string> DisabledTopics { get; set; } = new List<string>();
    public int CountersPeriodSeconds { get; set; } = 60;
    public int HealthPeriodSeconds { get; set; } = 30;

    public ChannelSettings For(string channelId)
    {
        if (!Channels.TryGetValue(channelId, out var c)) Channels[channelId] = c = new ChannelSettings();
        return c;
    }
}

public interface IPlantGateway
{
    IReadOnlyList<IPlantChannel> Channels { get; }
    CommunicationSettings Settings { get; }

    /// <summary>Active, désactive et relance les canaux selon les réglages.</summary>
    Task ApplySettingsAsync(CommunicationSettings settings, CancellationToken ct);
}
