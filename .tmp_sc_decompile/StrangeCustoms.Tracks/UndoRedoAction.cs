using System;

namespace StrangeCustoms.Tracks;

internal struct UndoRedoAction
{
	public Action<object> Undo;

	public Action<object> Redo;

	public object UndoState;

	public object RedoState;
}
