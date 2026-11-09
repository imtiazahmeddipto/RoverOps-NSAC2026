using UnityEngine;
using UnityEngine.EventSystems;

// On a chip button inside a code block (the If reading, the < > = sign, the value picker's ▼).
// A click with a tiny hand movement counts as a drag in Unity, and a drag on the BLOCK cancels the click.
// This takes the drag on the chip itself (and does nothing with it), so the click always goes through.
// CodeBlock adds it to its chips by itself: nothing to set up.
public class ChipClick : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public void OnBeginDrag(PointerEventData e) { }
    public void OnDrag(PointerEventData e) { }
    public void OnEndDrag(PointerEventData e) { }
}
