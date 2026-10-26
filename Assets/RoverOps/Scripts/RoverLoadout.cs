using System;
using System.Collections.Generic;
using UnityEngine;

// Puts the equipped parts on a rover. Use it on BOTH rovers:
//   Garage rover   - only the models (leave "Rover" empty)
//   Playable rover - the models AND the stats (assign "Rover")
// Every part prefab is placed under "Model" and keeps its own position, so all parts must be
// exported from the same .blend (like Body.fbx and the part files in Models/Parts).
public class RoverLoadout : MonoBehaviour
{
    public PartDatabase database;
    public Transform model;             // holds the Body model; the parts go here too
    public Transform drillMount;        // optional: Science (drill) parts are placed under this (Drill 2), so they move with the arm
    public Rover rover;                 // optional: the playable rover gets the part stats

    readonly Dictionary<PartType, GameObject> mounted = new Dictionary<PartType, GameObject>();

    void Start()
    {
        foreach (PartType type in Enum.GetValues(typeof(PartType)))
        {
            PartData part = database.Equipped(type);
            if (part != null) Mount(part);
        }
        ApplyStats();
    }

    // Garage: put this part on the rover and remember the choice
    public void Equip(PartData part)
    {
        database.Equip(part);
        Mount(part);
        ApplyStats();
    }

    // Remove the old model of this type and add the new one
    public void Mount(PartData part)
    {
        if (mounted.TryGetValue(part.type, out GameObject old) && old != null)
        {
            old.SetActive(false);                   // gone right away (Destroy finishes at the end of the frame)
            Destroy(old);
        }
        mounted.Remove(part.type);
        if (part.prefab == null) return;

        GameObject go = Instantiate(part.prefab, model, false);
        go.name = part.type + ": " + part.name;
        SetLayer(go.transform, model.gameObject.layer);
        if (part.type == PartType.Science && drillMount != null) go.transform.SetParent(drillMount, true);   // keeps its place, then follows the arm
        mounted[part.type] = go;
    }

    // ---------------- STATS ON THE PLAYABLE ROVER ----------------
    void ApplyStats()
    {
        if (rover == null) return;

        rover.mass = database.TotalMass();

        PartData wheels = database.Equipped(PartType.Wheels);
        if (wheels != null)
        {
            rover.topSpeed = wheels.Stat(0);
            rover.acceleration = 1f * wheels.Stat(1);    // grip: how hard the wheels can push...
            rover.braking = 2f * wheels.Stat(1);         // ...and stop
            rover.energyPerMeter = wheels.Stat(2);
        }
        PartData cam = database.Equipped(PartType.Camera);
        if (cam != null)
        {
            rover.cam.SetLens(cam.Stat(0), rover.cam.viewAngle);
            rover.cameraPower = cam.Stat(1);
        }
        PartData battery = database.Equipped(PartType.Battery);
        if (battery != null) rover.batteryCapacity = battery.Stat(0);
        PartData solar = database.Equipped(PartType.Solar);
        if (solar != null) rover.chargeRate = solar.Stat(0);
        PartData drill = database.Equipped(PartType.Science);
        if (drill != null)
        {
            rover.drillSpeed = drill.Stat(0);
            rover.sampleCapacity = Mathf.RoundToInt(drill.Stat(1));
        }

        FitToModels();
        rover.Refill();                         // new parts: full battery
    }

    // Wheels to spin, onboard camera on the camera part, crash box and nose around the whole rover
    void FitToModels()
    {
        if (mounted.TryGetValue(PartType.Wheels, out GameObject wheelSet) && wheelSet != null)
        {
            var wheels = new List<Transform>();
            foreach (Renderer r in wheelSet.GetComponentsInChildren<Renderer>()) wheels.Add(r.transform);
            rover.wheels = wheels.ToArray();
            if (wheels.Count > 0) rover.wheelRadius = Mathf.Max(0.05f, wheels[0].GetComponent<Renderer>().bounds.extents.y);
        }

        if (mounted.TryGetValue(PartType.Camera, out GameObject camPart) && camPart != null && rover.cam.onboardCamera != null)
        {
            Bounds b = WorldBounds(camPart);
            Vector3 forward = transform.forward;
            float frontHalf = Mathf.Abs(forward.x) * b.extents.x + Mathf.Abs(forward.y) * b.extents.y + Mathf.Abs(forward.z) * b.extents.z;
            rover.cam.onboardCamera.transform.position = b.center + forward * (frontHalf + 0.03f);   // on the lens
        }

        // the rover's size in its own space (for the crash box and the nose)
        Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
        foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
        {
            Bounds b = r.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3(i % 2 == 0 ? b.min.x : b.max.x, i / 2 % 2 == 0 ? b.min.y : b.max.y, i < 4 ? b.min.z : b.max.z);
                Vector3 local = transform.InverseTransformPoint(corner);
                min = Vector3.Min(min, local);
                max = Vector3.Max(max, local);
            }
        }
        if (min.x > max.x) return;                         // no models

        //BoxCollider box = rover.GetComponent<BoxCollider>();
        //if (box != null)
        //{
            //box.center = (min + max) / 2f + Vector3.up * 0.05f;   // a little above the ground
            //box.size = max - min - Vector3.up * 0.1f;
        //}
        if (rover.nose != null) rover.nose.localPosition = new Vector3(0f, 0.5f, max.z);
    }

    static Bounds WorldBounds(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        Bounds b = renderers.Length > 0 ? renderers[0].bounds : new Bounds(go.transform.position, Vector3.zero);
        foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
        return b;
    }

    static void SetLayer(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        foreach (Transform child in t) SetLayer(child, layer);
    }
}
