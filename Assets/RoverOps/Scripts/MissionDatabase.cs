using System;
using System.Collections.Generic;
using UnityEngine;

// One star goal of a mission. Only the text for now.
// TODO: what the goal checks (time, battery, samples...) so the mission scene can award the star.
public enum GoalType { Finish, MaxBlocks, MaxTime, MinBattery, MinSamples }

// One star goal of a mission: what it checks and the number to beat
[Serializable]
public class StarGoal
{
    public GoalType type;
    public float value;
    [Tooltip("Leave empty to write it from the type and value")]
    public string text;

    public string Text
    {
        get
        {
            if (!string.IsNullOrEmpty(text)) return text;
            switch (type)
            {
                case GoalType.MaxBlocks: return "Use " + value + " blocks or fewer";
                case GoalType.MaxTime: return "Finish in " + value + "s or less";
                case GoalType.MinBattery: return "Finish with " + value + "% battery or more";
                case GoalType.MinSamples: return "Collect " + value + " samples";
                default: return "Reach the finish line";
            }
        }
    }
}

// One mission = one scene
[Serializable]
public class MissionData
{
    [Tooltip("The mission's scene name. It must be in File > Build Settings, and the map button's OnClick uses the same name.")]
    public string scene;
    public string title = "New Mission";
    [TextArea(2, 4)] public string description;
    public Sprite image;
    public StarGoal[] starGoals = { new StarGoal(), new StarGoal(), new StarGoal() };
}

// One page of the Missions map
[Serializable]
public class SectorData
{
    public string name = "Sector 1";
    public string label = "Tutorial";               // shows as [Tutorial], leave empty for none
    public List<MissionData> missions = new List<MissionData>();
}

// THE MISSION DATABASE (Create > RoverOps > Mission Database).
// Sectors in page order, missions in button order.
// Progress (done, stars, picked mission) is saved in PlayerPrefs, not in this asset.
[CreateAssetMenu(menuName = "RoverOps/Mission Database", fileName = "Mission Database")]
public class MissionDatabase : ScriptableObject
{
    public List<SectorData> sectors = new List<SectorData>();

    // The mission with this scene name, its sector (page) and its place in the sector. Null if not found.
    public MissionData Find(string scene, out int sector, out int index)
    {
        sector = index = -1;
        if (string.IsNullOrEmpty(scene)) return null;
        for (int s = 0; s < sectors.Count; s++)
        {
            int i = sectors[s].missions.FindIndex(m => m.scene == scene);
            if (i < 0) continue;
            sector = s;
            index = i;
            return sectors[s].missions[i];
        }
        return null;
    }

    // Missions open in order: the very first one is always open, every other one opens when the mission
    // before it is done (the one above it in this sector, or the last one of the sector before)
    public bool IsUnlocked(int sector, int index)
    {
        if (index > 0) return IsDone(sectors[sector].missions[index - 1].scene);
        for (int s = sector - 1; s >= 0; s--)                       // skips empty sectors
            if (sectors[s].missions.Count > 0) return IsDone(sectors[s].missions[sectors[s].missions.Count - 1].scene);
        return true;
    }

    // ---------------- PROGRESS (PlayerPrefs) ----------------
    const string SelectedKey = "RoverOps.SelectedMission";
    static string Key(string scene) => "RoverOps.Mission." + scene;

    // The mission picked on the Missions screen (the garage's PLAY loads it)
    public static string SelectedScene
    {
        get => PlayerPrefs.GetString(SelectedKey, "");
        set { PlayerPrefs.SetString(SelectedKey, value); PlayerPrefs.Save(); }
    }

    public static bool IsDone(string scene) => PlayerPrefs.GetInt(Key(scene) + ".Done", 0) == 1;

    // Call from the mission scene when the mission is completed (not wired yet)
    public static void SetDone(string scene)
    {
        PlayerPrefs.SetInt(Key(scene) + ".Done", 1);
        PlayerPrefs.Save();
    }

    // Stars: one per star goal, kept once earned (not awarded yet)
    public static bool HasStar(string scene, int goal) => PlayerPrefs.GetInt(Key(scene) + ".Star" + goal, 0) == 1;

    public static void EarnStar(string scene, int goal)
    {
        PlayerPrefs.SetInt(Key(scene) + ".Star" + goal, 1);
        PlayerPrefs.Save();
    }

    public static int Stars(MissionData mission)
    {
        int count = 0;
        for (int i = 0; i < mission.starGoals.Length; i++) if (HasStar(mission.scene, i)) count++;
        return count;
    }

    public static int DoneCount(SectorData sector) => sector.missions.FindAll(m => IsDone(m.scene)).Count;
}