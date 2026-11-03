using UnityEngine;

// Marks the rock or sample a mission wants a photo of.
// Put it on the object (or on a parent of its collider). The object must be on the camera's
// rock or sample layer, so the onboard camera can detect it.
// Take Photo remembers whether a target was in view; Send Data rejects photos without one.
public class PhotoTarget : MonoBehaviour
{
    public string targetName = "Target";        // shown in the status, e.g. "Red Rock"

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
        Collider c = GetComponentInChildren<Collider>();
        if (c != null) Gizmos.DrawWireCube(c.bounds.center, c.bounds.size * 1.1f);
        else Gizmos.DrawWireSphere(transform.position, 0.5f);
    }
}
