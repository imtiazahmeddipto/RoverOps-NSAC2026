using System.Collections.Generic;
using UnityEngine;

// The lander: the rover's Send Data block reaches it only within 'range' metres
// (measured on the ground, x / z, from the rover to the lander's position).
// Put it on the lander object in a mission scene. The green ring in the Scene view shows the range.
// For the camera to SEE it (lander detected / dist / angle), put its collider on the Lander layer
// and set that layer in RoverCamera's Lander Layer.
public class Lander : MonoBehaviour
{
    [Min(0f)] public float range = 10f;             // metres

    static readonly List<Lander> all = new List<Lander>();
    public static int Count => all.Count;

    void OnEnable() { if (!all.Contains(this)) all.Add(this); }
    void OnDisable() => all.Remove(this);

    // Ground distance (x / z) from a point to this lander
    public float DistanceTo(Vector3 point)
    {
        Vector3 offset = point - transform.position;
        offset.y = 0f;
        return offset.magnitude;
    }

    public bool InRange(Vector3 point) => DistanceTo(point) <= range;

    // The closest lander to a point (null if the scene has none)
    public static Lander Nearest(Vector3 point, out float distance)
    {
        Lander best = null;
        distance = Mathf.Infinity;
        foreach (Lander lander in all)
        {
            if (lander == null) continue;
            float d = lander.DistanceTo(point);
            if (d < distance) { distance = d; best = lander; }
        }
        return best;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.9f);
        const int segments = 64;
        Vector3 center = transform.position + Vector3.up * 0.05f;
        Vector3 last = center + new Vector3(range, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            Vector3 next = center + new Vector3(Mathf.Cos(a) * range, 0f, Mathf.Sin(a) * range);
            Gizmos.DrawLine(last, next);
            last = next;
        }
    }
}
