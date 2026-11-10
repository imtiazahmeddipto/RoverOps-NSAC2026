using UnityEngine;

// A solar storm that stays around the rover (e.g. on the storm's particle system).
//   - follows the rover's POSITION only: it never turns, so the storm always blows the same way
//   - while this object is active, the rover is slower and uses more power (always, no collider needed)
// Put it on the storm (NOT a child of the rover). Rover is found by itself if left empty.
// Particle System tip: Main > Simulation Space = World, so the dust drifts naturally while the storm follows.
public class SolarStorm : MonoBehaviour
{
    public Rover rover;                                         // found by itself if empty

    [Header("Storm effect")]
    [Range(0.05f, 1f)] public float speedMultiplier = 0.5f;     // 0.5 = half the top speed
    [Min(1f)] public float powerMultiplier = 1.2f;              // 1.2 = 20% more power used (driving, turning, drill, camera...)

    [Header("Follow the rover")]
    public bool keepOffset = true;                              // stay where you placed it relative to the rover (else sit on it)
    public bool followHeight = true;                            // also move up / down with the rover (hills)
    [Min(0f)] public float smoothing = 0f;                      // 0 = stick exactly; higher = glide behind a little

    Vector3 offset;
    Quaternion fixedRotation;

    void Awake()
    {
        if (rover == null) rover = FindAnyObjectByType<Rover>();
        fixedRotation = transform.rotation;                     // the direction it keeps forever
        if (rover != null && keepOffset) offset = transform.position - rover.transform.position;
    }

    // the effect lasts exactly as long as the storm is active
    void OnEnable() { if (rover != null) rover.EnterStorm(this); }
    void OnDisable() { if (rover != null) rover.ExitStorm(this); }

    void LateUpdate()                                           // after the rover has moved this frame
    {
        if (rover == null) return;
        Vector3 goal = rover.transform.position + offset;
        if (!followHeight) goal.y = transform.position.y;

        transform.position = smoothing > 0f
            ? Vector3.Lerp(transform.position, goal, 1f - Mathf.Exp(-Time.deltaTime / smoothing))
            : goal;
        transform.rotation = fixedRotation;                     // never turns with the rover
    }
}
