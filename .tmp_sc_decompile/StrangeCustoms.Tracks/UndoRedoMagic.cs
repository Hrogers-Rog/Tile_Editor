using System.Collections.Generic;

namespace StrangeCustoms.Tracks;

internal class UndoRedoMagic
{
	private const int maximumActions = 100;

	private List<UndoRedoAction> actions = new List<UndoRedoAction>();

	private int currentIndex = -1;

	public void Record(UndoRedoAction action)
	{
		if (currentIndex < actions.Count - 1)
		{
			actions.RemoveRange(currentIndex + 1, actions.Count - currentIndex - 1);
		}
		if (actions.Count >= 100)
		{
			actions.RemoveAt(0);
		}
		actions.Add(action);
		action.Redo(action.RedoState);
		currentIndex = actions.Count - 1;
	}

	public bool Undo()
	{
		if (currentIndex < 0)
		{
			return false;
		}
		UndoRedoAction undoRedoAction = actions[currentIndex--];
		undoRedoAction.Undo(undoRedoAction.UndoState);
		return true;
	}

	public bool Redo()
	{
		if (currentIndex >= actions.Count - 1)
		{
			return false;
		}
		UndoRedoAction undoRedoAction = actions[currentIndex++];
		undoRedoAction.Redo(undoRedoAction.RedoState);
		return true;
	}
}
