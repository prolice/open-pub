using OpenPub.Core.Io;
using OpenPub.Core.Model;

namespace OpenPub.Core.Editing;

/// <summary>
/// Historique annuler / retablir par instantanes du document (serialisation JSON compacte).
/// Simple et sur pour un document de quelques centaines de Ko : chaque action enregistre
/// l'etat AVANT modification.
/// </summary>
public sealed class UndoStack
{
    readonly LinkedList<string> _undo = new();
    readonly Stack<string> _redo = new();
    public int Limit { get; set; } = 200;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public event EventHandler? Changed;

    /// <summary>A appeler juste avant de modifier le document.</summary>
    public void Push(PubDocument before)
    {
        _undo.AddLast(DocumentJson.Serialize(before, indented: false));
        while (_undo.Count > Limit) _undo.RemoveFirst();
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Annule la derniere action en enregistrant un instantane prealable a l'avance si rien n'a change.</summary>
    public void DropLastIfUnchanged(PubDocument current)
    {
        if (_undo.Last is { } last && last.Value == DocumentJson.Serialize(current, indented: false))
        {
            _undo.RemoveLast();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public PubDocument? Undo(PubDocument current)
    {
        if (_undo.Last is not { } last) return null;
        _undo.RemoveLast();
        _redo.Push(DocumentJson.Serialize(current, indented: false));
        Changed?.Invoke(this, EventArgs.Empty);
        return DocumentJson.Parse(last.Value);
    }

    public PubDocument? Redo(PubDocument current)
    {
        if (_redo.Count == 0) return null;
        _undo.AddLast(DocumentJson.Serialize(current, indented: false));
        var next = _redo.Pop();
        Changed?.Invoke(this, EventArgs.Empty);
        return DocumentJson.Parse(next);
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
