using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// One per mission scene (the same script in every scene, different settings).
// When the rover's nose reaches the finish zone during a run, it ends the run,
// checks the star goals from the Mission Database and saves the result.
// The finish zone is drawn green in the Scene view. The finish line itself needs NO collider.
public class MissionHandler : MonoBehaviour
{
    public MissionDatabase database;
    public Rover rover;
    public Transform finishLine;                            // an empty object (flag / line models as children, no colliders)
    public Vector3 finishSize = new Vector3(4f, 3f, 1f);    // width, height, depth of the finish zone (m)

    [Header("Results (optional)")]
    public GameObject resultsPanel;
    public TMP_Text resultsText;
    public string menuScene = "RoverOps Garage";            // the scene with the Missions screen

    MissionData mission;
    string scene;

    void Start()
    {
        scene = SceneManager.GetActiveScene().name;
        mission = database.Find(scene, out _, out _);
        if (mission == null) Debug.LogWarning("[Mission] The scene '" + scene + "' is not in the Mission Database.");
        if (resultsPanel) resultsPanel.SetActive(false);
    }

    void Update()
    {
        if (Rover.IsRunning && InFinish(rover.nose.position)) Complete();
    }

    void Complete()
    {
        rover.EndMission("<color=#7CFC7C>MISSION COMPLETE!</color>");
        if (mission == null) return;

        MissionDatabase.SetDone(scene);
        string report = "";
        for (int i = 0; i < mission.starGoals.Length; i++)
        {
            StarGoal goal = mission.starGoals[i];
            bool passed = Passed(goal);
            if (passed) MissionDatabase.EarnStar(scene, i);         // stars are kept once earned
            report += (passed ? "<color=#FFD54A>[STAR]</color> " : "<color=#888888>[ -- ]</color> ") + goal.Text + "\n";
        }
        if (resultsText) resultsText.text = report;
        if (resultsPanel) resultsPanel.SetActive(true);
    }

    bool Passed(StarGoal goal)
    {
        switch (goal.type)
        {
            case GoalType.Finish: return true;
            case GoalType.MaxBlocks: return rover.BlockCount <= goal.value;
            case GoalType.MaxTime: return rover.RunTime <= goal.value;
            case GoalType.MinBattery: return rover.BatteryPercent >= goal.value;
            case GoalType.MinSamples: return rover.Samples >= goal.value;
        }
        return false;
    }

    bool InFinish(Vector3 point)
    {
        Vector3 local = Quaternion.Inverse(finishLine.rotation) * (point - finishLine.position);
        return Mathf.Abs(local.x) <= finishSize.x / 2f && Mathf.Abs(local.y) <= finishSize.y / 2f && Mathf.Abs(local.z) <= finishSize.z / 2f;
    }

    // Results panel buttons
    public void Retry()
    {
        if (resultsPanel) resultsPanel.SetActive(false);
        rover.Restart();
    }

    public void BackToMenu() => SceneManager.LoadScene(menuScene);

    void OnDrawGizmos()
    {
        if (!finishLine) return;
        Gizmos.color = new Color(0f, 1f, 0.3f, 0.35f);
        Gizmos.matrix = Matrix4x4.TRS(finishLine.position, finishLine.rotation, Vector3.one);
        Gizmos.DrawCube(Vector3.zero, finishSize);
    }
}
