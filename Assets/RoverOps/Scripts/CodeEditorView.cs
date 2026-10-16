using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

// The Code Editor's view: mouse wheel (or the - / + buttons) zooms, dragging empty space moves the program.
// Blocks never leave the frame: the view clips them (RectMask2D) and panning stops at the edges.
public class CodeEditorView : MonoBehaviour, IScrollHandler, IBeginDragHandler, IDragHandler
{
    public RectTransform content;       // the program (block list) inside this view
    public TMP_Text zoomLabel;          // optional: shows "100%"
    public float minZoom = 0.5f, maxZoom = 1.6f, step = 0.1f;

    RectTransform view;

    void Awake()
    {
        view = (RectTransform)transform;
        UpdateLabel();
    }

    void LateUpdate() => Clamp();       // the program grows and shrinks as blocks are added

    // ---------------- ZOOM ----------------
    public void OnScroll(PointerEventData e)
    {
        if (e.scrollDelta.y != 0f) ZoomAt(e.position, e.scrollDelta.y > 0f ? step : -step);
    }

    public void ZoomIn() => ZoomAt(ViewCenter(), step);
    public void ZoomOut() => ZoomAt(ViewCenter(), -step);

    public void ResetView()
    {
        content.localScale = Vector3.one;
        content.anchoredPosition = Vector2.zero;
        UpdateLabel();
    }

    // Zoom while keeping the point under the mouse in the same place
    void ZoomAt(Vector2 screenPoint, float change)
    {
        float old = content.localScale.x;
        float zoom = Mathf.Clamp(Mathf.Round((old + change) * 10f) / 10f, minZoom, maxZoom);
        if (Mathf.Approximately(zoom, old)) return;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(content, screenPoint, null, out Vector2 before);
        content.localScale = Vector3.one * zoom;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(content, screenPoint, null, out Vector2 after);
        content.anchoredPosition += (after - before) * zoom;
        Clamp();
        UpdateLabel();
    }

    Vector2 ViewCenter() => RectTransformUtility.WorldToScreenPoint(null, view.TransformPoint(view.rect.center));

    void UpdateLabel()
    {
        if (zoomLabel) zoomLabel.text = Mathf.RoundToInt(content.localScale.x * 100f) + "%";
    }

    // ---------------- PAN (drag empty space) ----------------
    public void OnBeginDrag(PointerEventData e) { }

    public void OnDrag(PointerEventData e)
    {
        content.anchoredPosition += e.delta / view.lossyScale.x;   // screen pixels -> view units
        Clamp();
    }

    // Keep the program inside the frame: it can only move when it's bigger than the view
    void Clamp()
    {
        Vector2 viewSize = view.rect.size;
        Vector2 size = content.rect.size * content.localScale.x;
        Vector2 p = content.anchoredPosition;
        p.x = size.x <= viewSize.x ? 0f : Mathf.Clamp(p.x, viewSize.x - size.x, 0f);
        p.y = size.y <= viewSize.y ? 0f : Mathf.Clamp(p.y, 0f, size.y - viewSize.y);
        content.anchoredPosition = p;
    }
}
