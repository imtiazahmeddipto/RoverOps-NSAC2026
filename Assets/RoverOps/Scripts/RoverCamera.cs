using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

// Rover camera system:
// - Separate Render Textures for Rover Camera and Follow Camera.
// - One Mission Viewer Raw Image.
// - Object detection always uses the onboard Rover Camera.
// - Detection boxes follow the currently displayed camera.

public class RoverCamera : MonoBehaviour
{
    [Header("Camera Stats")]
    public float range = 8f;
    public float viewAngle = 60f;
    [Tooltip("Rocks / samples closer than this (m) right ahead are still measured after they leave the bottom of the picture")]
    public float nearRange = 3f;

    [Header("Detection Layers")]
    public LayerMask rockLayer;
    public LayerMask sampleLayer;
    public LayerMask landerLayer;       // the Lander's layer: the camera sees it as a target (not a rock)

    [Header("Cameras")]
    public Camera onboardCamera;
    public Camera followCamera;

    [Header("Render Textures")]
    public RenderTexture roverCameraTexture;
    public RenderTexture followCameraTexture;

    [Header("Mission Viewer")]
    public Transform nose;
    public RawImage missionViewerImage;
    public RectTransform overlay;
    public RectTransform boxTemplate;
    public Button viewToggleButton;
    public TMP_Text viewButtonLabel;

    [Header("Global Volumes")]
    public GameObject roverGlobalVolume;
    public GameObject followGlobalVolume;

    [System.Serializable]
    public class CameraVolumeProfile
    {
        public string cameraName;
        public VolumeProfile profile;
    }

    public CameraVolumeProfile[] cameraProfiles;


    public float RockDistance { get; private set; } = Mathf.Infinity;
    public float SampleDistance { get; private set; } = Mathf.Infinity;
    public float SampleAngle { get; private set; }
    public float RockAngle { get; private set; }       // nearest rock: + right, - left (degrees)
    public float LanderDistance { get; private set; } = Mathf.Infinity;
    public float LanderAngle { get; private set; }     // nearest lander: + right, - left (degrees)

    // ROCK SIZE / SAMPLE SIZE: only the HD Cam can measure them
    // (the biggest side of the nearest rock / sample, in metres; 0 = none in view or no HD Cam)
    public const string HDCameraName = "HD Cam";               // the part name in the Part Database
    public static bool HasHDCamera { get; private set; }       // static so the Code Editor's chips can ask too
    public float RockSize { get; private set; }
    public float SampleSize { get; private set; }

    public static bool IsHDOnly(string reading)
    {
        if (reading == null) return false;
        string key = reading.Replace(" ", "").ToLowerInvariant();
        return key == "rocksize" || key == "samplesize";
    }

    static readonly Color RockColor = Color.white;
    static readonly Color NearestColor = Color.red;
    static readonly Color SampleColor = new Color(0.2f, 1f, 0.3f);
    static readonly Color LanderColor = new Color(0.3f, 0.8f, 1f);

    readonly List<Collider> seen = new List<Collider>();
    readonly List<float> seenDistance = new List<float>();
    readonly List<RectTransform> boxes = new List<RectTransform>();
    readonly List<TMP_Text> nameLabels = new List<TMP_Text>();   // "ROCK 0.0 m" / "SAMPLE 0.0 m" (NameTXT in the box template)
    readonly List<TMP_Text> sizeLabels = new List<TMP_Text>();
    // the name label's own place in the box template (lander labels move to the center, others keep this)
    bool labelSaved;
    Vector2 labelAnchorMin, labelAnchorMax, labelPivot, labelPos, labelSize;
    TextAlignmentOptions labelAlignment;   // "SIZE 0.0 m", HD Cam only (SizeTXT in the box template)
    bool hasCamera = true;              // false only when "No Camera" is fitted
    public bool HasCamera => hasCamera;

    // What the camera sees right now (used by Take Photo)
    public int RocksInView { get; private set; }
    public int SamplesInView { get; private set; }

    bool roverView;
    public Camera ActiveCamera => roverView ? onboardCamera : followCamera;   // the camera shown in the Mission Viewer

    public bool IsRock(Collider c)
    {
        return (rockLayer.value & (1 << c.gameObject.layer)) != 0;
    }

    public bool IsLander(Collider c)
    {
        return (landerLayer.value & (1 << c.gameObject.layer)) != 0;
    }

    public bool IsSample(Collider c)
    {
        return (sampleLayer.value & (1 << c.gameObject.layer)) != 0;
    }

    void Awake()
    {
        HasHDCamera = false;            // the loadout says which camera is fitted (in Start)

        // Assign separate Render Textures.
        if (onboardCamera != null)
            onboardCamera.targetTexture = roverCameraTexture;

        if (followCamera != null)
            followCamera.targetTexture = followCameraTexture;

        SetLens(range, viewAngle);

        // Start with Follow Camera.
        SetView(false);

        Clear();
    }

    void OnDestroy() => HasHDCamera = false;

    void LateUpdate()
    {
        Scan();
        DrawBoxes();
    }

    void Clear()
    {
        RockDistance = Mathf.Infinity;
        SampleDistance = Mathf.Infinity;
        SampleAngle = 0f;
        RockAngle = 0f;
        LanderDistance = Mathf.Infinity;
        LanderAngle = 0f;
        RockSize = 0f;
        SampleSize = 0f;
        RocksInView = 0;
        SamplesInView = 0;

        seen.Clear();
        seenDistance.Clear();
    }

    // --------------------------------------------------
    // CAMERA LENS
    // --------------------------------------------------

    public void SetLens(float newRange, float newViewAngle)
    {
        range = newRange;
        viewAngle = newViewAngle;

        if (onboardCamera == null)
            return;

        float aspect = 1.4f;

        if (roverCameraTexture != null)
        {
            aspect = (float)roverCameraTexture.width /
                     roverCameraTexture.height;
        }

        onboardCamera.aspect = aspect;

        onboardCamera.fieldOfView =
            Camera.HorizontalToVerticalFieldOfView(
                viewAngle,
                aspect
            );
    }

    // --------------------------------------------------
    // CAMERA SWITCHING
    // --------------------------------------------------

    public void ToggleView()
    {
        SetView(!roverView);
    }

    void SetView(bool rover)
    {
        roverView = rover;

        if (onboardCamera != null)
            onboardCamera.enabled = rover;

        if (followCamera != null)
            followCamera.enabled = !rover;

        // Switch Global Volumes
        if (roverGlobalVolume != null)
            roverGlobalVolume.SetActive(rover);

        if (followGlobalVolume != null)
            followGlobalVolume.SetActive(!rover);

        if (missionViewerImage != null)
            missionViewerImage.texture = rover ? roverCameraTexture : followCameraTexture;

        if (viewButtonLabel != null)
            viewButtonLabel.text = rover ? "ROVER CAM" : "FOLLOW CAM";

        foreach (RectTransform box in boxes)
            if (box != null)
                box.gameObject.SetActive(false);
    }

    public void SetCameraType(string cameraName)
    {
        HasHDCamera = cameraName != null && string.Equals(cameraName.Trim(), HDCameraName,
            System.StringComparison.OrdinalIgnoreCase);

        if (string.Equals(cameraName, "No Camera",
            System.StringComparison.OrdinalIgnoreCase))
        {
            SetCameraAvailable(false);
            Debug.Log("No camera equipped.");
            return;
        }

        SetCameraAvailable(true);

        if (roverGlobalVolume == null)
            return;

        Volume volume = roverGlobalVolume.GetComponent<Volume>();

        if (volume == null)
            return;

        foreach (CameraVolumeProfile item in cameraProfiles)
        {
            if (item.cameraName == cameraName)
            {
                volume.profile = item.profile;
                Debug.Log($"Camera changed: {cameraName}");
                return;
            }
        }

        Debug.LogWarning($"No Volume Profile found for: {cameraName}");
    }

    public void SetCameraAvailable(bool available)
    {
        hasCamera = available;

        if (!available)
        {
            // Force Follow Camera
            SetView(false);

            // Disable Rover Camera
            if (onboardCamera != null)
                onboardCamera.enabled = false;

            // Disable Rover Volume
            if (roverGlobalVolume != null)
                roverGlobalVolume.SetActive(false);

            // Keep Follow Camera active
            if (followCamera != null)
                followCamera.enabled = true;

            if (followGlobalVolume != null)
                followGlobalVolume.SetActive(true);

            // Disable camera switch button
            if (viewToggleButton != null)
                viewToggleButton.interactable = false;

            if (missionViewerImage != null)
                missionViewerImage.texture = followCameraTexture;

            if (viewButtonLabel != null)
                viewButtonLabel.text = "NO CAMERA";
        }
        else
        {
            if (viewToggleButton != null)
                viewToggleButton.interactable = true;

            SetView(false);
        }
    }

    // --------------------------------------------------
    // OBJECT DETECTION
    // --------------------------------------------------

    public void Scan()
    {
        Clear();

        if (onboardCamera == null || nose == null)
            return;

        Plane[] picture =
            GeometryUtility.CalculateFrustumPlanes(onboardCamera);

        Vector3 eye = onboardCamera.transform.position;

        int detectionMask = rockLayer.value | sampleLayer.value | landerLayer.value;

        foreach (Collider c in Physics.OverlapSphere(eye, range, detectionMask))
        {
            if (c == null)
                continue;

            if (c.transform.IsChildOf(transform) ||
                c.name == "Ground")
                continue;

            if (!GeometryUtility.TestPlanesAABB(picture, c.bounds))
                continue;

            if (!VisiblePoint(c, eye, out Vector3 point))
                continue;

            float distance = NearestDistance(c);       // to the rock's nearest edge, not the point the camera sees

            seen.Add(c);
            seenDistance.Add(distance);

            if (IsSample(c)) SamplesInView++;
            else if (!IsLander(c)) RocksInView++;
            Consider(c, distance);
        }

        // Close up, a low rock leaves the bottom of the picture before the rover reaches it.
        // Keep measuring what is right ahead (like a real rover's hazard camera), so "rock dist" doesn't jump to "none".
        foreach (Collider c in Physics.OverlapSphere(nose.position, nearRange, detectionMask))
        {
            if (c == null || seen.Contains(c) || c.transform.IsChildOf(transform) || c.name == "Ground")
                continue;

            Vector3 to = c.bounds.center - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.0001f && Vector3.Angle(transform.forward, to) > viewAngle / 2f)
                continue;                               // not ahead of the rover

            Consider(c, NearestDistance(c));
        }
    }

    // Keeps the nearest rock / sample / lander and its readings
    void Consider(Collider c, float distance)
    {
        if (IsSample(c))
        {
            if (distance < SampleDistance)
            {
                SampleDistance = distance;
                SampleSize = HasHDCamera ? Measure(c) : 0f;    // only the HD Cam sees how big it is
                SampleAngle = AngleTo(c);
            }
        }
        else if (IsLander(c))
        {
            if (distance < LanderDistance)
            {
                LanderDistance = distance;
                LanderAngle = AngleTo(c);
            }
        }
        else if (distance < RockDistance)
        {
            RockDistance = distance;
            RockAngle = AngleTo(c);
            RockSize = HasHDCamera ? Measure(c) : 0f;          // only the HD Cam sees how big it is
        }
    }

    // Ground distance from the nose to the nearest point of the object
    // (its real shape when Unity can measure it, else its box)
    float NearestDistance(Collider c)
    {
        Vector3 from = nose.position;
        bool exact = !(c is TerrainCollider) && !(c is MeshCollider mesh && !mesh.convex);
        return GroundDistance(exact ? c.ClosestPoint(from) : c.bounds.ClosestPoint(from));
    }

    // The first thing a ray hits, skipping the rover's own parts (the solar panel mustn't hide a rock)
    readonly RaycastHit[] rayHits = new RaycastHit[16];

    bool FirstHit(Vector3 from, Vector3 direction, float length, out RaycastHit first)
    {
        first = default;
        int count = Physics.RaycastNonAlloc(from, direction, rayHits, length, ~0, QueryTriggerInteraction.Ignore);   // trigger boxes (storms) don't block the view
        float best = Mathf.Infinity;
        for (int i = 0; i < count; i++)
        {
            if (rayHits[i].collider.transform.IsChildOf(transform)) continue;
            if (rayHits[i].distance < best) { best = rayHits[i].distance; first = rayHits[i]; }
        }
        return best < Mathf.Infinity;
    }

    // --------------------------------------------------
    // LINE OF SIGHT
    // --------------------------------------------------

    bool VisiblePoint(
        Collider c,
        Vector3 eye,
        out Vector3 best)
    {
        Bounds b = c.bounds;

        Vector3[] targets =
        {
            b.ClosestPoint(eye),
            b.center,

            new Vector3(
                b.min.x,
                b.center.y,
                b.center.z
            ),

            new Vector3(
                b.max.x,
                b.center.y,
                b.center.z
            ),

            new Vector3(
                b.center.x,
                b.max.y,
                b.center.z
            )
        };

        best = Vector3.zero;

        float bestDistance = Mathf.Infinity;

        foreach (Vector3 target in targets)
        {
            Vector3 direction = target - eye;

            if (FirstHit(
                eye,
                direction,
                direction.magnitude + 0.1f,
                out RaycastHit hit
            ))
            {
                if (hit.collider == c && hit.distance <= range)
                {
                    float distance = GroundDistance(hit.point);

                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = hit.point;
                    }
                }
            }
        }

        return bestDistance < Mathf.Infinity;
    }

    // --------------------------------------------------
    // PHOTO
    // --------------------------------------------------

    // The nearest PhotoTarget the camera sees right now (in view, in range, not hidden), or null
    public PhotoTarget TargetInView()
    {
        PhotoTarget best = null;
        float bestDistance = Mathf.Infinity;

        for (int i = 0; i < seen.Count; i++)
        {
            if (seen[i] == null || !seen[i].gameObject.activeInHierarchy)
                continue;

            PhotoTarget target = seen[i].GetComponentInParent<PhotoTarget>();

            if (target != null && target.isActiveAndEnabled && seenDistance[i] < bestDistance)
            {
                best = target;
                bestDistance = seenDistance[i];
            }
        }

        return best;
    }

    // Renders the onboard camera once and copies the picture into a new texture (works in either view).
    // The caller owns the texture (Destroy it when done). Null if the camera or its Render Texture is missing.
    public Texture2D TakePhoto()
    {
        if (!hasCamera || onboardCamera == null || roverCameraTexture == null)
            return null;

        onboardCamera.Render();

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = roverCameraTexture;

        Texture2D photo = new Texture2D(roverCameraTexture.width, roverCameraTexture.height, TextureFormat.RGB24, false);
        photo.ReadPixels(new Rect(0, 0, roverCameraTexture.width, roverCameraTexture.height), 0, 0);
        photo.Apply();

        RenderTexture.active = previous;
        return photo;
    }

    // Which way an object is from the rover's heading, seen from above: + right, - left (degrees, to 0.1)
    float AngleTo(Collider c)
    {
        Vector3 to = c.bounds.center - transform.position;
        to.y = 0f;
        return Mathf.Round(Vector3.SignedAngle(transform.forward, to, Vector3.up) * 10f) / 10f;
    }

    // The biggest side of a rock or sample (m), to 0.1 m
    static float Measure(Collider c)
    {
        Vector3 s = c.bounds.size;
        return Mathf.Round(Mathf.Max(s.x, s.y, s.z) * 10f) / 10f;
    }

    // --------------------------------------------------
    // DISTANCE
    // --------------------------------------------------

    public float GroundDistance(Vector3 point)
    {
        Vector3 offset = point - nose.position;

        offset.y = 0f;

        return offset.magnitude;
    }

    // --------------------------------------------------
    // DETECTION BOXES
    // --------------------------------------------------

    // Lander: label in the middle of the box. Rock / sample: the template's own place
    void PlaceLabel(TMP_Text label, bool center)
    {
        RectTransform r = label.rectTransform;
        if (!labelSaved)
        {
            labelAnchorMin = r.anchorMin; labelAnchorMax = r.anchorMax;
            labelPivot = r.pivot; labelPos = r.anchoredPosition; labelSize = r.sizeDelta;
            labelAlignment = label.alignment;
            labelSaved = true;
        }

        if (center)
        {
            Vector2 size = r.rect.size;                          // keep its size when the anchors change
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = size;
            r.anchoredPosition = Vector2.zero;
            label.alignment = TextAlignmentOptions.Center;
        }
        else
        {
            r.anchorMin = labelAnchorMin; r.anchorMax = labelAnchorMax;
            r.pivot = labelPivot; r.sizeDelta = labelSize; r.anchoredPosition = labelPos;
            label.alignment = labelAlignment;
        }
    }

    void DrawBoxes()
    {
        Camera view = roverView
            ? onboardCamera
            : followCamera;

        if (view == null || overlay == null ||
            boxTemplate == null)
            return;

        Vector2 size = overlay.rect.size;

        int used = 0;

        for (int i = 0; i < seen.Count; i++)
        {
            if (seen[i] == null ||
                !seen[i].gameObject.activeInHierarchy)
                continue;

            if (!ViewportRect(
                view,
                seen[i].bounds,
                out Rect r
            ))
                continue;

            if (used == boxes.Count)
            {
                RectTransform made = Instantiate(boxTemplate, overlay);
                boxes.Add(made);
                nameLabels.Add(FindLabel(made, "NameTXT", true));
                sizeLabels.Add(FindLabel(made, "SizeTXT", false));
            }

            RectTransform box = boxes[used++];

            box.gameObject.SetActive(true);

            box.anchoredPosition = new Vector2(
                r.xMin * size.x,
                r.yMin * size.y
            );

            box.sizeDelta = new Vector2(
                r.width * size.x,
                r.height * size.y
            );

            bool sample = IsSample(seen[i]);
            bool lander = !sample && IsLander(seen[i]);

            Color color = sample ? SampleColor
                        : lander ? LanderColor
                        : seenDistance[i] <= RockDistance ? NearestColor
                        : RockColor;

            for (int c = 0; c < box.childCount; c++)
            {
                if (box.GetChild(c).TryGetComponent(
                    out Image corner
                ))
                {
                    corner.color = color;
                }
            }

            TMP_Text nameLabel = nameLabels[used - 1];
            TMP_Text sizeLabel = sizeLabels[used - 1];

            if (nameLabel != null)
            {
                nameLabel.text = (sample ? "SAMPLE " : lander ? "LANDER " : "ROCK ") + seenDistance[i].ToString("0.0") + " m";
                nameLabel.color = color;
                PlaceLabel(nameLabel, lander);                      // lander: in the middle of the box
            }

            if (sizeLabel != null)
            {
                bool showSize = HasHDCamera && !lander;             // only the HD Cam measures rocks and samples
                sizeLabel.gameObject.SetActive(showSize);
                if (showSize)
                {
                    sizeLabel.text = "SIZE " + Measure(seen[i]).ToString("0.0") + " m";
                    sizeLabel.color = color;
                }
            }
        }

        // Hide unused boxes.
        for (int i = used; i < boxes.Count; i++)
        {
            boxes[i].gameObject.SetActive(false);
        }
    }

    // A text in the box by name (also inactive ones). fallbackToFirst: an old template with one text still works
    static TMP_Text FindLabel(RectTransform box, string name, bool fallbackToFirst)
    {
        TMP_Text[] texts = box.GetComponentsInChildren<TMP_Text>(true);
        foreach (TMP_Text text in texts)
            if (text.name == name) return text;
        if (fallbackToFirst && texts.Length > 0) return texts[0];
        if (!fallbackToFirst) return null;
        Debug.LogWarning("[RoverCamera] Detection box has no '" + name + "' text");
        return null;
    }

    // --------------------------------------------------
    // WORLD BOUNDS TO VIEWPORT RECT
    // --------------------------------------------------

    static bool ViewportRect(
        Camera cam,
        Bounds b,
        out Rect rect)
    {
        float xMin = float.MaxValue;
        float yMin = float.MaxValue;

        float xMax = float.MinValue;
        float yMax = float.MinValue;

        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = new Vector3(
                i % 2 == 0 ? b.min.x : b.max.x,
                i / 2 % 2 == 0 ? b.min.y : b.max.y,
                i < 4 ? b.min.z : b.max.z
            );

            Vector3 v = cam.WorldToViewportPoint(corner);

            if (v.z <= 0f)
                continue;

            xMin = Mathf.Min(xMin, v.x);
            xMax = Mathf.Max(xMax, v.x);

            yMin = Mathf.Min(yMin, v.y);
            yMax = Mathf.Max(yMax, v.y);
        }

        rect = Rect.MinMaxRect(
            Mathf.Clamp01(xMin),
            Mathf.Clamp01(yMin),
            Mathf.Clamp01(xMax),
            Mathf.Clamp01(yMax)
        );

        return xMax > xMin &&
               rect.width > 0.005f &&
               rect.height > 0.005f;
    }
}
