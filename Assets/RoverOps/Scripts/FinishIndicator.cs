using UnityEngine;
using UnityEngine.UI;

// An arrow on the edge of the Mission Viewer that points to what the mission needs NEXT, from its Finish By:
//   FinishLine     - the finish line
//   AnalyzeRock    - the nearest target rock not analyzed yet
//   SendData       - target rocks / photo targets one by one (nearest first) until enough data is gathered,
//                    then the Lander to send it. No targets in the scene ("any rock counts"): only the Lander,
//                    once the needed data is gathered. A full memory also points to the Lander (send first).
//   Both           - as SendData, then the finish line once the data is delivered
//   CollectSamples - the nearest target sample not collected yet (when the scene has SampleTarget samples)
// Hidden when the target is inside the camera frame, or nothing is left to point at.
// Optional Target Marker (e.g. a ▼ image): for target rocks / photo targets IN the frame, it sits above the target.
// Put it on the arrow Image (a child of the Mission Viewer Raw Image). The arrow art must point RIGHT.
public class FinishIndicator : MonoBehaviour
{
    public MissionHandler mission;
    public RoverCamera roverCamera;
    public RectTransform viewArea;      // the Mission Viewer Raw Image
    public float edgeMargin = 24f;      // space between the arrow and the viewer's edge
    public bool rotateArrow = true;     // turn the arrow toward the finish line

    [Header("Rocks / photo targets in view")]
    public float markerHeight = 0.3f;   // the arrow sits this many metres above the top of the target, pointing down

    RectTransform arrow;
    Graphic[] graphics;
    bool targetIsData;                  // the current target is a rock / photo target (not the finish line)
    Vector3 targetTop;                  // the top of the current target (for the marker)

    void Awake()
    {
        // empty links: find them in the scene (each mission scene has one of each)
        if (mission == null) mission = FindAnyObjectByType<MissionHandler>();
        if (roverCamera == null) roverCamera = FindAnyObjectByType<RoverCamera>();
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
        if (!TargetPosition(out Vector3 targetPos)) return false;

        Camera cam = roverCamera.ActiveCamera;
        if (cam == null) return false;

        // Where the target is on the camera image (0..1 = inside)
        Vector3 vp = cam.WorldToViewportPoint(targetPos);
        bool inFront = vp.z > 0f;
        Rect rect = viewArea.rect;
        if (inFront && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f)
        {
            // in the frame: a rock / photo target gets the arrow above it, pointing down.
            // The finish line only when something (a slope, a rock) hides it from the camera.
            if (!targetIsData && !Hidden(cam.transform.position, targetPos)) return false;
            Vector3 top = cam.WorldToViewportPoint(targetTop + Vector3.up * markerHeight);
            if (top.z <= 0f) return false;
            Vector2 spot = rect.min + new Vector2(top.x * rect.width, top.y * rect.height);
            arrow.position = viewArea.TransformPoint(spot);
            arrow.rotation = viewArea.rotation * Quaternion.Euler(0f, 0f, -90f);   // art points right: -90 = down
            return true;
        }

        // Direction from the image center (flipped when the finish is behind the camera)
        Vector2 dir = new Vector2(vp.x - 0.5f, vp.y - 0.5f);
        if (!inFront) dir = -dir;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector2.down;     // straight behind: point down (turn around)

        // Push it out to the viewer's edge
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

    // What the mission needs next (from its Finish By); false = nothing to point at
    bool TargetPosition(out Vector3 position)
    {
        position = Vector3.zero;
        targetIsData = false;
        if (mission.rover == null) return false;
        Vector3 from = mission.rover.transform.position;

        // data first (target rocks, then photo targets), then the finish line
        if (mission.WantsAnalysisTarget && NearestRock(from, out position)) { targetIsData = true; return true; }
        if (mission.WantsPhotoTarget && NearestPhoto(from, out position)) { targetIsData = true; return true; }
        if (mission.WantsSampleTarget && NearestSample(from, out position)) { targetIsData = true; return true; }
        if (mission.WantsLander && NearestLander(from, out position)) { targetIsData = true; return true; }   // everything gathered: send it

        bool dataLeft = mission.WantsAnalysisTarget || mission.WantsPhotoTarget || mission.WantsSampleTarget
                        || mission.DataStillNeeded;
        if (mission.NeedsFinish && mission.finishLine != null && !dataLeft)
        {
            position = mission.finishLine.position;
            targetTop = position;
            return true;
        }
        return false;
    }

    bool NearestRock(Vector3 from, out Vector3 position)
    {
        position = Vector3.zero;
        float best = float.MaxValue;
        foreach (AnalysisTarget t in AnalysisTarget.All)
            if (t != null && !mission.rover.IsAnalyzed(t)) Closer(t.transform, from, ref best, ref position, ref targetTop);
        return best < float.MaxValue;
    }

    bool NearestSample(Vector3 from, out Vector3 position)
    {
        position = Vector3.zero;
        float best = float.MaxValue;
        foreach (SampleTarget t in SampleTarget.All)
            if (t != null && t.gameObject.activeInHierarchy && !mission.rover.IsCollected(t)) Closer(t.transform, from, ref best, ref position, ref targetTop);
        return best < float.MaxValue;
    }

    bool NearestLander(Vector3 from, out Vector3 position)
    {
        position = Vector3.zero;
        Lander lander = Lander.Nearest(from, out _);
        if (lander == null) return false;
        float best = float.MaxValue;
        Closer(lander.transform, from, ref best, ref position, ref targetTop);
        return true;
    }

    bool NearestPhoto(Vector3 from, out Vector3 position)
    {
        position = Vector3.zero;
        float best = float.MaxValue;
        foreach (PhotoTarget t in PhotoTarget.All)
            if (t != null && !mission.rover.IsPhotographed(t)) Closer(t.transform, from, ref best, ref position, ref targetTop);
        return best < float.MaxValue;
    }

    // Is something between the camera and this point? (the rover's own parts and the finish line itself don't count)
    readonly RaycastHit[] hits = new RaycastHit[16];

    bool Hidden(Vector3 eye, Vector3 point)
    {
        Vector3 target = point + Vector3.up * 0.2f;             // just above the ground, so the ground itself doesn't hide it
        Vector3 dir = target - eye;
        int count = Physics.RaycastNonAlloc(eye, dir, hits, dir.magnitude, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Transform t = hits[i].collider.transform;
            if (mission.rover != null && t.IsChildOf(mission.rover.transform)) continue;
            if (mission.finishLine != null && t.IsChildOf(mission.finishLine)) continue;
            return true;
        }
        return false;
    }

    // Keeps the nearest target (its collider's center, so the arrow points at the rock, not its pivot)
    static void Closer(Transform t, Vector3 from, ref float best, ref Vector3 position, ref Vector3 top)
    {
        Collider c = t.GetComponentInChildren<Collider>();
        Vector3 p = c != null ? c.bounds.center : t.position;
        float d = (p - from).sqrMagnitude;
        if (d < best)
        {
            best = d;
            position = p;
            top = c != null ? new Vector3(p.x, c.bounds.max.y, p.z) : p;
        }
    }
}
