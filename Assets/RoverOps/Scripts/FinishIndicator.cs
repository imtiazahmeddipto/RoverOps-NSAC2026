using UnityEngine;
using UnityEngine.UI;

// An arrow on the edge of the Mission Viewer that points to the finish line.
// Hidden when the finish line is inside the camera frame, or when the mission doesn't end at a finish line.
// Put it on the arrow Image (a child of the Mission Viewer Raw Image). The arrow art must point RIGHT.
public class FinishIndicator : MonoBehaviour
{
    public MissionHandler mission;
    public RoverCamera roverCamera;
    public RectTransform viewArea;      // the Mission Viewer Raw Image
    public float edgeMargin = 24f;      // space between the arrow and the viewer's edge
    public bool rotateArrow = true;     // turn the arrow toward the finish line

    RectTransform arrow;
    Graphic[] graphics;

    void Awake()
    {
        arrow = (RectTransform)transform;
        graphics = GetComponentsInChildren<Graphic>(true);
    }

    void LateUpdate()
    {
        bool show = Place();
        foreach (Graphic g in graphics)
            if (g.enabled != show) g.enabled = show;    // hide the images, keep this script running
    }

    bool Place()
    {
        if (mission == null || roverCamera == null || viewArea == null) return false;
        if (!mission.NeedsFinish || mission.finishLine == null) return false;

        Camera cam = roverCamera.ActiveCamera;
        if (cam == null) return false;

        // Where the finish line is on the camera image (0..1 = inside)
        Vector3 vp = cam.WorldToViewportPoint(mission.finishLine.position);
        bool inFront = vp.z > 0f;
        if (inFront && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f) return false;

        // Direction from the image center (flipped when the finish is behind the camera)
        Vector2 dir = new Vector2(vp.x - 0.5f, vp.y - 0.5f);
        if (!inFront) dir = -dir;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector2.down;     // straight behind: point down (turn around)

        // Push it out to the viewer's edge
        Rect rect = viewArea.rect;
        Vector2 dirPx = new Vector2(dir.x * rect.width, dir.y * rect.height);
        Vector2 half = new Vector2(Mathf.Max(0f, rect.width / 2f - edgeMargin), Mathf.Max(0f, rect.height / 2f - edgeMargin));
        float tx = Mathf.Abs(dirPx.x) > 0.0001f ? half.x / Mathf.Abs(dirPx.x) : float.MaxValue;
        float ty = Mathf.Abs(dirPx.y) > 0.0001f ? half.y / Mathf.Abs(dirPx.y) : float.MaxValue;
        Vector2 local = rect.center + dirPx * Mathf.Min(tx, ty);

        arrow.position = viewArea.TransformPoint(local);
        if (rotateArrow)
            arrow.rotation = viewArea.rotation * Quaternion.Euler(0f, 0f, Mathf.Atan2(dirPx.y, dirPx.x) * Mathf.Rad2Deg);
        return true;
    }
}
