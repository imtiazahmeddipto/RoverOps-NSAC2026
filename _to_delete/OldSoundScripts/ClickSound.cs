using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The UI click (and a very soft hover tick) for a button, only while it can be clicked.
// UISounds adds it to every Button by itself; you can also add it by hand.
public class ClickSound : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler
{
    bool CanUse
    {
        get
        {
            Selectable s = GetComponent<Selectable>();
            return s == null || s.IsInteractable();
        }
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Left && CanUse) UISounds.Play(UISounds.Cue.Click);
    }

    public void OnPointerEnter(PointerEventData e)
    {
        if (CanUse && !e.dragging) UISounds.Play(UISounds.Cue.Hover);
    }
}
