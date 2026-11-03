using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MissionHandler : MonoBehaviour
{
    [Header("Mission")]
    public TMP_Text missionNameText;
    public MissionDatabase database;
    public Rover rover;
    public Transform finishLine;
    public Vector3 finishSize = new Vector3(4f, 3f, 1f);

    // How this mission ends:
    //   FinishLine  - the rover's nose reaches the finish zone (as before)
    //   SendData    - the lander has received the data below (Send Data block, within the Lander's range)
    //   Both        - the data is sent AND the rover is in the finish zone
    //   CollectSamples - only the samples below (no finish line, no data)
    // Samples To Collect is an extra goal in ANY mode: 0 = not needed (and not shown in the status).
    public enum FinishBy { FinishLine, SendData, Both, CollectSamples }   // new ones at the END (scenes save the number)

    [Header("How the mission ends")]
    public FinishBy finishBy = FinishBy.FinishLine;
    [Min(0)] public int analysesToSend = 1;            // analyses of rocks WITH an AnalysisTarget the lander must receive
    [Min(0)] public int photosToSend = 0;              // photos WITH a PhotoTarget in them the lander must receive
    [Min(0)] public int samplesToCollect = 0;          // samples the rover must have in its sample box (0 = not needed)

    bool NeedsFinish => finishBy == FinishBy.FinishLine || finishBy == FinishBy.Both;
    bool NeedsData => finishBy == FinishBy.SendData || finishBy == FinishBy.Both;
    bool NeedsSamples => samplesToCollect > 0;
    bool HasGoal => NeedsFinish || NeedsData || NeedsSamples;

    [Header("Results Panel")]
    public GameObject resultsPanel;
    public Transform starGoals;

    [Header("Star Icons")]
    public Sprite starEmpty;
    public Sprite starEarned;

    [Header("Navigation")]
    public string missionsScene = "Mission";
    public Transition transition;                       // optional: plays the transition before loading the garage

    public static MissionHandler Current { get; private set; }   // the Rover asks it why a run failed

    private MissionData mission;
    private string scene;
    private bool completed;

    void OnEnable() => Current = this;
    void OnDisable() { if (Current == this) Current = null; }

    void Start()
    {
        scene = SceneManager.GetActiveScene().name;

        if (database == null)
        {
            Debug.LogError("[Mission] MissionDatabase is not assigned.", this);
            enabled = false;
            return;
        }

        mission = database.Find(scene, out _, out _);

        if (mission == null)
        {
            Debug.LogWarning(
                "[Mission] Scene '" + scene +
                "' is not in the Mission Database.", this);
        }
        else if (missionNameText != null)
        {
            missionNameText.text = mission.title;
        }

        if (finishBy == FinishBy.CollectSamples && samplesToCollect == 0)
            Debug.LogWarning("[Mission] Finish By is CollectSamples but Samples To Collect is 0: the mission can't end. Set it to 1 or more.", this);

        if (NeedsData && Lander.Count == 0)
            Debug.LogWarning("[Mission] This mission ends by sending data, but the scene has no Lander.", this);

        if (NeedsFinish && finishLine == null)
            Debug.LogWarning("[Mission] This mission ends at the finish line, but Finish Line is not assigned.", this);

        if (NeedsData && analysesToSend == 0 && photosToSend == 0)
            Debug.LogWarning("[Mission] Ends by sending data, but Analyses To Send and Photos To Send are both 0: any sent data finishes it.", this);

        if (rover == null)
        {
            Debug.LogError("[Mission] Rover is not assigned.", this);
            enabled = false;
            return;
        }

        if (resultsPanel != null)
            resultsPanel.SetActive(false);
    }

    void Update()
    {
        if (completed || !Rover.IsRunning || !HasGoal)      // no goal set: never ends by itself
            return;

        if ((!NeedsFinish || ReachedFinish())
            && (!NeedsData || DataDelivered())
            && (!NeedsSamples || rover.Samples >= samplesToCollect))
            Complete();
    }

    // The mission's goal for the status panel, e.g. "Finish line 12.3 m" / "Send Analysis 0/1" / "Samples 1/3"
    public string Objective()
    {
        var parts = new List<string>();

        if (NeedsFinish && finishLine != null)
            parts.Add(ReachedFinish() ? Done("Finish line reached") : "Finish line " + FinishDistance().ToString("0.0") + " m");

        if (NeedsData)
        {
            if (analysesToSend > 0) parts.Add(Count("Send Analysis", rover.AnalysesSent, analysesToSend));
            if (photosToSend > 0) parts.Add(Count("Send Photo", rover.PhotosSent, photosToSend));
            if (analysesToSend == 0 && photosToSend == 0)
                parts.Add(Count("Send Data", rover.AnalysesSent + rover.PhotosSent > 0 ? 1 : 0, 1));
        }

        if (NeedsSamples)
            parts.Add(Count("Samples", rover.Samples, samplesToCollect));

        return string.Join("    ", parts);
    }

    static string Count(string label, int have, int need)
    {
        string text = label + " " + Mathf.Min(have, need) + "/" + need;
        return have >= need ? Done(text) : text;
    }

    static string Done(string text) => "<color=#7CFC7C>" + text + "</color>";

    // Ground distance from the rover's nose to the nearest edge of the finish zone (0 inside it)
    float FinishDistance()
    {
        Vector3 nose = rover.nose != null ? rover.nose.position : rover.transform.position;
        Vector3 local = Quaternion.Inverse(finishLine.rotation) * (nose - finishLine.position);
        Vector3 half = finishSize / 2f;
        Vector3 inside = new Vector3(Mathf.Clamp(local.x, -half.x, half.x),
                                     Mathf.Clamp(local.y, -half.y, half.y),
                                     Mathf.Clamp(local.z, -half.z, half.z));
        Vector3 offset = nose - (finishLine.position + finishLine.rotation * inside);
        offset.y = 0f;
        return offset.magnitude;
    }

    bool ReachedFinish()
    {
        return rover.nose != null && finishLine != null && InFinish(rover.nose.position);
    }

    // Why the mission isn't complete yet (null = nothing missing / already complete)
    public string Missing()
    {
        if (completed) return null;
        if (!HasGoal) return "This mission has no goal set in its MissionHandler.";
        var reasons = new List<string>();

        if (NeedsFinish && !ReachedFinish())
            reasons.Add("The rover didn't reach the finish line.");

        if (NeedsSamples && rover.Samples < samplesToCollect)
            reasons.Add("The rover collected only " + rover.Samples + "/" + samplesToCollect + " samples.");

        if (NeedsData && !DataDelivered())
        {
            var got = new List<string>();
            if (analysesToSend > 0) got.Add(rover.AnalysesSent + "/" + analysesToSend + " target rock analyses");
            if (photosToSend > 0) got.Add(rover.PhotosSent + "/" + photosToSend + " target photos");
            reasons.Add(got.Count > 0
                ? "The lander got only " + string.Join(" and ", got) + "."
                : "The lander got no data.");
        }

        return reasons.Count > 0 ? string.Join(" ", reasons) : null;
    }

    // The lander has everything this mission asks for (and at least something was sent)
    bool DataDelivered()
    {
        int sent = rover.AnalysesSent + rover.PhotosSent;
        return sent > 0
            && rover.AnalysesSent >= analysesToSend
            && rover.PhotosSent >= photosToSend;
    }

    // --------------------------------------------------
    // MISSION COMPLETION
    // --------------------------------------------------

    void Complete()
    {
        if (completed)
            return;

        completed = true;

        rover.EndMission("<color=#7CFC7C>MISSION COMPLETE!</color>");

        if (mission == null)
        {
            if (resultsPanel != null)
                resultsPanel.SetActive(true);

            return;
        }

        MissionDatabase.SetDone(scene);

        for (int i = 0; i < mission.starGoals.Length; i++)
        {
            if (Passed(mission.starGoals[i]))
                MissionDatabase.EarnStar(scene, i);
        }

        UpdateStarGoals();

        if (resultsPanel != null)
            resultsPanel.SetActive(true);
    }

    // --------------------------------------------------
    // STAR GOALS
    // --------------------------------------------------

    void UpdateStarGoals()
    {
        if (starGoals == null || mission == null)
        {
            Debug.LogWarning(
                "[Mission] Star Goals or Mission is missing.", this);
            return;
        }

        for (int i = 0; i < starGoals.childCount; i++)
        {
            Transform row = starGoals.GetChild(i);

            bool hasGoal = i < mission.starGoals.Length;
            row.gameObject.SetActive(hasGoal);

            if (!hasGoal)
                continue;

            StarGoal goal = mission.starGoals[i];
            bool passed = Passed(goal);

            TMP_Text goalText =
                row.GetComponentInChildren<TMP_Text>(true);

            if (goalText != null)
                goalText.text = goal.Text;

            Image star = row.GetComponentInChildren<Image>(true);

            if (star != null)
            {
                if (starEmpty != null && starEarned != null)
                {
                    star.sprite = passed ? starEarned : starEmpty;
                }
                else
                {
                    star.color = passed
                        ? new Color(1f, 0.82f, 0.25f)
                        : new Color(0.45f, 0.45f, 0.5f);
                }
            }
        }

        if (starGoals.childCount < mission.starGoals.Length)
        {
            Debug.LogWarning(
                "[Mission] Not enough star goal objects. Required: " +
                mission.starGoals.Length +
                ", Available: " + starGoals.childCount, this);
        }
    }

    bool Passed(StarGoal goal)
    {
        switch (goal.type)
        {
            case GoalType.Finish:
                return true;

            case GoalType.MaxBlocks:
                return rover.BlockCount <= goal.value;

            case GoalType.MaxTime:
                return rover.RunTime <= goal.value;

            case GoalType.MinBattery:
                return rover.BatteryPercent >= goal.value;

            case GoalType.MinSamples:
                return rover.Samples >= goal.value;

            case GoalType.MinAnalysesSent:
                return rover.AnalysesSent >= goal.value;

            case GoalType.MinPhotosSent:
                return rover.PhotosSent >= goal.value;

            default:
                return false;
        }
    }

    // --------------------------------------------------
    // FINISH ZONE
    // --------------------------------------------------

    bool InFinish(Vector3 point)
    {
        Vector3 local = Quaternion.Inverse(finishLine.rotation)
            * (point - finishLine.position);

        return Mathf.Abs(local.x) <= finishSize.x / 2f
            && Mathf.Abs(local.y) <= finishSize.y / 2f
            && Mathf.Abs(local.z) <= finishSize.z / 2f;
    }

    void OnDrawGizmos()
    {
        if (finishLine == null)
            return;

        Gizmos.color = new Color(0f, 1f, 0.3f, 0.35f);

        Gizmos.matrix = Matrix4x4.TRS(
            finishLine.position,
            finishLine.rotation,
            Vector3.one
        );

        Gizmos.DrawCube(Vector3.zero, finishSize);
    }

    // --------------------------------------------------
    // RESULTS BUTTONS
    // --------------------------------------------------

    // Replay the current mission.
    public void Replay()
    {
        if (resultsPanel != null)
            resultsPanel.SetActive(false);

        completed = false;

        if (rover != null)
            rover.Restart();
    }

    // Continue to the next mission in the same sector.
    public void Continue()
    {
        if (database == null || mission == null)
        {
            BackToMenu();
            return;
        }

        database.Find(scene, out int sectorIndex, out int missionIndex);

        if (sectorIndex < 0 || sectorIndex >= database.sectors.Count)
        {
            BackToMenu();
            return;
        }

        SectorData sector = database.sectors[sectorIndex];
        int nextIndex = missionIndex + 1;

        if (nextIndex < sector.missions.Count)
        {
            MissionData nextMission = sector.missions[nextIndex];

            // Check that the next mission is unlocked.
            if (database.IsUnlocked(sectorIndex, nextIndex))
            {
                MissionDatabase.SelectedScene = nextMission.scene;
                SceneManager.LoadScene(nextMission.scene);
            }
            else
            {
                // Next mission is locked.
                BackToMenu();
            }
        }
        else
        {
            // No more missions in this sector.
            BackToMenu();
        }
    }

    // Return to the mission selection screen.
    public void BackToMenu()
    {

        if (transition) transition.LoadScene(missionsScene);
        else SceneManager.LoadScene(missionsScene);
    }
}