using System.Text.Json;

namespace Core.Orders;

/// <summary>Ordre de fabrication.</summary>
public sealed class Order
{
    public int Id { get; set; }
    public string Reference { get; set; } = "";
    public int Quantity { get; set; }
}

/// <summary>
/// Source d'OF. Aujourd'hui locale (<see cref="LocalOrderSource"/>) ; un connecteur vers un
/// système externe pourra implémenter la même interface.
/// </summary>
public interface IOrderSource
{
    IReadOnlyList<Order> GetOrders();

    /// <summary>Crée un OF. Retourne null et renseigne <paramref name="error"/> si la saisie est invalide.</summary>
    Order? Create(string reference, int quantity, out string? error);

    Order? Selected { get; }

    bool Select(int orderId);

    event Action? Changed;
}

/// <summary>OF locaux, en mémoire, sauvegardés dans un JSON si un chemin est fourni.</summary>
public sealed class LocalOrderSource : IOrderSource
{
    private sealed class State
    {
        public List<Order> Orders { get; set; } = new();
        public int? SelectedId { get; set; }
    }

    private readonly string? _path;
    private readonly State _state;

    public LocalOrderSource(string? path = null)
    {
        _path = path;
        _state = Load(path);
    }

    public event Action? Changed;

    public IReadOnlyList<Order> GetOrders() => _state.Orders.ToList();

    public Order? Selected => _state.Orders.FirstOrDefault(o => o.Id == _state.SelectedId);

    public Order? Create(string reference, int quantity, out string? error)
    {
        reference = reference?.Trim() ?? "";
        error = null;
        if (reference.Length == 0) error = "La référence est obligatoire.";
        else if (quantity <= 0) error = "La quantité doit être supérieure à zéro.";
        else if (_state.Orders.Any(o => o.Reference == reference)) error = "Cette référence existe déjà.";
        if (error is not null) return null;

        var order = new Order
        {
            Id = _state.Orders.Count == 0 ? 1 : _state.Orders.Max(o => o.Id) + 1,
            Reference = reference,
            Quantity = quantity,
        };
        _state.Orders.Add(order);
        Save();
        Changed?.Invoke();
        return order;
    }

    public bool Select(int orderId)
    {
        if (_state.Orders.All(o => o.Id != orderId)) return false;
        _state.SelectedId = orderId;
        Save();
        Changed?.Invoke();
        return true;
    }

    private static State Load(string? path)
    {
        try
        {
            if (path is not null && File.Exists(path))
                return JsonSerializer.Deserialize<State>(File.ReadAllText(path)) ?? new State();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Fichier illisible : on repart d'une liste vide.
        }
        return new State();
    }

    private void Save()
    {
        if (_path is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        File.WriteAllText(_path, JsonSerializer.Serialize(_state));
    }
}
