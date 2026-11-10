using System.Collections;
using UnityEngine;

// Drill arm animation. Put it on the Rover object (next to Rover and RoverLoadout).
//   Drill 1  - the base: turns only sideways (around the rover's up)       found by name
//   Drill 2  - the arm:  tilts only up / down, toward the sample            RoverLoadout's Drill Mount (or by name)
// The arm moves only while the rover drills: the base turns, then the arm tilts; afterwards it folds back.
// The arm aims its TIP at the object's upper surface, then lowers from rest in small steps and stops where
// it first TOUCHES the object (any part of the arm, not just the tip), so it never goes through it.
// A tip touch digs drillDepth into the surface; a touch further up the arm just rests there.
// Then the drill head (the Science part RoverLoadout mounts on the arm) turns on its own local X axis only,
// around its pivot, so the line pivot -> drill tip points at the drill point as close as X allows.
// It turns back before the arm folds.
public class RoverDrill : MonoBehaviour
{
    [Header("Names")]
    public string baseName = "Drill 1";
    public string armName = "Drill 2";      // only used if RoverLoadout has no Drill Mount

    [Header("Motion")]
    public float armSpeed = 90f;            // how fast the base and arm turn (degrees per second)
    public float drillDepth = 0.1f;         // how far the tip digs in after touching the surface (m)
    public float armRadius = 0.03f;         // how thick the arm is, for the touch test (m)

    [Header("Drill head")]
    public string headPrefix = "Science";   // RoverLoadout names the mounted drill part "Science: <part name>"
    public float headMaxAngle = 60f;        // the head turns at most this far (local X, either way, degrees)

    RoverLoadout loadout;
    Transform drillBase, arm;
    Quaternion baseRest, armRest;
    Vector3 baseAxis, armAxis;              // turning axes, in each joint's parent space
    float yaw, pitch;                       // current base turn and arm tilt (degrees)

    Transform head;                         // the drill head, found on the arm each time
    Quaternion headRestRot;                 // how it was mounted
    float headTurn;                         // its local X turn now (degrees, signed)

    IEnumerator Start()
    {
        loadout = GetComponent<RoverLoadout>();
        yield return null;                  // wait one frame: RoverLoadout mounts the parts in its Start
        Setup();
    }

    // ---------------- CALLED BY ROVER ----------------
    // Turn the base, then tilt the arm, so the arm's tip reaches the top of the object at 'target'
    // (the Rover passes the object's middle; the drill point is worked out from it)
    public IEnumerator Reach(Vector3 target)
    {
        if (!Ready()) yield break;
        RestoreHead();
        target = SurfacePoint(target, out Collider body);
        Vector3 up = transform.up;
        Pose(0f, 0f);

        // sideways: turn the base so the arm's end is in line with the sample (seen from above)
        Vector3 to = Vector3.ProjectOnPlane(target - drillBase.position, up);
        float yawTarget = Vector3.SignedAngle(Vector3.ProjectOnPlane(End() - drillBase.position, up), to, up);

        // up / down: with the base turned, tilt the arm around a level hinge so its end points at the sample
        Pose(yawTarget, 0f);
        Vector3 hinge = Vector3.Cross(up, to);
        if (hinge.sqrMagnitude < 1e-4f) hinge = transform.right;
        hinge.Normalize();
        armAxis = arm.parent.InverseTransformDirection(hinge);
        float pitchTarget = Vector3.SignedAngle(Vector3.ProjectOnPlane(Tip() - arm.position, hinge),
                                                Vector3.ProjectOnPlane(target - arm.position, hinge), hinge);
        pitchTarget = TouchPitch(body, yawTarget, pitchTarget);
        Pose(0f, 0f);

        yield return Move(yawTarget, 0f);               // Drill 1 turns first
        yield return Move(yawTarget, pitchTarget);      // then Drill 2 tilts
        yield return TurnHead(target);                  // then the drill head points at the drill point
    }

    // Fold back: arm up first, then the base turns back
    public IEnumerator Fold()
    {
        if (!Ready()) yield break;
        yield return TurnHeadBack();                    // head straight first
        yield return Move(yaw, 0f);
        yield return Move(0f, 0f);
    }

    // Restart: straight back to rest
    public void ResetPose()
    {
        RestoreHead();
        if (Ready()) Pose(0f, 0f);
    }

    // ---------------- SETUP ----------------
    void Setup()
    {
        if (loadout == null) loadout = GetComponent<RoverLoadout>();
        drillBase = Find(baseName);
        arm = loadout != null && loadout.drillMount != null ? loadout.drillMount : Find(armName);
        if (drillBase == null || arm == null)
        {
            Debug.LogWarning("[Drill] Can't find the base '" + baseName + "' or the arm (Drill Mount or '" + armName + "').");
            return;
        }
        if (!arm.IsChildOf(drillBase)) arm.SetParent(drillBase, true);   // the arm turns with the base

        baseRest = drillBase.localRotation;
        armRest = arm.localRotation;
        baseAxis = drillBase.parent.InverseTransformDirection(transform.up);
        armAxis = drillBase.InverseTransformDirection(transform.right);
        yaw = pitch = 0f;
    }

    bool Ready()
    {
        if (drillBase == null || arm == null) Setup();
        return drillBase != null && arm != null;
    }

    // ---------------- MOTION ----------------
    void Pose(float y, float p)
    {
        yaw = y;
        pitch = p;
        drillBase.localRotation = Quaternion.AngleAxis(y, baseAxis) * baseRest;
        arm.localRotation = Quaternion.AngleAxis(p, armAxis) * armRest;
    }

    IEnumerator Move(float y, float p)
    {
        while (Mathf.Abs(Mathf.DeltaAngle(yaw, y)) > 0.01f || Mathf.Abs(Mathf.DeltaAngle(pitch, p)) > 0.01f)
        {
            float step = armSpeed * Time.deltaTime;
            Pose(Mathf.MoveTowardsAngle(yaw, y, step), Mathf.MoveTowardsAngle(pitch, p, step));
            yield return null;
        }
        Pose(y, p);
    }

    // ---------------- DRILL POINT ----------------
    // Straight down onto the object at 'point': its upper surface, drillDepth below it.
    // Skips the rover itself and anything above that isn't this object. No hit: the point as given.
    Vector3 SurfacePoint(Vector3 point, out Collider body)
    {
        bool found = false;
        Vector3 top = point;
        body = null;
        foreach (RaycastHit hit in Physics.RaycastAll(point + Vector3.up * 10f, Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(transform)) continue;          // the rover's own parts
            if (!hit.collider.bounds.Contains(point)) continue;                 // something else above the object
            if (!found || hit.point.y > top.y) { top = hit.point; body = hit.collider; found = true; }
        }
        return found ? top + Vector3.down * drillDepth : point;
    }

    // Lower the arm from rest toward 'aim' in small steps and stop where it first touches the object.
    // Touch at the tip: dig drillDepth further. Touch further up the arm: rest there.
    // No touch on the way: 'aim' (the tip points at the drill point). Poses for testing; Reach resets after.
    float TouchPitch(Collider body, float yaw, float aim)
    {
        if (body == null) return aim;
        const float step = 0.5f;                                    // degrees per test
        int steps = Mathf.CeilToInt(Mathf.Abs(aim) / step);
        for (int s = 0; s <= steps; s++)
        {
            float p = Mathf.MoveTowards(0f, aim, s * step);
            Pose(yaw, p);
            if (!Touches(body, out bool atTip)) continue;
            if (!atTip) return p;                                   // the arm's side rests on the object
            float length = Vector3.Distance(arm.position, Tip());
            float dig = length > 0.01f ? drillDepth / length * Mathf.Rad2Deg : 0f;   // tip moves drillDepth deeper
            return p + Mathf.Sign(aim) * dig;
        }
        return aim;
    }

    // Does the arm (a line of small spheres from its joint to its tip) touch the object?
    // atTip: the touch is at the tip end.
    bool Touches(Collider body, out bool atTip)
    {
        const int points = 12;
        Vector3 joint = arm.position, tip = Tip();
        atTip = false;
        for (int i = points; i >= 1; i--)                           // tip first
        {
            Vector3 point = Vector3.Lerp(joint, tip, i / (float)points);
            foreach (Collider c in Physics.OverlapSphere(point, armRadius, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c != body) continue;
                atTip = i >= points - 1;
                return true;
            }
        }
        return false;
    }

    // ---------------- DRILL HEAD ----------------
    // Turn the head on its local X axis only (around its pivot) so pivot -> drill tip points at the target
    IEnumerator TurnHead(Vector3 target)
    {
        head = FindHead();
        if (head == null) yield break;
        headRestRot = head.localRotation;

        Vector3 pivot = head.position;                                  // the drill's own pivot
        Vector3 axis = head.right;                                      // its local X
        Vector3 from = Vector3.ProjectOnPlane(HeadTip() - pivot, axis);                // pivot -> drill tip
        Vector3 to = Vector3.ProjectOnPlane(target - pivot, axis);                    // pivot -> drill point
        if (from.sqrMagnitude < 1e-6f || to.sqrMagnitude < 1e-6f) yield break;

        float angle = Mathf.Clamp(Vector3.SignedAngle(from, to, axis), -headMaxAngle, headMaxAngle);
        while (Mathf.Abs(angle - headTurn) > 0.01f)
        {
            SetHeadTurn(Mathf.MoveTowards(headTurn, angle, armSpeed * Time.deltaTime));
            yield return null;
        }
        SetHeadTurn(angle);
    }

    IEnumerator TurnHeadBack()
    {
        while (head != null && Mathf.Abs(headTurn) > 0.01f)
        {
            SetHeadTurn(Mathf.MoveTowards(headTurn, 0f, armSpeed * Time.deltaTime));
            yield return null;
        }
        RestoreHead();
    }

    void SetHeadTurn(float x)
    {
        headTurn = x;
        head.localRotation = headRestRot * Quaternion.Euler(x, 0f, 0f);  // X only
    }

    // Exactly back to how it was mounted
    void RestoreHead()
    {
        if (head != null && headTurn != 0f) head.localRotation = headRestRot;
        headTurn = 0f;
    }

    // The drill tip: a child named "Tip" if the drill prefab has one; otherwise, in the drill's own space,
    // the middle of the box face farthest from its pivot (for a cone pointing along an axis: its point)
    Vector3 HeadTip()
    {
        foreach (Transform t in head.GetComponentsInChildren<Transform>(true))
            if (t != head && t.name == "Tip") return t.position;

        bool any = false;
        Bounds box = new Bounds();
        foreach (Renderer r in head.GetComponentsInChildren<Renderer>())
        {
            Bounds b = r.localBounds;
            Matrix4x4 toHead = head.worldToLocalMatrix * r.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = toHead.MultiplyPoint3x4(new Vector3(
                    i % 2 == 0 ? b.min.x : b.max.x, i / 2 % 2 == 0 ? b.min.y : b.max.y, i < 4 ? b.min.z : b.max.z));
                if (!any) { box = new Bounds(corner, Vector3.zero); any = true; }
                else box.Encapsulate(corner);
            }
        }
        if (!any) return head.position;

        // the pivot is (0,0,0) here: pick the face that reaches farthest from it
        Vector3 c = box.center, tip = c;
        float far = -1f;
        void Face(float distance, Vector3 point) { if (distance > far) { far = distance; tip = point; } }
        Face(box.max.x, new Vector3(box.max.x, c.y, c.z));
        Face(-box.min.x, new Vector3(box.min.x, c.y, c.z));
        Face(box.max.y, new Vector3(c.x, box.max.y, c.z));
        Face(-box.min.y, new Vector3(c.x, box.min.y, c.z));
        Face(box.max.z, new Vector3(c.x, c.y, box.max.z));
        Face(-box.min.z, new Vector3(c.x, c.y, box.min.z));
        return head.TransformPoint(tip);
    }

    Transform FindHead()
    {
        if (arm == null) return null;
        foreach (Transform t in arm.GetComponentsInChildren<Transform>(true))
            if (t != arm && t.name.StartsWith(headPrefix)) return t;
        return null;
    }

    // The drill bit: the point of the arm (and what hangs on it) farthest from the arm's joint
    Vector3 Tip() => Farthest(arm, arm.position);

    // The point of everything under 'root' farthest from 'from' (corners of each model's own box)
    static Vector3 Farthest(Transform root, Vector3 from)
    {
        Vector3 best = from;
        float bestDistance = -1f;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
        {
            Bounds b = r.localBounds;                    // the model's own box, turned with it
            Matrix4x4 toWorld = r.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = toWorld.MultiplyPoint3x4(new Vector3(
                    i % 2 == 0 ? b.min.x : b.max.x, i / 2 % 2 == 0 ? b.min.y : b.max.y, i < 4 ? b.min.z : b.max.z));
                float d = (corner - from).sqrMagnitude;
                if (d > bestDistance) { bestDistance = d; best = corner; }
            }
        }
        return best;
    }

    // ---------------- HELPERS ----------------
    // The middle of everything on the arm (Drill 2 and what hangs on it)
    Vector3 End()
    {
        Renderer[] looks = arm.GetComponentsInChildren<Renderer>();
        if (looks.Length == 0) return arm.position;
        Bounds b = looks[0].bounds;
        foreach (Renderer r in looks) b.Encapsulate(r.bounds);
        return b.center;
    }

    Transform Find(string name)
    {
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }
}