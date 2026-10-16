using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The rover's camera: a real camera on the mast.
// It is always on: every frame it finds every object inside its picture, checks the object isn't hidden
// behind something else (line of sight), measures its distance from the rover's nose,
// tells rocks and samples apart, and draws a labelled box over each one in the Mission Viewer.
//
// Readings it gives the blocks:
//   RockDistance   nearest rock (m)            Infinity = no rock in view
//   SampleDistance nearest sample (m)          Infinity = no sample in view
//   SampleAngle    direction to that sample (degrees, + = right, - = left, 0 = none)
public class RoverCamera : MonoBehaviour
{
    [Header("Camera stats")]
    public float range = 8f;            // how far it can measure (m)
    public float viewAngle = 60f;       // horizontal field of view (degrees)

    [Header("Links")]
    public Camera onboardCamera;        // the camera on the rover's mast
    public Camera followCamera;         // the camera behind the rover
    public Transform nose;              // front of the rover: distances are measured from here
    public RectTransform overlay;       // area over the Mission Viewer picture
    public RectTransform boxTemplate;   // hidden detection box (frame image + label text)
    public TMP_Text viewButtonLabel;

    public float RockDistance { get; private set; } = Mathf.Infinity;
    public float SampleDistance { get; private set; } = Mathf.Infinity;
    public float SampleAngle { get; private set; }

    static readonly Color RockColor = new Color(1f, 0.8f, 0.25f), NearestColor = new Color(1f, 0.35f, 0.4f), SampleColor = new Color(0.3f, 1f, 0.9f);

    readonly List<Collider> seen = new List<Collider>();
    readonly List<float> seenDistance = new List<float>();
    readonly List<RectTransform> boxes = new List<RectTransform>();
    bool roverView;

    public static bool IsSample(Collider c) => c.name.StartsWith("Sample");

    void Awake()
    {
        SetLens(range, viewAngle);
        SetView(false);
        Clear();
    }

    void LateUpdate()
    {
        Scan();
        DrawBoxes();
    }

    void Clear()
    {
        RockDistance = SampleDistance = Mathf.Infinity;
        SampleAngle = 0f;
        seen.Clear();
        seenDistance.Clear();
    }

    // Range and field of view (set by the Camera part)
    public void SetLens(float newRange, float newViewAngle)
    {
        range = newRange;
        viewAngle = newViewAngle;
        RenderTexture picture = onboardCamera.targetTexture;
        onboardCamera.aspect = picture != null ? (float)picture.width / picture.height : 1.4f;
        onboardCamera.fieldOfView = Camera.HorizontalToVerticalFieldOfView(viewAngle, onboardCamera.aspect);
    }

    // Toggle button: Follow Cam <-> Rover Cam
    public void ToggleView() => SetView(!roverView);

    void SetView(bool rover)
    {
        roverView = rover;
        onboardCamera.enabled = rover;
        followCamera.enabled = !rover;
        if (viewButtonLabel) viewButtonLabel.text = rover ? "ROVER CAM" : "FOLLOW CAM";
    }

    // ---------------- DETECTION ----------------
    public void Scan()
    {
        Clear();

        Plane[] picture = GeometryUtility.CalculateFrustumPlanes(onboardCamera);
        Vector3 eye = onboardCamera.transform.position;
        foreach (Collider c in Physics.OverlapSphere(eye, range))
        {
            if (c.transform.IsChildOf(transform) || c.name == "Ground") continue;   // not the rover or the ground
            if (!GeometryUtility.TestPlanesAABB(picture, c.bounds)) continue;        // not inside the camera picture
            if (!VisiblePoint(c, eye, out Vector3 point)) continue;                  // hidden behind something

            float distance = GroundDistance(point);
            seen.Add(c);
            seenDistance.Add(distance);
            if (!IsSample(c)) RockDistance = Mathf.Min(RockDistance, distance);
            else if (distance < SampleDistance)
            {
                SampleDistance = distance;
                Vector3 toSample = c.bounds.center - transform.position;               // from the rover's centre (it turns around it)
                toSample.y = 0f;
                SampleAngle = Mathf.Round(Vector3.SignedAngle(transform.forward, toSample, Vector3.up) * 10f) / 10f;
            }
        }
    }

    // Aim at a few points on the object and keep the nearest one the camera can really see
    bool VisiblePoint(Collider c, Vector3 eye, out Vector3 best)
    {
        Bounds b = c.bounds;
        Vector3[] targets =
        {
            b.ClosestPoint(eye), b.center,
            new Vector3(b.min.x, b.center.y, b.center.z), new Vector3(b.max.x, b.center.y, b.center.z),
            new Vector3(b.center.x, b.max.y, b.center.z)
        };
        best = Vector3.zero;
        float bestDistance = Mathf.Infinity;
        foreach (Vector3 target in targets)
        {
            Vector3 direction = target - eye;
            if (Physics.Raycast(eye, direction, out RaycastHit hit, direction.magnitude + 0.1f)
                && hit.collider == c && hit.distance <= range)                          // first thing the ray hits is this object
            {
                float distance = GroundDistance(hit.point);
                if (distance < bestDistance) { bestDistance = distance; best = hit.point; }
            }
        }
        return bestDistance < Mathf.Infinity;
    }

    // Distance along the ground from the rover's nose to a point (height ignored)
    public float GroundDistance(Vector3 point)
    {
        Vector3 offset = point - nose.position;
        offset.y = 0f;
        return offset.magnitude;
    }

    // ---------------- BOXES IN THE MISSION VIEWER ----------------
    void DrawBoxes()
    {
        Camera view = roverView ? onboardCamera : followCamera;
        Vector2 size = overlay.rect.size;
        int used = 0;
        for (int i = 0; i < seen.Count; i++)
        {
            if (seen[i] == null || !seen[i].gameObject.activeInHierarchy) continue;
            if (!ViewportRect(view, seen[i].bounds, out Rect r)) continue;

            if (used == boxes.Count) boxes.Add(Instantiate(boxTemplate, overlay));
            RectTransform box = boxes[used++];
            box.gameObject.SetActive(true);
            box.anchoredPosition = new Vector2(r.xMin * size.x, r.yMin * size.y);
            box.sizeDelta = new Vector2(r.width * size.x, r.height * size.y);

            bool sample = IsSample(seen[i]);
            Color color = sample ? SampleColor : seenDistance[i] <= RockDistance ? NearestColor : RockColor;
            box.GetComponent<Image>().color = color;
            TMP_Text label = box.GetComponentInChildren<TMP_Text>();
            label.text = (sample ? "SAMPLE  " : "ROCK  ") + seenDistance[i].ToString("0.0") + " m";
            label.color = color;
        }
        for (int i = used; i < boxes.Count; i++) boxes[i].gameObject.SetActive(false);
    }

    // Where an object's bounding box appears in a camera picture (0..1 viewport coordinates)
    static bool ViewportRect(Camera cam, Bounds b, out Rect rect)
    {
        float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new Vector3(i % 2 == 0 ? b.min.x : b.max.x, i / 2 % 2 == 0 ? b.min.y : b.max.y, i < 4 ? b.min.z : b.max.z);
            Vector3 v = cam.WorldToViewportPoint(corner);
            if (v.z <= 0f) continue;                                  // behind the camera
            xMin = Mathf.Min(xMin, v.x); xMax = Mathf.Max(xMax, v.x);
            yMin = Mathf.Min(yMin, v.y); yMax = Mathf.Max(yMax, v.y);
        }
        rect = Rect.MinMaxRect(Mathf.Clamp01(xMin), Mathf.Clamp01(yMin), Mathf.Clamp01(xMax), Mathf.Clamp01(yMax));
        return xMax > xMin && rect.width > 0.005f && rect.height > 0.005f;
    }
}
