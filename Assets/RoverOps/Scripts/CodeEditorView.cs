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

    // ---------------- FOLLOW ----------------
    public void Refresh() => Clamp();   // after blocks were added or removed

    public void ScrollToTop()
    {
        Vector2 p = content.anchoredPosition;
        p.y = 0f;
        content.anchoredPosition = p;
        Clamp();
    }

    // Pans the program just enough that 'target' is fully inside the frame (with a margin), if it fits
    static readonly Vector3[] corners = new Vector3[4];

    public void Reveal(RectTransform target, float margin = 12f)
    {
        if (target == null || content == null) return;
        target.GetWorldCorners(corners);
        Vector3 min = view.InverseTransformPoint(corners[0]);
        Vector3 max = view.InverseTransformPoint(corners[2]);
        Rect r = view.rect;

        Vector2 shift = Vector2.zero;
        if (min.x < r.xMin + margin) shift.x = r.xMin + margin - min.x;            // too far left (left edge first)
        else if (max.x > r.xMax - margin) shift.x = r.xMax - margin - max.x;       // too far right
        if (max.y > r.yMax - margin) shift.y = r.yMax - margin - max.y;            // above the frame (top edge first)
        else if (min.y < r.yMin + margin) shift.y = r.yMin + margin - min.y;       // below the frame
        if (shift == Vector2.zero) return;

        content.anchoredPosition += shift;
        Clamp();
    }

    // ---------------- PAN (drag empty space) ----------------
    public void OnBeginDrag(PointerEventData e) { }

    public void OnDrag(PointerEventData e)
    {
        content.anchoredPosition += e.delta / view.lossyScale.x;   // screen pixels -> view units
        Clamp();
    }

    // Keep the program inside the frame: it can only move when it's bigger than the view.
    // Sideways it uses the BLOCKS' width (with indents), not the Program object's (that one is wider than the
    // blocks), so the blocks are never pushed off the left or right edge. If they fit, they stay left-aligned.
    public float sideMargin = 8f;       // space kept between the blocks and the frame's sides

    void Clamp()
    {
        float zoom = content.localScale.x;
        Vector2 viewSize = view.rect.size;
        Vector2 p = content.anchoredPosition;

        BlocksWidth(out float left, out float right);           // in the program's own units
        float min = (left - sideMargin) * zoom, max = (right + sideMargin) * zoom;
        p.x = max - min <= viewSize.x ? -min                   // fits: left-aligned
            : Mathf.Clamp(p.x, viewSize.x - max, -min);        // wider than the frame: pan between both edges

        float height = content.rect.height * zoom;
        p.y = height <= viewSize.y ? 0f : Mathf.Clamp(p.y, 0f, height - viewSize.y);
        content.anchoredPosition = p;
    }

    // Left and right edge of all the blocks (their visible bodies, so indents count), seen from the program
    void BlocksWidth(out float left, out float right)
    {
        left = float.MaxValue;
        right = float.MinValue;
        foreach (Transform child in content)
        {
            if (!child.gameObject.activeSelf) continue;
            CodeBlock block = child.GetComponent<CodeBlock>();
            RectTransform shape = block != null && block.body != null ? block.body : child as RectTransform;
            if (shape == null) continue;
            shape.GetWorldCorners(corners);
            left = Mathf.Min(left, content.InverseTransformPoint(corners[0]).x);
            right = Mathf.Max(right, content.InverseTransformPoint(corners[2]).x);
        }
        if (left > right)                                       // no blocks: the Program object itself
        {
            left = content.rect.xMin;
            right = content.rect.xMax;
        }
    }
}
