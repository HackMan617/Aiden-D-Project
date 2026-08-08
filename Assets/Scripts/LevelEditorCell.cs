using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// One clickable square of the level editor's board. Pressing paints the cell with the active tool;
// dragging across neighbouring cells keeps painting, so a wall can be drawn in one stroke rather
// than one click per tile.
//
// The drag is detected by asking the mouse directly (this project is Input-System only) instead of
// using OnDrag, because the pointer never "drags" a cell — it just sweeps over other cells, which
// arrives as a plain pointer-enter on each of them.
public class LevelEditorCell : MonoBehaviour, IPointerDownHandler, IPointerEnterHandler
{
    [HideInInspector] public LevelEditor editor;
    [HideInInspector] public int cellX, cellY;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (editor != null) editor.PaintCell(cellX, cellY, false);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (editor == null) return;
        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.isPressed) editor.PaintCell(cellX, cellY, true);
    }
}
