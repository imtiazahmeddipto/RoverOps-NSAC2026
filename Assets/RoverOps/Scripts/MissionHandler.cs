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

    [Header("Results Panel")]
    public GameObject resultsPanel;
    public Transform starGoals;

    [Header("Star Icons")]
    public Sprite starEmpty;
    public Sprite starEarned;

    [Header("Navigation")]
    public string missionsScene = "Mission";
    public Transition transition;                       // optional: plays the transition before loading the garage

    private MissionData mission;
    private string scene;
    private bool completed;

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
        missionNameText.text = mission.title;

        if (mission == null)
        {
            Debug.LogWarning(
                "[Mission] Scene '" + scene +
                "' is not in the Mission Database.", this);
        }

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
        if (completed || !Rover.IsRunning)
            return;

        if (rover.nose == null || finishLine == null)
            return;

        if (InFinish(rover.nose.position))
            Complete();
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