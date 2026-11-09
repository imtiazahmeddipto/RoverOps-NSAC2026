using System.Collections.Generic;
using UnityEngine;

// Marks a sample the mission wants collected.
// Put it on the sample (or on a parent of its collider). The sample must be on the camera's sample layer.
// With Finish By = CollectSamples and target samples in the scene, the rover must collect ALL of them,
// and collecting any other sample is an error ("WRONG SAMPLE").
public class SampleTarget : MonoBehaviour
{
    public string targetName = "Target Sample";   // shown in the status, e.g. "Blue Sample"

    // Kept even while the sample is hidden (a collected sample is switched off until Restart)
    static readonly List<SampleTarget> all = new List<SampleTarget>();
    public static int Count => all.Count;         // target samples in the scene (0 = Samples To Collect is used)
    public static IReadOnlyList<SampleTarget> All => all;

    void Awake() { if (!all.Contains(this)) all.Add(this); }
    void OnDestroy() => all.Remove(this);

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.2f, 1f, 0.5f, 0.9f);
        Collider c = GetComponentInChildren<Collider>();
        if (c != null) Gizmos.DrawWireCube(c.bounds.center, c.bounds.size * 1.2f);
        else Gizmos.DrawWireSphere(transform.position, 0.4f);
    }
}
