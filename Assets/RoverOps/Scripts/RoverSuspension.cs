using UnityEngine;

// Raycast suspension. Put it on the Rover object (next to Rover).
// Rover still decides WHERE the rover is on the map (exact metres and degrees).
// This script decides how it SITS there:
//   - every wheel looks for the ground under it and moves up or down on its spring
//   - every wheel rolls by exactly the distance it covers: driving, and turning on the spot
//     (the two sides roll opposite ways, the outer wheels further), spinning around its own centre
//   - the body follows the wheels: height, nose up/down (pitch) and side tilt (roll), with a little bounce
//   - over a hole, a wheel that can't reach the ground hangs down and the body leans onto the wheels
//     that still touch; with no wheel on the ground it falls (never faster than gravity)
// Place the rover in the scene with its wheels touching the ground: that is its resting height.
[RequireComponent(typeof(Rover))]
public class RoverSuspension : MonoBehaviour
{
    [Header("Springs")]
    public float travel = 0.15f;            // how far each wheel can move up or down from rest (m)
    public float stiffness = 120f;          // body height spring (higher = stiffer)
    public float damping = 14f;             // body height damper (higher = less bounce)
    public float tiltStiffness = 90f;       // pitch / roll spring
    public float tiltDamping = 12f;         // pitch / roll damper

    [Header("Gravity")]
    public float gravity = 3.71f;           // Mars gravity (m/s²). Earth = 9.81
    public float maxTilt = 60f;             // the body never tilts further than this (degrees)

    [Header("Ground")]
    public LayerMask groundLayers = ~0;     // what the wheels can stand on (samples and the rover itself are always skipped)
    public float probeDepth = 6f;           // how deep a wheel looks for ground under it (m)

    Rover rover;
    Rigidbody rb;

    Transform[] wheels;                     // the wheels this was set up for (they change when parts change)
    Vector3[] bottom;                       // each wheel's lowest point at rest, in the rover's own space
    Vector3[] restLocal, upLocal;           // each wheel's rest position, and the rover's "up" in the wheel's parent space
    Quaternion[] restRotation;              // each wheel's rest rotation
    Vector3[] centerLocal, axleLocal;       // each wheel's centre and axle (the rover's "right") in its parent space
    float[] radius, spin;                   // each wheel's radius (m) and how far it has rolled (degrees)
    float[] ground, offset;                 // ground height under each wheel; how far each wheel is pushed up (+) or hangs (-)
    bool[] onGround;

    float height, pitch, roll;              // the body's pose (m, degrees)
    float vHeight, vPitch, vRoll;           // how fast it changes

    void Awake()
    {
        rover = GetComponent<Rover>();
        rb = GetComponent<Rigidbody>();
        height = transform.position.y;
    }

    // ---------------- CALLED BY ROVER ----------------
    // Every physics step: spring the body and wheels toward the ground at this spot
    public void Place(Vector3 flat, float heading)
    {
        if (wheels != rover.wheels) Setup();
        if (wheels.Length == 0) { height = flat.y; pitch = roll = 0f; Apply(flat, heading, false); return; }
        Step(flat, heading, Time.fixedDeltaTime);
        Apply(flat, heading, false);
    }

    // Start / Restart: put the rover down at once, settled, no bounce. flat.y = the height to start looking from
    public void Snap(Vector3 flat, float heading)
    {
        if (wheels != rover.wheels) Setup();
        height = flat.y;
        pitch = roll = 0f;
        vHeight = vPitch = vRoll = 0f;
        for (int k = 0; k < 4 && wheels.Length > 0; k++)        // where the wheels touch depends on the pose: settle in a few passes
        {
            Sense(flat, heading);
            if (!Targets(out float h, out float p, out float r)) break;
            height = h; pitch = p; roll = r;
        }
        if (wheels.Length > 0) { Sense(flat, heading); UpdateOffsets(); }
        Apply(flat, heading, true);
    }

    // Rover calls this when it drives (forward, m) or turns on the spot (degrees, + = right)
    public void Roll(float forward, float turnDegrees)
    {
        if (wheels != rover.wheels) Setup();
        for (int i = 0; i < wheels.Length; i++)
        {
            // turning on the spot: each wheel covers turn angle x its distance sideways from the middle;
            // the left side (x < 0) goes forward when turning right, the right side goes backward
            float distance = forward - turnDegrees * Mathf.Deg2Rad * bottom[i].x;
            spin[i] = Mathf.Repeat(spin[i] + distance / radius[i] * Mathf.Rad2Deg, 360f);
        }
    }

    // ---------------- SETUP ----------------
    void Setup()
    {
        wheels = rover.wheels ?? new Transform[0];
        int n = wheels.Length;
        bottom = new Vector3[n];
        restLocal = new Vector3[n];
        upLocal = new Vector3[n];
        ground = new float[n];
        offset = new float[n];
        onGround = new bool[n];
        restRotation = new Quaternion[n];
        centerLocal = new Vector3[n];
        axleLocal = new Vector3[n];
        radius = new float[n];
        spin = new float[n];
        for (int i = 0; i < n; i++)
        {
            Transform w = wheels[i];
            Transform parent = w.parent != null ? w.parent : transform;
            Renderer look = w.GetComponent<Renderer>();
            Vector3 center = look != null ? look.bounds.center : w.position;           // the real middle of the wheel (its pivot may be elsewhere)
            radius[i] = look != null ? Mathf.Max(0.05f, look.bounds.extents.y) : Mathf.Max(0.05f, rover.wheelRadius);

            restLocal[i] = w.localPosition;
            restRotation[i] = w.localRotation;
            upLocal[i] = parent.InverseTransformVector(transform.up);
            centerLocal[i] = parent.InverseTransformPoint(center);
            axleLocal[i] = parent.InverseTransformDirection(transform.right).normalized;

            Vector3 p = transform.InverseTransformPoint(center);
            bottom[i] = new Vector3(p.x, p.y - radius[i], p.z);
        }
    }

    // ---------------- SIMULATION ----------------
    void Step(Vector3 flat, float heading, float dt)
    {
        Sense(flat, heading);
        if (Targets(out float h, out float p, out float r))
        {
            // the ground pushes up as hard as it needs to, but nothing pulls the rover down faster than gravity
            vHeight += Mathf.Max(stiffness * (h - height) - damping * vHeight, -gravity) * dt;
            vPitch += (tiltStiffness * Mathf.DeltaAngle(pitch, p) - tiltDamping * vPitch) * dt;
            vRoll += (tiltStiffness * Mathf.DeltaAngle(roll, r) - tiltDamping * vRoll) * dt;
        }
        else vHeight -= gravity * dt;                           // no wheel on the ground: free fall
        height += vHeight * dt;
        pitch += vPitch * dt;
        roll += vRoll * dt;
        if (Mathf.Abs(pitch) > maxTilt) { pitch = Mathf.Clamp(pitch, -maxTilt, maxTilt); vPitch = 0f; }
        if (Mathf.Abs(roll) > maxTilt) { roll = Mathf.Clamp(roll, -maxTilt, maxTilt); vRoll = 0f; }
        UpdateOffsets();
    }

    // Looks for the ground under every wheel, and whether the wheel's spring can reach it
    void Sense(Vector3 flat, float heading)
    {
        Quaternion yaw = Quaternion.Euler(0f, heading, 0f);
        for (int i = 0; i < wheels.Length; i++)
        {
            Vector3 at = flat + yaw * new Vector3(bottom[i].x, 0f, bottom[i].z);
            float rest = BottomY(i, height, pitch, roll);
            bool hit = FindGround(new Vector3(at.x, rest + 1.5f, at.z), 1.5f + probeDepth, out ground[i]);
            onGround[i] = hit && ground[i] >= rest - travel;
        }
    }

    // The pose that puts the wheels touching the ground onto it. False = no wheel touches the ground.
    bool Targets(out float h, out float p, out float r)
    {
        h = height; p = pitch; r = roll;

        int n = 0;
        float mx = 0f, mz = 0f, mg = 0f;
        for (int i = 0; i < wheels.Length; i++)
        {
            if (!onGround[i]) continue;
            n++;
            mx += bottom[i].x; mz += bottom[i].z; mg += ground[i];
        }
        if (n == 0) return false;
        mx /= n; mz /= n; mg /= n;

        // slope of the ground under the touching wheels (least squares); keep the current tilt where the wheels can't tell
        float sxx = 0f, szz = 0f, sxz = 0f, sxg = 0f, szg = 0f;
        for (int i = 0; i < wheels.Length; i++)
        {
            if (!onGround[i]) continue;
            float x = bottom[i].x - mx, z = bottom[i].z - mz, g = ground[i] - mg;
            sxx += x * x; szz += z * z; sxz += x * z; sxg += x * g; szg += z * g;
        }
        float slopeX = Mathf.Tan(roll * Mathf.Deg2Rad), slopeZ = -Mathf.Tan(pitch * Mathf.Deg2Rad);
        float det = sxx * szz - sxz * sxz;
        if (det > 1e-4f) { slopeX = (sxg * szz - szg * sxz) / det; slopeZ = (szg * sxx - sxg * sxz) / det; }
        else if (sxx > 1e-3f) slopeX = sxg / sxx;
        else if (szz > 1e-3f) slopeZ = szg / szz;
        p = -Mathf.Atan(slopeZ) * Mathf.Rad2Deg;                 // ground rising ahead = nose up
        r = Mathf.Atan(slopeX) * Mathf.Rad2Deg;                  // ground rising on the right = right side up

        // height that puts the touching wheels on the ground (on average)
        Quaternion tilt = Quaternion.Euler(p, 0f, r);
        float sum = 0f;
        for (int i = 0; i < wheels.Length; i++)
            if (onGround[i]) sum += ground[i] - (tilt * bottom[i]).y;
        h = sum / n;
        return true;
    }

    // How far each wheel is pushed up by the ground (+) or hangs down (-), within its travel
    void UpdateOffsets()
    {
        for (int i = 0; i < wheels.Length; i++)
            offset[i] = onGround[i] ? Mathf.Clamp(ground[i] - BottomY(i, height, pitch, roll), -travel, travel) : -travel;
    }

    void Apply(Vector3 flat, float heading, bool teleport)
    {
        Vector3 position = new Vector3(flat.x, height, flat.z);
        Quaternion rotation = Quaternion.Euler(0f, heading, 0f) * Quaternion.Euler(pitch, 0f, roll);
        if (teleport)
        {
            rb.position = position;
            rb.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
        }
        else
        {
            rb.MovePosition(position);
            rb.MoveRotation(rotation);
        }
        // wheels: roll around their own centre, then move up or down on the spring
        for (int i = 0; i < wheels.Length; i++)
        {
            if (wheels[i] == null) continue;
            Quaternion turn = Quaternion.AngleAxis(spin[i], axleLocal[i]);
            wheels[i].localRotation = turn * restRotation[i];
            wheels[i].localPosition = centerLocal[i] + turn * (restLocal[i] - centerLocal[i]) + upLocal[i] * offset[i];
        }
    }

    // ---------------- HELPERS ----------------
    // World height of a wheel's lowest point at rest, for a body pose
    float BottomY(int i, float h, float p, float r) => h + (Quaternion.Euler(p, 0f, r) * bottom[i]).y;

    // The nearest ground under a point (not the rover, not samples, not triggers)
    bool FindGround(Vector3 from, float distance, out float y)
    {
        y = 0f;
        float best = Mathf.Infinity;
        foreach (RaycastHit hit in Physics.RaycastAll(from, Vector3.down, distance, groundLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(transform) || RoverCamera.IsSample(hit.collider)) continue;
            if (hit.distance < best) { best = hit.distance; y = hit.point.y; }
        }
        return best < Mathf.Infinity;
    }
}