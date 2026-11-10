using System.Collections;
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
    //   CollectSamples - only the samples below (no finish line, no data).
    //                    If the scene has SampleTarget samples, ALL of them must be collected instead (Samples To Collect is not used)
    //   AnalyzeRock - the rover analyzes Rocks To Analyze rocks (no sending needed).
    //                 If the scene has AnalysisTarget rocks, ONLY those count; with none, any rock counts.
    // Samples To Collect is an extra goal in ANY mode: 0 = not needed (and not shown in the status).
    public enum FinishBy { FinishLine, SendData, Both, CollectSamples, AnalyzeRock }   // new ones at the END (scenes save the number)

    [Header("How the mission ends")]
    public FinishBy finishBy = FinishBy.FinishLine;
    [Min(0)] public int analysesToSend = 1;            // analyses of rocks WITH an AnalysisTarget the lander must receive
    [Min(0)] public int photosToSend = 0;              // photos WITH a PhotoTarget in them the lander must receive
    [Min(0)] public int samplesToCollect = 0;          // samples the rover must have in its sample box (0 = not needed)
    [Min(0)] public int rocksToAnalyze = 1;            // AnalyzeRock only: rocks to analyze (target rocks only, if the scene has any)

    public bool NeedsFinish => finishBy == FinishBy.FinishLine || finishBy == FinishBy.Both;
    bool NeedsData => finishBy == FinishBy.SendData || finishBy == FinishBy.Both;
    bool UsesSampleTargets => finishBy == FinishBy.CollectSamples && SampleTarget.Count > 0;
    bool NeedsSamples => samplesToCollect > 0 || UsesSampleTargets;
    int SamplesDone => UsesSampleTargets ? rover.TargetSamplesCollected : rover.Samples;
    int SamplesNeeded => UsesSampleTargets ? SampleTarget.Count : samplesToCollect;
    public bool EndsBySampleTargets => UsesSampleTargets;   // the Rover asks: a wrong sample is an error right away
    bool NeedsAnalysis => finishBy == FinishBy.AnalyzeRock;
    public bool EndsByAnalysis => NeedsAnalysis;       // the Rover asks: a wrong rock is an error right away

    // The indicator asks what to point at next.
    // Send Data: gathered = already sent + waiting in the rover's memory. Targets are pointed at until enough are gathered,
    // then the Lander (also when the memory is full: it must send before it can gather more).
    int AnalysesGathered => rover.AnalysesSent + rover.GoodAnalysesWaiting;
    int PhotosGathered => rover.PhotosSent + rover.GoodPhotosWaiting;
    bool GatheredAll => AnalysesGathered >= analysesToSend && PhotosGathered >= photosToSend;

    public bool WantsAnalysisTarget => rover != null && !completed
        && ((NeedsAnalysis && RocksDone < rocksToAnalyze)
            || (NeedsData && AnalysesGathered < analysesToSend && !rover.MemoryFull));
    public bool WantsPhotoTarget => rover != null && !completed && NeedsData && PhotosGathered < photosToSend && !rover.MemoryFull;
    public bool WantsLander => rover != null && !completed && NeedsData && !DataDelivered() && rover.HasDataWaiting
        && (GatheredAll || rover.MemoryFull);
    public bool DataStillNeeded => rover != null && NeedsData && !DataDelivered();     // Both: the finish line waits for this
    public bool WantsSampleTarget => rover != null && !completed && UsesSampleTargets && rover.TargetSamplesCollected < SampleTarget.Count;
    bool HasGoal => NeedsFinish || NeedsData || NeedsSamples || NeedsAnalysis;

    // Rocks that count: only target rocks when the scene has any, else every analyzed rock
    bool UsesTargets => AnalysisTarget.Count > 0;
    int RocksDone => UsesTargets ? rover.TargetRocksAnalyzed : rover.RocksAnalyzed;
    string RockLabel => UsesTargets ? "target rock" : "rock";

    [Header("Results Panel")]
    public GameObject resultsPanel;
    public Transform starGoals;

    [Header("Space Points (SP) reward")]
    public PartDatabase partDatabase;                  // the garage shop's database: SP go to the same balance it spends
    [Min(0)] public int spPerStarGoal = 100;           // SP for each star goal, paid once per goal
    public TMP_Text totalSPText;                       // SP earned this time (e.g. Result Panel/Image/Total/SP Point Total)
    public string spTextName = "SP Point";             // the SP text inside each StarGoal row

    enum Reward { None, Earned, AlreadyEarned }

    [Header("Star Icons")]
    public Sprite starEmpty;
    public Sprite starEarned;

    [Header("Navigation")]
    public string missionsScene = "Mission";
    public Transition transition;                       // optional: plays the transition before loading the garage

    public static MissionHandler Current { get; private set; }   // the Rover asks it why a run failed

    private MissionData mission;
    public MissionData Mission => mission;              // this scene's mission from the Mission Database (null if not found)
    private string scene;
    private bool completed;
    public bool IsCompleted => completed;               // done: RUN waits for the results panel (Replay starts again)

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

        if (finishBy == FinishBy.CollectSamples && samplesToCollect == 0 && SampleTarget.Count == 0)
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

        if (UsesSampleTargets && rover.sampleCapacity < SampleTarget.Count)
            Debug.LogWarning("[Mission] The scene has " + SampleTarget.Count + " target samples but the sample box holds "
                             + rover.sampleCapacity + " (it may change with the drill fitted in the garage).", this);

        if (NeedsAnalysis && UsesTargets && AnalysisTarget.Count < rocksToAnalyze)
            Debug.LogWarning("[Mission] Rocks To Analyze is " + rocksToAnalyze + " but the scene has only " + AnalysisTarget.Count
                             + " target rocks (AnalysisTarget): the mission can't end.", this);

        if (NeedsAnalysis && rocksToAnalyze > rover.analysisCapacity)
            Debug.LogWarning("[Mission] Rocks To Analyze (" + rocksToAnalyze + ") is more than the rover's Analysis Capacity ("
                             + rover.analysisCapacity + "): the player must use Send Data between analyses.", this);

        if (resultsPanel != null)
            resultsPanel.SetActive(false);
    }

    void Update()
    {
        if (completed || !Rover.IsRunning || !HasGoal)      // no goal set: never ends by itself
            return;

        if ((!NeedsFinish || ReachedFinish())
            && (!NeedsData || DataDelivered())
            && (!NeedsSamples || SamplesDone >= SamplesNeeded)
            && (!NeedsAnalysis || RocksDone >= rocksToAnalyze))
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
            parts.Add(Count(UsesSampleTargets ? "Target Samples" : "Samples", SamplesDone, SamplesNeeded));

        if (NeedsAnalysis)
            parts.Add(Count(UsesTargets ? "Analyze Target Rock" : "Analyze Rock", RocksDone, rocksToAnalyze));

        return string.Join("    ", parts);
    }

    static string Count(string label, int have, int need)
    {
        string text = label + " " + Mathf.Min(have, need) + "/" + need;
        return have >= need ? Done(text) : text;
    }

    static string Done(string text) => "<color=#7CFC7C>" + text + "</color>";

    // "finish dist" in the Code Editor: metres to the finish zone (0 inside it), Infinity = no finish line in this mission
    public float FinishLineDistance => finishLine != null && rover != null ? FinishDistance() : Mathf.Infinity;

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

        if (NeedsSamples && SamplesDone < SamplesNeeded)
            reasons.Add("The rover collected only " + SamplesDone + "/" + SamplesNeeded + (UsesSampleTargets ? " target samples." : " samples."));

        if (NeedsAnalysis && RocksDone < rocksToAnalyze)
            reasons.Add("The rover analyzed only " + RocksDone + "/" + rocksToAnalyze + " " + RockLabel + (rocksToAnalyze == 1 ? "." : "s."));

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
            ShowResults();
            return;
        }

        MissionDatabase.SetDone(scene);

        var rewards = new Reward[mission.starGoals.Length];
        int newSP = 0;
        bool unpaid = false;

        for (int i = 0; i < mission.starGoals.Length; i++)
        {
            bool passed = Passed(mission.starGoals[i]);
            if (passed)
                MissionDatabase.EarnStar(scene, i);

            if (MissionDatabase.HasStarReward(scene, i))
            {
                rewards[i] = Reward.AlreadyEarned;          // paid on an earlier run: never again
            }
            else if (passed && partDatabase != null)
            {
                MissionDatabase.SetStarReward(scene, i);    // marked first: a goal can never be paid twice
                newSP += spPerStarGoal;
                rewards[i] = Reward.Earned;
            }
            else if (passed)
            {
                unpaid = true;                              // no Part Database linked: pay it next time instead
            }
        }

        if (newSP > 0)
            partDatabase.AddSpacePoints(newSP);

        if (unpaid)
            Debug.LogWarning("[Mission] Star goals were passed but no Part Database is assigned: no SP awarded.", this);

        UpdateStarGoals(rewards);
        ShowTotalSP(newSP);

        ShowResults();
    }

    // ---------------- RESULTS PANEL ----------------
    [Header("Results Delay")]
    float resultsDelay = 1.5f;           // seconds after the mission ends before the panel appears
    Coroutine resultsRoutine;

    // The player sees the last action finish (and hears the success sound) before the panel comes up
    void ShowResults()
    {
        if (resultsPanel == null) return;
        if (resultsRoutine != null) StopCoroutine(resultsRoutine);
        resultsRoutine = StartCoroutine(ShowResultsLater());
    }

    IEnumerator ShowResultsLater()
    {
        yield return new WaitForSecondsRealtime(resultsDelay);   // real seconds: the same at any run speed
        resultsRoutine = null;
        if (!completed) yield break;                              // not if the player pressed Replay meanwhile
        resultsPanel.SetActive(true);
        GameAudio.Play(GameAudio.Sfx.MissionComplete);            // the success sound comes with the panel
        GameAudio.DuckMusic(3.5f);                                // the music steps back so it stands out
    }

    // --------------------------------------------------
    // STAR GOALS
    // --------------------------------------------------

    void UpdateStarGoals(Reward[] rewards)
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

            TMP_Text goalText = FindText(row, "txt");
            if (goalText == null)
            {
                goalText = row.GetComponentInChildren<TMP_Text>(true);
                if (goalText != null && goalText == FindText(row, spTextName)) goalText = null;   // never the SP text
            }

            if (goalText != null)
                goalText.text = goal.Text;

            ShowReward(row, rewards != null && i < rewards.Length ? rewards[i] : Reward.None);

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

    // --------------------------------------------------
    // SPACE POINTS
    // --------------------------------------------------

    // The row's SP Point text: "+100" when paid this run, "+0" when paid on an earlier run, "0" when not earned
    void ShowReward(Transform row, Reward reward)
    {
        TMP_Text sp = FindText(row, spTextName);
        if (sp == null) return;

        sp.text = reward == Reward.Earned ? "+" + spPerStarGoal.ToString("N0")
                : reward == Reward.AlreadyEarned ? "+0"
                : "0";
    }

    // Total SP this run added to the balance
    void ShowTotalSP(int newSP)
    {
        TMP_Text total = totalSPText;
        if (total == null && resultsPanel != null)
            total = FindText(resultsPanel.transform, spTextName + " Total");

        if (total != null)
            total.text = newSP > 0 ? "+" + newSP.ToString("N0") : "0";
    }

    // A text under 'root' by name (spaces and capitals don't matter), also on inactive objects
    static TMP_Text FindText(Transform root, string name)
    {
        if (root == null || string.IsNullOrWhiteSpace(name)) return null;
        string want = name.Trim();
        foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            if (string.Equals(text.name.Trim(), want, System.StringComparison.OrdinalIgnoreCase))
                return text;
        return null;
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

            case GoalType.UseBlock:
                return rover.UsedBlock(goal.block);

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
        if (resultsRoutine != null) { StopCoroutine(resultsRoutine); resultsRoutine = null; }
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
                LoadWithTransition(nextMission.scene);
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
        if (Rover.IsRunning || ProgramBuilder.IsBuilding)        // can't leave while the rover is out on a run
        {
            if (rover != null) rover.Message("<color=#FFC63A>Stop the program first (press STOP), then go back to Missions.</color>");
            GameAudio.Play(GameAudio.Sfx.Error);
            return;
        }
        LoadWithTransition(missionsScene);
    }

    // Plays the end transition first (if there is one), then loads the scene
    void LoadWithTransition(string sceneName)
    {
        Time.timeScale = 1f;                    // the transition runs on game time: never at a run speed
        if (transition) transition.LoadScene(sceneName);
        else SceneManager.LoadScene(sceneName);
    }
}