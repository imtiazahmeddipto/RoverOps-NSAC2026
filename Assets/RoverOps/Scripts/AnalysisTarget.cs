using UnityEngine;

// Marks a rock the mission wants analyzed.
// Put it on the rock (or on a parent of its collider). Analyze Rock remembers whether the rock it drilled
// was a target; Send Data rejects analyses of other rocks ("WRONG ANALYSIS").
public class AnalysisTarget : MonoBehaviour
{
    public string targetName = "Target Rock";   // shown in the status, e.g. "Blue Rock"

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.4f, 0.9f, 0.9f);
        Collider c = GetComponentInChildren<Collider>();
        if (c != null) Gizmos.DrawWireCube(c.bounds.center, c.bounds.size * 1.15f);
        else Gizmos.DrawWireSphere(transform.position, 0.5f);
    }
}
