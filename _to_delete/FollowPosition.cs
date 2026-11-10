using UnityEngine;

// Follows a target's POSITION only: it never turns with it (keeps its own rotation / direction).
// E.g. a dust / weather particle system that stays around the rover but always blows the same way.
// Put it on the particle system (NOT a child of the rover) and drag the rover into Target.
public class FollowPosition : MonoBehaviour
{
    public Transform target;                    // the rover
    public bool keepOffset = true;              // stay where you placed it relative to the target (else sit on it)
    public bool followHeight = true;            // also move up / down with the target (hills)
    [Min(0f)] public float smoothing = 0f;      // 0 = stick exactly; higher = glide behind a little

    Vector3 offset;
    Quaternion fixedRotation;

    void Start()
    {
        fixedRotation = transform.rotation;                     // the direction it keeps forever
        if (target != null && keepOffset) offset = transform.position - target.position;
    }

    void LateUpdate()                                           // after the rover has moved this frame
    {
        if (target == null) return;
        Vector3 goal = target.position + offset;
        if (!followHeight) goal.y = transform.position.y;

        transform.position = smoothing > 0f
            ? Vector3.Lerp(transform.position, goal, 1f - Mathf.Exp(-Time.deltaTime / smoothing))
            : goal;
        transform.rotation = fixedRotation;                     // never turns with the rover
    }
}
