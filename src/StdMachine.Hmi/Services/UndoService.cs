using CommunityToolkit.Mvvm.ComponentModel;

namespace StdMachine.Hmi.Services;

/// <summary>Modification annulable (docs/07 § 6) : chaque dépôt, déplacement ou réglage de l'éditeur en est une.</summary>
public interface IUndoableEdit
{
    string Description { get; }
    void Undo();
    void Redo();
}

public sealed class DelegateEdit : IUndoableEdit
{
    private readonly Action _undo;
    private readonly Action _redo;

    public DelegateEdit(string description, Action undo, Action redo)
    {
        Description = description; _undo = undo; _redo = redo;
    }

    public string Description { get; }
    public void Undo() => _undo();
    public void Redo() => _redo();
}

/// <summary>Pile annuler / refaire (100 niveaux).</summary>
public sealed class UndoService : ObservableObject
{
    private const int Limit = 100;
    private readonly List<IUndoableEdit> _undo = new List<IUndoableEdit>();
    private readonly List<IUndoableEdit> _redo = new List<IUndoableEdit>();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoDescription => _undo.Count > 0 ? _undo[_undo.Count - 1].Description : null;

    public event EventHandler? Changed;

    /// <summary>Enregistre une modification déjà appliquée.</summary>
    public void Push(IUndoableEdit edit)
    {
        _undo.Add(edit);
        if (_undo.Count > Limit) _undo.RemoveAt(0);
        _redo.Clear();
        Notify();
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        var e = _undo[_undo.Count - 1];
        _undo.RemoveAt(_undo.Count - 1);
        e.Undo();
        _redo.Add(e);
        Notify();
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        var e = _redo[_redo.Count - 1];
        _redo.RemoveAt(_redo.Count - 1);
        e.Redo();
        _undo.Add(e);
        Notify();
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(UndoDescription));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
