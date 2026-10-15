using System.Collections.ObjectModel;
using Core.Access;
using Core.Orders;
using Hmi.Mvvm;
using Hmi.Services;

namespace Hmi.ViewModels;

public sealed class OrdersPageViewModel : PageViewModel
{
    private readonly IOrderSource _source;
    private readonly SessionService _session;
    private string _newReference = "";
    private string _newQuantity = "";
    private string _message = "";
    private Order? _selected;

    public OrdersPageViewModel(IOrderSource source, SessionService session) : base("Ordres de fabrication")
    {
        _source = source;
        _session = session;
        CreateCommand = new RelayCommand(Create, () => CanEdit);
        _source.Changed += Reload;
        _session.Changed += () =>
        {
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(EditHint));
        };
        Reload();
    }

    public ObservableCollection<Order> Orders { get; } = new();
    public RelayCommand CreateCommand { get; }

    public bool CanEdit => _session.CanDo(ActionType.OrderEdit);
    public string EditHint => CanEdit ? "" : "Connexion avec un niveau suffisant requise pour créer ou sélectionner un OF.";

    public Order? SelectedOrder
    {
        get => _selected;
        set
        {
            if (value is null || !CanEdit) return;
            if (_source.Select(value.Id)) Set(ref _selected, value);
        }
    }

    public string NewReference
    {
        get => _newReference;
        set => Set(ref _newReference, value);
    }

    public string NewQuantity
    {
        get => _newQuantity;
        set => Set(ref _newQuantity, value);
    }

    public string Message
    {
        get => _message;
        private set => Set(ref _message, value);
    }

    public string CurrentText => _source.Selected is { } o
        ? $"OF courant : {o.Reference} · quantité {o.Quantity}"
        : "Aucun OF sélectionné";

    private void Create()
    {
        if (!int.TryParse(NewQuantity, out var quantity))
        {
            Message = "La quantité doit être un nombre entier.";
            return;
        }
        var order = _source.Create(NewReference, quantity, out var error);
        if (order is null)
        {
            Message = error ?? "Création impossible.";
            return;
        }
        Message = $"OF {order.Reference} créé.";
        NewReference = "";
        NewQuantity = "";
    }

    private void Reload()
    {
        Orders.Clear();
        foreach (var o in _source.GetOrders()) Orders.Add(o);
        _selected = _source.Selected is { } s ? Orders.FirstOrDefault(o => o.Id == s.Id) : null;
        OnPropertyChanged(nameof(SelectedOrder));
        OnPropertyChanged(nameof(CurrentText));
    }
}
