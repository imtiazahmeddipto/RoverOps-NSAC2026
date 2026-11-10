using System.Collections.Generic;
using UnityEngine;

// Marks the rock or sample a mission wants a photo of.
// Put it on the object (or on a parent of its collider). The object must be on the camera's
// rock or sample layer, so the onboard camera can detect it.
// Take Photo remembers whether a target was in view; Send Data rejects photos without one.
public class PhotoTarget : MonoBehaviour
{
    public string targetName = "Target";        // shown in the status, e.g. "Red Rock"

    static readonly List<PhotoTarget> all = new List<PhotoTarget>();
    public static IReadOnlyList<PhotoTarget> All => all;        // every photo target in the scene
    public static int Count => all.Count;                       // 0 = any photo counts

    void OnEnable() { if (!all.Contains(this)) all.Add(this); }
    void OnDisable() => all.Remove(this);

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
        Collider c = GetComponentInChildren<Collider>();
        if (c != null) Gizmos.DrawWireCube(c.bounds.center, c.bounds.size * 1.1f);
        else Gizmos.DrawWireSphere(transform.position, 0.5f);
    }
}
