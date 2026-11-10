using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Put it on the hover area (e.g. the "Hover React" image over the title). The image stays as it is.
// Hover it for a moment: this mission's description (from the Mission Database) appears beside the cursor.
// Description Text: your own text to show (optional). Empty = a small tooltip (dark box + text) is made for you.
public class MissionInfoHover : MonoBehaviour
{
    public TMP_Text descriptionText;            // optional: the text to show (its object is shown / hidden and moved)
    [Min(0f)] public float delay = 1f;          // seconds of hovering before it shows
    public bool followCursor = true;            // sit beside the cursor (else stay where it was placed)
    public Vector2 cursorOffset = new Vector2(18f, -18f);   // screen pixels from the cursor (right and down)
    public float tooltipWidth = 360f;           // width of the made tooltip (its height fits the text)

    RectTransform hoverArea, popup;
    Canvas canvas;
    float enterTime = -1f;                      // -1 = the pointer isn't over the hover area
    bool shown;

    void Awake()
    {
        hoverArea = (RectTransform)transform;
        canvas = GetComponentInParent<Canvas>();
        if (descriptionText == null) descriptionText = MakeTooltip();
        // what is shown and moved: the made tooltip's box, or your own text
        Transform parent = descriptionText.transform.parent;
        popup = parent != null && parent.name == "Tooltip" ? (RectTransform)parent : descriptionText.rectTransform;
        popup.gameObject.SetActive(false);
    }

    void Update()
    {
        if (!Over(hoverArea))
        {
            enterTime = -1f;
            if (shown) { shown = false; popup.gameObject.SetActive(false); }
            return;
        }
        if (enterTime < 0f) enterTime = Time.unscaledTime;
        if (shown) { if (followCursor) FollowCursor(); return; }
        if (Time.unscaledTime - enterTime < delay) return;

        MissionData mission = MissionHandler.Current != null ? MissionHandler.Current.Mission : null;
        if (mission == null || string.IsNullOrWhiteSpace(mission.description)) return;    // nothing to show
        descriptionText.text = mission.description;
        popup.gameObject.SetActive(true);
        popup.SetAsLastSibling();                                             // drawn on top
        FitHeight();
        shown = true;
        if (followCursor) FollowCursor();
    }

    // the made tooltip grows with the description
    void FitHeight()
    {
        if (popup == descriptionText.rectTransform) return;
        descriptionText.ForceMeshUpdate();
        float h = descriptionText.GetPreferredValues(descriptionText.text, tooltipWidth - 28f, 0f).y + 22f;
        popup.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, tooltipWidth);
        popup.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);
    }

    // Beside the cursor; flips to the other side near the right / bottom edge so it stays on screen
    void FollowCursor()
    {
        Vector2 pos = PointerPosition();
        Vector2 size = popup.rect.size * popup.lossyScale;                    // popup size in screen pixels (overlay canvas)
        float x = cursorOffset.x, y = cursorOffset.y;
        if (pos.x + x + size.x > Screen.width) x = -cursorOffset.x - size.x;  // too far right: put it on the left
        if (pos.y + y - size.y < 0f) y = -cursorOffset.y + size.y;            // too low: put it above
        Vector2 topLeft = pos + new Vector2(x, y);
        Vector2 pivotShift = new Vector2(popup.pivot.x * size.x, -(1f - popup.pivot.y) * size.y);
        if (RectTransformUtility.ScreenPointToWorldPointInRectangle((RectTransform)popup.parent, topLeft + pivotShift, Cam(), out Vector3 world))
            popup.position = world;
    }

    static Vector2 PointerPosition()
    {
#if ENABLE_INPUT_SYSTEM
        UnityEngine.InputSystem.Pointer p = UnityEngine.InputSystem.Pointer.current;
        return p != null ? p.position.ReadValue() : new Vector2(-9999f, -9999f);
#else
        return Input.mousePosition;
#endif
    }

    Camera Cam() => canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;

    bool Over(RectTransform r) => r != null && RectTransformUtility.RectangleContainsScreenPoint(r, PointerPosition(), Cam());

    // A simple tooltip on the canvas: dark rounded-ish box with the text inside (never blocks clicks)
    TMP_Text MakeTooltip()
    {
        Transform root = canvas != null ? canvas.rootCanvas.transform : transform;
        var box = new GameObject("Tooltip", typeof(RectTransform), typeof(Image));
        box.transform.SetParent(root, false);
        var brt = (RectTransform)box.transform;
        brt.pivot = new Vector2(0f, 1f);
        brt.sizeDelta = new Vector2(tooltipWidth, 80f);
        Image bg = box.GetComponent<Image>();
        bg.color = new Color(0.08f, 0.07f, 0.16f, 0.92f);
        bg.raycastTarget = false;

        var go = new GameObject("Description", typeof(RectTransform));
        go.transform.SetParent(box.transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(14, 11); rt.offsetMax = new Vector2(-14, -11);
        var text = go.AddComponent<TextMeshProUGUI>();
        TMP_Text anyText = transform.parent != null ? transform.parent.GetComponentInChildren<TMP_Text>(true) : null;
        if (anyText != null) text.font = anyText.font;                       // the title's font
        text.color = Color.white;
        text.fontSize = 22;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.raycastTarget = false;
        return text;
    }
}
