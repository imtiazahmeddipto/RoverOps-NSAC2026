using System.Collections;
using UnityEngine;

// Drill arm animation. Put it on the Rover object (next to Rover and RoverLoadout).
//   Drill 1  - the base: turns only sideways (around the rover's up)       found by name
//   Drill 2  - the arm:  tilts only up / down, toward the sample            RoverLoadout's Drill Mount (or by name)
// The arm moves only while the rover drills: the base turns, then the arm tilts; afterwards it folds back.
public class RoverDrill : MonoBehaviour
{
    [Header("Names")]
    public string baseName = "Drill 1";
    public string armName = "Drill 2";      // only used if RoverLoadout has no Drill Mount

    [Header("Motion")]
    public float armSpeed = 90f;            // how fast the base and arm turn (degrees per second)

    RoverLoadout loadout;
    Transform drillBase, arm;
    Quaternion baseRest, armRest;
    Vector3 baseAxis, armAxis;              // turning axes, in each joint's parent space
    float yaw, pitch;                       // current base turn and arm tilt (degrees)

    IEnumerator Start()
    {
        loadout = GetComponent<RoverLoadout>();
        yield return null;                  // wait one frame: RoverLoadout mounts the parts in its Start
        Setup();
    }

    // ---------------- CALLED BY ROVER ----------------
    // Turn the base, then tilt the arm, so the arm's end points at the sample
    public IEnumerator Reach(Vector3 target)
    {
        if (!Ready()) yield break;
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
        float pitchTarget = Vector3.SignedAngle(Vector3.ProjectOnPlane(End() - arm.position, hinge),
                                                Vector3.ProjectOnPlane(target - arm.position, hinge), hinge);
        Pose(0f, 0f);

        yield return Move(yawTarget, 0f);               // Drill 1 turns first
        yield return Move(yawTarget, pitchTarget);      // then Drill 2 tilts
    }

    // Fold back: arm up first, then the base turns back
    public IEnumerator Fold()
    {
        if (!Ready()) yield break;
        yield return Move(yaw, 0f);
        yield return Move(0f, 0f);
    }

    // Restart: straight back to rest
    public void ResetPose()
    {
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