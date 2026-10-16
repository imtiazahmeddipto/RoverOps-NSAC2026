using System;
using System.Collections.Generic;
using UnityEngine;

public enum PartType { Wheels, Camera, Battery, Solar, Science }

// One stat of a part. The name is filled in from the part type and shows as the label in the Inspector.
[Serializable]
public class PartStat
{
    [HideInInspector] public string name;
    public float value;
}

// One part in the database
[Serializable]
public class PartData
{
    public string name = "New Part";
    public PartType type;
    public Sprite icon;
    [Tooltip("The 3D model. It is placed under the rover's Model and keeps its own position, like the parts exported from the .blend.")]
    public GameObject prefab;
    public float mass = 10f;                                // kg
    public PartStat[] stats = new PartStat[0];

    public float Stat(int index) => index < stats.Length ? stats[index].value : 0f;
}

// THE PART DATABASE (Create > RoverOps > Part Database).
// Add a part with "+", pick its type, and its stat slots appear with the right names.
// It also remembers which part of each type is on the rover (saved between scenes and play sessions).
[CreateAssetMenu(menuName = "RoverOps/Part Database", fileName = "Part Database")]
public class PartDatabase : ScriptableObject
{
    public float bodyMass = 120f;                           // kg, the rover without parts
    public List<PartData> parts = new List<PartData>();

    // The stats each type has (the order matters: RoverLoadout reads them by number)
    public static string[] StatNames(PartType type)
    {
        switch (type)
        {
            case PartType.Wheels: return new[] { "Speed", "Grip", "Power Use" };
            case PartType.Camera: return new[] { "Range", "Power Use" };
            case PartType.Battery: return new[] { "Capacity" };
            case PartType.Solar: return new[] { "Charge Rate" };
            default: return new[] { "Drill Speed", "Sample Capacity" };     // Science (drill)
        }
    }

    public static string[] StatUnits(PartType type)
    {
        switch (type)
        {
            case PartType.Wheels: return new[] { "m/s", "", "/m" };
            case PartType.Camera: return new[] { "m", "/s" };
            case PartType.Battery: return new[] { "" };
            case PartType.Solar: return new[] { "/s" };
            default: return new[] { "x", "" };
        }
    }

    // ---------------- WHAT'S ON THE ROVER ----------------
    static string Key(PartType type) => "RoverOps.Equipped." + type;

    public List<PartData> OfType(PartType type) => parts.FindAll(p => p.type == type);

    // The equipped part of a type (the first one of that type if none was chosen yet)
    public PartData Equipped(PartType type)
    {
        string saved = PlayerPrefs.GetString(Key(type), "");
        return parts.Find(p => p.type == type && p.name == saved) ?? parts.Find(p => p.type == type);
    }

    public bool IsEquipped(PartData part) => Equipped(part.type) == part;

    public void Equip(PartData part)
    {
        PlayerPrefs.SetString(Key(part.type), part.name);
        PlayerPrefs.Save();
    }

    // ---------------- NUMBERS FOR THE UI ----------------
    public float TotalMass()
    {
        float mass = bodyMass;
        foreach (PartType type in Enum.GetValues(typeof(PartType)))
        {
            PartData part = Equipped(type);
            if (part != null) mass += part.mass;
        }
        return mass;
    }

    public float MaxMass()                                  // heaviest possible rover (full mass bar)
    {
        float mass = bodyMass;
        foreach (PartType type in Enum.GetValues(typeof(PartType)))
        {
            float heaviest = 0f;
            foreach (PartData p in OfType(type)) heaviest = Mathf.Max(heaviest, p.mass);
            mass += heaviest;
        }
        return mass;
    }

    public float BestStat(PartType type, int index)         // best value of a stat among parts of a type (full stat bar)
    {
        float best = 0.0001f;
        foreach (PartData p in OfType(type)) best = Mathf.Max(best, p.Stat(index));
        return best;
    }

    // Keeps every part's stat slots matching its type (runs when you edit the asset)
    void OnValidate()
    {
        foreach (PartData part in parts)
        {
            string[] names = StatNames(part.type);
            if (part.stats == null || part.stats.Length != names.Length)
            {
                var stats = new PartStat[names.Length];
                for (int i = 0; i < stats.Length; i++)
                    stats[i] = part.stats != null && i < part.stats.Length && part.stats[i] != null ? part.stats[i] : new PartStat();
                part.stats = stats;
            }
            for (int i = 0; i < names.Length; i++) part.stats[i].name = names[i];
        }
    }
}
