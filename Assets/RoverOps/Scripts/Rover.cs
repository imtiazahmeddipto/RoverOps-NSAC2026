using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum Block
{
    Start,
    MoveForward, MoveBackward, TurnLeft, TurnRight,                  // movement
    CollectSample, Wait,                                             // actions
    If, Else, EndIf,                                                 // conditions
    RepeatTimes, RepeatWhile, RepeatForever, EndRepeat,              // loops
    Stop,
    AnalyzeRock, TakePhoto, SendData                                 // science (kept at the END: blocks already in scenes keep their type)
}

// The rover: wheels, crash sensor, drill, readings, variables and the code-block runner.
//
// READINGS (the camera is always on):  rock dist, sample dist, sample angle, samples, battery (%)
//           rock size, sample size (m, biggest side of the nearest rock / sample) - HD Cam only;
//           RUN refuses a program that uses them without one
//           rock detected, sample detected, lander detected (yes / no)
//           lander dist, lander angle (camera: the Lander's layer must be set on RoverCamera)
//           data (analyses + photos waiting to be sent)
//
// SCIENCE:  Analyze Rock (drill arm on a rock in reach), Take Photo (onboard camera),
//           Send Data (everything waiting goes to a Lander, only within its range)
// Every number slot also accepts a reading or variable name, e.g.  Turn Right [sample angle]
// "sample angle" / "rock angle" are signed (+ right, - left), so Turn Left [sample angle] and
// Turn Right [sample angle] both turn toward the sample (and [rock angle] toward the nearest rock).
//
// One sensor rule: an "If" placed right after a Move or a Turn WATCHES the camera during that move / turn.
// The moment its test is true, the move / turn is cancelled and the If part runs.
// If the move / turn finishes first, the test is checked once more: true runs the If part, false the Else part.
// Any other If checks once.
//
// Moves and turns are both ORDERS: the program sets stopAt / turnLeft, FixedUpdate carries them out,
// and cancelling is just clearing the order. No extra coroutines, so nothing can keep turning on its own.
public class Rover : MonoBehaviour
{
    public static readonly string[] Readings = { "rock detected", "rock dist", "rock angle", "rock size", "sample detected", "sample dist", "sample size", "sample angle",
                                                 "lander detected", "lander dist", "lander angle", "samples", "battery", "data" };

    [Header("Stats")]
    public float mass = 300f;           // kg (heavier = slower to speed up and to stop)
    public float topSpeed = 1.5f;       // m/s
    public float acceleration = 1f;     // m/s² for a 300 kg rover
    public float braking = 2f;          // m/s² for a 300 kg rover
    public float turnSpeed = 60f;       // degrees per second
    public float drillSpeed = 1f;       // 1 = 3 seconds per sample
    public float drillReach = 1f;       // a sample must be closer than this to the nose (m)
    public float areaSize = 58f;        // leaving the test area also ends the run (m)

    [Header("Power (set by the Battery and Solar parts)")]
    public float batteryCapacity = 100f;// energy the battery holds
    public float chargeRate = 0.5f;     // energy the solar panel adds per second
    public float energyPerMeter = 1f;   // driving cost for a 300 kg rover
    public float energyPerTurn = 0.5f;  // cost of a 90° turn for a 300 kg rover
    public float drillEnergy = 5f;      // cost of drilling one sample
    public float cameraPower = 0.1f;    // energy the camera uses per second while a program runs (set by the Camera part)
    public int sampleCapacity = 3;      // samples the sample box holds (set by the drill part)
    public float restartLevel = 0.2f;   // an empty battery must recharge to this part (20 %) before the rover moves again

    [Header("Science (Analyze Rock / Take Photo / Send Data)")]
    public float analyzeEnergy = 5f;    // cost of analyzing one rock (heavier rovers use more, like drilling)
    public float photoEnergy = 1f;      // cost of one photo
    public float photoTime = 0.5f;      // seconds to take a photo
    [Min(1)] public int photoCapacity = 1;  // photos the rover can hold until Send Data
    [Min(1)] public int analysisCapacity = 1;   // rock analyses the rover can hold until Send Data
    public float sendEnergy = 2f;       // cost of sending one analysis (a photo counts the same)
    public float sendTime = 1f;         // seconds to send one analysis (a photo takes twice as long)
    public RawImage photoPreview;       // optional: shows the last photo, then fades out
    public float previewTime = 3f;      // seconds the photo stays fully visible
    public float previewFadeTime = 0.5f;// seconds to fade out

    [Header("Links")]
    public RoverCamera cam;
    public Transform nose;              // front of the rover
    public Transform[] wheels;
    public float wheelRadius = 0.2f;
    public Transform programArea;       // the Code Editor's block list
    public TMP_Text statusText;         // the message: what the rover is doing, errors, mission result
    public TMP_Text statsText;          // optional: battery, mission goal and time (empty = shown under the message)
    public TMP_Text runButtonLabel;
    public Image runButtonImage;        // optional: the RUN / STOP button's background (green / red)
    public Color runColor = new Color(0.55f, 0.85f, 0.45f);
    public Color stopColor = new Color(1f, 0.45f, 0.45f);
    public TMP_Text speedLabel;         // optional: the speed button's text ("SPEED 1x"); the button's OnClick -> Rover.CycleSpeed

    public static bool IsRunning { get; private set; }

    class Loop { public int start, left; public bool isWhile; }   // left < 0 = forever

    Rigidbody rb;
    BoxCollider body;
    Vector3 startPosition;
    Quaternion startRotation;
    RoverSuspension suspension;         // optional: raycast suspension on the same object
    RoverDrill drillArm;                // optional: drill arm animation on the same object
    Vector3 flatPos;                    // where the rover is on the map (x, z); height and tilt come from the suspension
    float heading;                      // which way the rover faces (degrees)
    Quaternion Facing => Quaternion.Euler(0f, heading, 0f);
    Coroutine program;
    CodeBlock highlighted, errorBlock;
    string message = "";
    bool crashed, failed;
    int samples;
    float runStart, runTime;
    public float RunTime => runTime;
    int blockCount;
    public int BlockCount => blockCount;        // blocks in the last program (not counting "When mission starts")
    public int Samples => samples;
    public void EndMission(string text) => StopProgram(text);   // the MissionHandler stops the run at the finish line
    readonly List<GameObject> collected = new List<GameObject>();

    // science data: Analyze Rock / Take Photo store it, Send Data moves it to the lander
    readonly HashSet<Collider> analyzedRocks = new HashSet<Collider>();
    readonly List<Texture2D> photos = new List<Texture2D>();
    readonly List<PhotoTarget> waitingPhotos = new List<PhotoTarget>();   // one per photo waiting to send: its target (null = none)
    readonly List<AnalysisTarget> waitingAnalyses = new List<AnalysisTarget>();   // one per analysis waiting to send: its target (null = not a target rock)
    int analysesSent, photosSent, wrongPhotos, wrongAnalyses;
    public int AnalysesSent => analysesSent;     // the MissionHandler checks these
    public int PhotosSent => photosSent;         // only data of TARGETS counts
    int DataWaiting => waitingAnalyses.Count + waitingPhotos.Count;
    Coroutine previewRoutine;                    // the photo preview's show-then-fade (UI only, not part of the program)

    // motion order (FixedUpdate carries it out)
    float stopAt = -1f;                 // odometer reading to stop on (-1 = no move, the rover brakes)
    float moveDir = 1f;                 // +1 forward, -1 backward
    float speed, odometer;              // m/s, metres driven
    float energy;
    bool recharging;                    // the battery ran empty: wait for the sun

    public float BatteryPercent => batteryCapacity > 0f ? energy / batteryCapacity * 100f : 0f;

    bool Moving => stopAt >= 0f;

    // turn order (FixedUpdate carries it out, like a move)
    float turnLeft;                     // degrees still to turn (0 = no turn)
    float turnDir = 1f;                 // +1 right, -1 left
    bool Turning => turnLeft > 0f;
    bool InMotion => Moving || Turning;
    float MassFactor => mass / 300f;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        body = GetComponent<BoxCollider>();
        rb.mass = mass;
        energy = batteryCapacity;
        startPosition = transform.position;
        startRotation = transform.rotation;
        suspension = GetComponent<RoverSuspension>();
        drillArm = GetComponent<RoverDrill>();
        flatPos = startPosition;
        heading = startRotation.eulerAngles.y;
        IsRunning = false;
        HidePreview();
        editorView = programArea != null ? programArea.GetComponentInParent<CodeEditorView>() : null;
        SetRunLabel();
        Say("Build your program, then press RUN");
    }

    // Run speed: 1x / 2x / 3x, used only while a program runs (editing, results and menus stay at 1x).
    // Game time speeds up too, so time star goals are measured the same at any speed.
    int runSpeed = 1;

    public void CycleSpeed()            // the speed button's OnClick
    {
        runSpeed = runSpeed % 3 + 1;
        if (speedLabel) speedLabel.text = "SPEED " + runSpeed + "x";
    }

    void Update()
    {
        float scale = IsRunning ? runSpeed : 1f;
        if (Time.timeScale != scale) Time.timeScale = scale;
        ShowStatus();                   // live telemetry
    }

    void OnDisable() => Time.timeScale = 1f;   // leaving the scene: back to normal

    // While a program runs, the Code Editor keeps the running block fully inside its frame
    // (the highlight draws it on top, outside the frame's clipping, so it must never sit across the edge)
    CodeEditorView editorView;

    void LateUpdate()
    {
        if (IsRunning && highlighted != null && editorView != null)
            editorView.Reveal(highlighted.body);
    }

    // ---------------- WHEELS (every physics step) ----------------
    void FixedUpdate()
    {
        Drive();
        Place();
    }

    // Puts the rover where Drive and Turn moved it (the suspension adds height and tilt). now = jump there at once
    void Place(bool now = false)
    {
        if (suspension != null)
        {
            if (now) suspension.Snap(flatPos, heading);
            else suspension.Place(flatPos, heading);
            return;
        }
        if (now) { rb.position = flatPos; rb.rotation = Facing; transform.SetPositionAndRotation(flatPos, Facing); }
        else { rb.MovePosition(flatPos); rb.MoveRotation(Facing); }
    }

    void Drive()
    {
        if (crashed) return;
        float dt = Time.fixedDeltaTime;
        float accel = acceleration / MassFactor, brake = braking / MassFactor;

        // the solar panel always charges the battery; the camera uses power while a program runs
        energy = Mathf.Min(batteryCapacity, energy + chargeRate * dt);
        if (IsRunning && !recharging) UseEnergy(cameraPower * dt, false);
        if (recharging)
        {
            speed = 0f;
            if (energy < batteryCapacity * restartLevel) return;
            recharging = false;
            Say("Battery at " + BatteryPercent.ToString("0") + "%: carrying on");
        }

        if (Turning && speed <= 0f) { Rotate(dt); return; }              // turns happen on the spot, standing still

        bool go = false;
        if (Moving)
        {
            float left = stopAt - odometer;
            if (left <= 0.005f) { stopAt = -1f; speed = 0f; }              // arrived on the mark
            else go = left > speed * speed / (2f * brake);                  // brake in time to stop on the mark
        }
        speed = go ? Mathf.Min(topSpeed, speed + accel * dt) : Mathf.Max(0f, speed - brake * dt);
        if (Moving) speed = Mathf.Max(speed, 0.05f);                       // creep the last few centimetres
        if (speed <= 0f) return;

        float step = speed * dt;
        if (Moving) step = Mathf.Min(step, stopAt - odometer);
        Vector3 next = flatPos + Facing * Vector3.forward * (step * moveDir);
        if (Mathf.Abs(next.x) > areaSize / 2f || Mathf.Abs(next.z) > areaSize / 2f) { Crash("The rover drove out of the test area!"); return; }

        flatPos = next;
        SpinWheels(step * moveDir, 0f);
        odometer += step;
        UseEnergy(step * energyPerMeter);
        if (Touching()) Crash("The rover crashed!");
    }

    // One physics step of a turn order
    void Rotate(float dt)
    {
        float step = Mathf.Min(turnSpeed * dt, turnLeft);
        heading += step * turnDir;
        turnLeft -= step;
        if (turnLeft < 0.001f) turnLeft = 0f;                           // arrived on the mark
        SpinWheels(0f, step * turnDir);
        UseEnergy(step / 90f * energyPerTurn);
        if (Touching()) Crash("The rover crashed while turning!");
    }

    // The test area in the Scene view: a square of areaSize around the world centre (0, 0).
    // Driving past its edge ends the run (see Drive).
    void OnDrawGizmos()
    {
        float y = Application.isPlaying ? startPosition.y : transform.position.y;
        Vector3 center = new Vector3(0f, y, 0f);
        Vector3 size = new Vector3(areaSize, 0f, areaSize);

        Gizmos.color = new Color(1f, 0.35f, 0.2f, 0.9f);
        Gizmos.DrawWireCube(center, size);
        Gizmos.color = new Color(1f, 0.35f, 0.2f, 0.06f);
        Gizmos.DrawCube(center, size);
    }

    bool Touching()
    {
        Vector3 center = rb.position + rb.rotation * body.center;
        foreach (Collider c in Physics.OverlapBox(center, body.size * 0.49f, rb.rotation))
            if (!c.transform.IsChildOf(transform) && c.name != "Ground") return true;
        return false;
    }

    // Driving, turning and drilling use energy (heavier rovers use more); the camera doesn't care about mass
    void UseEnergy(float amount, bool heavierUsesMore = true)
    {
        energy -= heavierUsesMore ? amount * MassFactor : amount;
        if (energy > 0f) return;
        energy = 0f;
        recharging = true;
        speed = 0f;
        Say("<color=#FFC63A>Battery empty! Waiting for the solar panel to recharge it...</color>");
    }

    // Full battery (back at the start, or new parts fitted in the garage)
    public void Refill()
    {
        energy = batteryCapacity;
        recharging = false;
    }

    void Crash(string text)
    {
        crashed = true;
        speed = 0f;
        StopProgram("<color=#FF6B6B>ROVER CRASHED. " + text + "\nPress RUN to try again.</color>");
        Debug.LogWarning("[Rover] Crashed: " + text);
    }

    // ---------------- BUTTONS ----------------
    // The one button: RUN starts fresh (rover back at the start, full battery, samples and data reset).
    // While running it is STOP: the rover stops where it is. A run that ends by itself turns it back into RUN.
    public void Run()
    {
        if (ProgramBuilder.IsBuilding) return;          // wait for the AI to finish placing blocks
        if (IsRunning)
        {
            StopProgram("Program stopped. Press RUN to try again.");
            return;
        }
        var all = new List<CodeBlock>(programArea.GetComponentsInChildren<CodeBlock>());
        int start = all.FindIndex(b => b.type == Block.Start);
        if (start < 0) { Say("Add a 'When mission starts' block first"); return; }
        List<CodeBlock> script = all.GetRange(start + 1, all.Count - start - 1);

        MarkError(null);
        string problem = CheckStructure(script, out CodeBlock bad);           // every If / Repeat needs its End
        if (problem == null) problem = CheckParts(script, out bad);           // and the parts for its readings
        if (problem != null)
        {
            MarkError(bad);
            Say("<color=#FF6B6B>Can't run: " + problem + " (marked in red)</color>");
            return;
        }

        Restart();                                  // every run starts fresh: back at the start, full battery, samples back
        blockCount = script.Count;
        runStart = Time.time;
        runTime = 0f;
        failed = false;
        IsRunning = true;
        SetRunLabel();
        program = StartCoroutine(RunMain(script));
    }

    public void Restart()
    {
        StopProgram("Rover is back at the start");
        crashed = false;
        speed = odometer = 0f;
        flatPos = startPosition;
        heading = startRotation.eulerAngles.y;
        Place(true);
        foreach (GameObject sample in collected) sample.SetActive(true);
        collected.Clear();
        samples = 0;
        ClearData();
        Refill();
        if (drillArm) drillArm.ResetPose();
    }

    public void Clear()
    {
        if (IsRunning || ProgramBuilder.IsBuilding) return;
        MarkError(null);
        foreach (Transform child in programArea)
            if (child.GetComponent<CodeBlock>().type != Block.Start) Destroy(child.gameObject);
        Say("Program cleared");
    }

    void StopProgram(string text)
    {
        if (program != null) StopCoroutine(program);
        EndProgram(text);
    }

    void EndProgram(string text)
    {
        if (IsRunning) runTime = Time.time - runStart;   // freeze the exact time the run ended
        stopAt = -1f;                   // the rover brakes to a stop by itself
        turnLeft = 0f;                  // and stops turning at once
        IsRunning = false;
        Highlight(null);
        SetRunLabel();
        Say(text);
    }

    // ---------------- PROGRAM ----------------
    IEnumerator RunMain(List<CodeBlock> blocks)
    {
        Say("Program started");
        yield return RunBlocks(blocks);
        if (failed) yield break;
        yield return BrakeToStop();

        // A complete mission ends the run itself (MissionHandler): give it a frame to do so
        MissionHandler handler = MissionHandler.Current;
        if (handler != null && handler.Missing() == null)
        {
            yield return null;
            if (IsRunning) EndProgram("Program finished");
            yield break;
        }

        // Every block has run but the mission isn't done: end the run, the rover stays where it is
        string why = handler != null ? handler.Missing() : "The program ended before the mission was done.";
        Debug.LogWarning("[Rover] Mission not complete: " + why);
        EndProgram("<color=#FF6B6B>MISSION NOT COMPLETE. " + why + "\nPress RUN to try again.</color>");
    }

    IEnumerator RunBlocks(List<CodeBlock> blocks)
    {
        var loops = new Stack<Loop>();
        for (int i = 0; i < blocks.Count; i++)
        {
            CodeBlock block = blocks[i];
            Highlight(block);
            switch (block.type)
            {
                // ----- movement -----
                case Block.MoveForward:
                case Block.MoveBackward:
                    float meters = Num(block, 2f);
                    if (failed) break;
                    if (block.type == Block.MoveBackward) meters = -meters;
                    yield return StartMove(meters);
                    if (!WatchedByNextIf(blocks, i)) while (Moving) yield return null;      // wait for the move to finish
                    break;                                                                 // else: the If after it watches the move

                case Block.TurnLeft:
                case Block.TurnRight:
                    float degrees = Num(block, 90f);
                    if (failed) break;
                    if (IsAimReading(block.ValueText))                    // [sample / rock / lander angle]: like a real rover, the block decides
                    {                                                     // the way and the reading only gives HOW MUCH. Wrong block = turns away.
                        string aim = Key(block.ValueText);
                        string what = aim == "rockangle" ? "rock" : aim == "landerangle" ? "lander" : "sample";
                        float seenAt = what == "rock" ? cam.RockDistance : what == "lander" ? cam.LanderDistance : cam.SampleDistance;
                        if (float.IsInfinity(seenAt)) Say("No " + what + " in view: not turning");
                        degrees = Mathf.Abs(degrees);
                    }
                    if (block.type == Block.TurnLeft) degrees = -degrees;
                    yield return StartTurn(degrees);
                    if (!WatchedByNextIf(blocks, i)) while (Turning) yield return null;     // wait for the turn to finish
                    break;                                                                 // else: the If after it watches the turn

                // ----- sensors & actions -----

                case Block.CollectSample: yield return CollectSample(); break;
                case Block.AnalyzeRock: yield return AnalyzeRock(); break;
                case Block.TakePhoto: yield return TakePhoto(); break;
                case Block.SendData: yield return SendData(); break;

                case Block.Wait:
                    float seconds = Num(block, 1f);
                    if (failed) break;
                    Say("Waiting " + seconds + " s");
                    yield return new WaitForSeconds(Mathf.Max(0f, seconds));
                    break;

                // ----- conditions -----
                case Block.If:
                    bool yes = false;
                    bool watching = i > 0 && IsMotion(blocks[i - 1].type) && InMotion;
                    yield return CheckIf(block, watching, result => yes = result);
                    if (failed) break;
                    if (!yes) i = FindIfEnd(blocks, i, true);                     // jump to Else (or End If)
                    break;

                case Block.Else: i = FindIfEnd(blocks, i, false); break;          // IF part done: skip the ELSE part

                // ----- loops -----
                case Block.RepeatTimes:
                    int times = Mathf.RoundToInt(Num(block, 3f));
                    if (failed) break;
                    if (times > 0) loops.Push(new Loop { start = i, left = times });
                    else i = FindLoopEnd(blocks, i);
                    break;

                case Block.RepeatWhile:
                    bool keepGoing = Test(block, out string test);
                    if (failed) break;
                    Say("While " + test);
                    if (keepGoing) loops.Push(new Loop { start = i, left = -1, isWhile = true });
                    else i = FindLoopEnd(blocks, i);
                    break;

                case Block.RepeatForever: loops.Push(new Loop { start = i, left = -1 }); break;

                case Block.EndRepeat:
                    if (loops.Count == 0) break;
                    Loop loop = loops.Peek();
                    if (loop.isWhile) { loops.Pop(); i = loop.start - 1; }      // go back and test the While again
                    else if (loop.left < 0) i = loop.start;                     // forever
                    else if (--loop.left > 0) i = loop.start;                   // more times to go
                    else loops.Pop();                                           // done
                    break;

                case Block.Stop: yield return BrakeToStop(); i = blocks.Count; break;
            }

            if (failed)
            {
                EndProgram("<color=#FF6B6B>" + message + "\nFix it, then press RUN.</color>");
                yield break;
            }
            yield return null;          // every block takes at least one frame (keeps loops safe)
        }
        Highlight(null);
    }

    // ---------------- STRUCTURE CHECK ----------------
    static bool IsLoop(Block t) => t == Block.RepeatTimes || t == Block.RepeatWhile || t == Block.RepeatForever;
    static bool IsMotion(Block t) => t == Block.MoveForward || t == Block.MoveBackward || t == Block.TurnLeft || t == Block.TurnRight;
    static bool WatchedByNextIf(List<CodeBlock> blocks, int i) => i + 1 < blocks.Count && blocks[i + 1].type == Block.If;

    // Every If needs an End If, every Repeat / While needs an End Repeat, and they must not cross.
    // Returns null when the program is fine, otherwise the problem and the block that causes it.
    static string CheckStructure(List<CodeBlock> blocks, out CodeBlock bad)
    {
        var open = new Stack<CodeBlock>();
        bad = null;
        foreach (CodeBlock b in blocks)
        {
            switch (b.type)
            {
                case Block.If:
                case Block.RepeatTimes:
                case Block.RepeatWhile:
                case Block.RepeatForever:
                    open.Push(b);
                    break;

                case Block.Else:
                    if (open.Count == 0) { bad = b; return "this Else has no If above it"; }
                    if (IsLoop(open.Peek().type)) { bad = open.Peek(); return "this Repeat needs its End Repeat before the Else"; }
                    if (open.Peek().type == Block.Else) { bad = b; return "this If already has an Else"; }
                    open.Pop();
                    open.Push(b);                                   // the Else carries on the same If
                    break;

                case Block.EndIf:
                    if (open.Count == 0) { bad = b; return "this End If has no If to close"; }
                    if (IsLoop(open.Peek().type)) { bad = open.Peek(); return "this Repeat needs its End Repeat before the End If"; }
                    open.Pop();
                    break;

                case Block.EndRepeat:
                    if (open.Count == 0) { bad = b; return "this End Repeat has no Repeat to close"; }
                    if (!IsLoop(open.Peek().type)) { bad = open.Peek(); return "this If needs its End If before the End Repeat"; }
                    open.Pop();
                    break;
            }
        }
        if (open.Count == 0) return null;
        bad = open.Peek();
        return IsLoop(bad.type) ? "this Repeat has no End Repeat" : "this If has no End If";
    }

    // Readings that need a part the rover doesn't have: rock size and sample size need the HD Cam
    static string CheckParts(List<CodeBlock> blocks, out CodeBlock bad)
    {
        bad = null;
        if (RoverCamera.HasHDCamera) return null;
        foreach (CodeBlock b in blocks)
            if (RoverCamera.IsHDOnly(b.Selected) || RoverCamera.IsHDOnly(b.ValueText))
            {
                bad = b;
                string reading = RoverCamera.IsHDOnly(b.Selected) ? b.Selected.Trim() : b.ValueText;
                return "'" + reading + "' needs the " + RoverCamera.HDCameraName + ". Fit it in the garage";
            }
        return null;
    }

    void MarkError(CodeBlock block)          // tint a faulty block red
    {
        if (errorBlock) errorBlock.body.GetComponent<Image>().color = Color.white;
        errorBlock = block;
        if (block) block.body.GetComponent<Image>().color = new Color(1f, 0.55f, 0.55f);
    }

    // The If test. While watching a move or turn, it tests every frame until that move / turn ends.
    // True during the motion: cancel it, stop, answer true (If part).
    // Motion finished first: test once more with a fresh scan (true = If part, false = Else part).
    IEnumerator CheckIf(CodeBlock block, bool watchMotion, System.Action<bool> answer)
    {
        if (watchMotion)
        {
            Say((Turning ? "Turning" : "Moving") + " and watching: " + block.Selected + " " + block.Sign + " " + block.ValueText + " ?");
            while (InMotion)
            {
                bool hit = Test(block, out string seen, false);          // the camera scans every frame by itself
                if (failed) { answer(false); yield break; }
                if (hit)
                {
                    string what = Turning ? "Turn" : "Move";
                    Say("If " + seen + ". " + what + " cancelled.");
                    yield return BrakeToStop();                          // clears the move / turn order, waits until standing still
                    answer(true);
                    yield break;
                }
                yield return null;
            }
        }

        bool yes = Test(block, out string text);
        Say("If " + text);
        answer(yes);
    }

    // "rock dist < 2" -> true / false, with a readable explanation
    bool Test(CodeBlock block, out string text, bool freshScan = true)
    {
        if (freshScan)
            cam.Scan();

        Read(block.Selected, out float left);

        // Yes / no conditions: rock detected, sample detected, lander detected
        if (Key(block.Selected).EndsWith("detected"))
        {
            bool detected = left > 0f;

            text = block.Selected.Trim() + ": " + (detected ? "YES" : "NO");

            return detected;
        }

        // Normal numerical conditions
        float right = Num(block, 0f);

        bool yes = block.Sign == "<" ? left < right
                 : block.Sign == ">" ? left > right
                 : Mathf.Abs(left - right) < 0.05f;

        text = block.Selected + " (" + Show(block.Selected, left) + ") "
             + block.Sign + " " + right.ToString("0.##") + ": "
             + (yes ? "YES" : "NO");

        return yes;
    }

    // From an If (or Else) at 'from', find its matching Else / End If (nested Ifs are skipped)
    static int FindIfEnd(List<CodeBlock> blocks, int from, bool stopAtElse)
    {
        int depth = 0;
        for (int j = from + 1; j < blocks.Count; j++)
        {
            Block type = blocks[j].type;
            if (type == Block.If) depth++;
            else if (type == Block.EndIf) { if (depth == 0) return j; depth--; }
            else if (type == Block.Else && depth == 0 && stopAtElse) return j;
        }
        return blocks.Count;
    }

    // From a Repeat at 'from', find its matching End Repeat (nested loops are skipped)
    static int FindLoopEnd(List<CodeBlock> blocks, int from)
    {
        int depth = 0;
        for (int j = from + 1; j < blocks.Count; j++)
        {
            Block type = blocks[j].type;
            if (type == Block.RepeatTimes || type == Block.RepeatWhile || type == Block.RepeatForever) depth++;
            else if (type == Block.EndRepeat) { if (depth == 0) return j; depth--; }
        }
        return blocks.Count;
    }

    // ---------------- READINGS & VARIABLES ----------------
    static string Key(string name) => name.Replace(" ", "").ToLowerInvariant();

    // Readings that carry their own direction: a Turn block aims by them instead of using Left / Right
    static bool IsAimReading(string name) => Key(name) == "sampleangle" || Key(name) == "rockangle" || Key(name) == "landerangle";

    // A reading or variable by name. False if the name is unknown.
    bool Read(string name, out float value)
    {
        switch (Key(name))
        {
            case "rockdist": value = cam.RockDistance; return true;
            case "rockangle": value = cam.RockAngle; return true;
            case "rocksize": value = RoverCamera.HasHDCamera ? cam.RockSize : 0f; return true;
            case "samplesize": value = RoverCamera.HasHDCamera ? cam.SampleSize : 0f; return true;
            case "landerdist": value = cam.LanderDistance; return true;
            case "landerangle": value = cam.LanderAngle; return true;
            case "landerdetected": value = float.IsInfinity(cam.LanderDistance) ? 0f : 1f; return true;
            case "rockdetected": value = float.IsInfinity(cam.RockDistance) ? 0f : 1f; return true;
            case "data": value = DataWaiting; return true;
            case "sampledist": value = cam.SampleDistance; return true;
            case "sampleangle": value = cam.SampleAngle; return true;
            case "samples": value = samples; return true;
            case "battery": value = BatteryPercent; return true;
            case "sampledetected": value = float.IsInfinity(cam.SampleDistance) ? 0f : 1f;
                return true;
        }
        value = 0f;
        return false;
    }

    // The number slot: empty = default, a number, or a reading / variable name
    float Num(CodeBlock block, float defaultValue)
    {
        string text = block.ValueText;
        if (text == "") return defaultValue;
        if (float.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) return v;
        if (Read(text, out v)) return v;
        Fail("ERROR: '" + text + "' is not a number or a reading. Use a number, rock dist, rock angle, rock size (HD Cam), sample dist, sample size (HD Cam), sample angle, samples, battery, lander dist, lander angle or data.");
        return defaultValue;
    }

    void Fail(string text)
    {
        failed = true;
        message = text;
        Debug.LogWarning("[Rover] " + text);   // a player mistake, not a code error: LogError would trigger Error Pause
    }

    static string Show(string name, float value)
    {
        if (float.IsInfinity(value)) return "none";
        string key = Key(name);
        if (key.EndsWith("size")) return value > 0f ? value.ToString("0.0") + " m" : "none";
        if (key.EndsWith("dist")) return value.ToString("0.0") + " m";
        if (key.EndsWith("angle")) return value.ToString("0") + "°";
        if (key == "battery") return value.ToString("0") + "%";
        return value.ToString("0.##");
    }

    // ---------------- ORDERS ----------------
    IEnumerator StartMove(float meters)
    {
        float dir = Mathf.Sign(meters);
        if (speed > 0f && dir != moveDir) yield return BrakeToStop();      // stop before changing direction
        Say(meters >= 0f ? "Moving forward " + meters.ToString("0.##") + " m" : "Moving back " + (-meters).ToString("0.##") + " m");
        turnLeft = 0f;                                                      // never drive and turn at once
        moveDir = dir;
        if (Mathf.Abs(meters) > 0.005f) stopAt = odometer + Mathf.Abs(meters);
    }

    // Cancels any move or turn order and waits until the rover stands still
    IEnumerator BrakeToStop()
    {
        stopAt = -1f;
        turnLeft = 0f;
        while (speed > 0f) yield return null;
    }

    // negative = left, positive = right. FixedUpdate turns the rover; this only gives the order.
    IEnumerator StartTurn(float degrees)
    {
        yield return BrakeToStop();                                         // a rover turns on the spot, so it stops first
        turnDir = degrees < 0f ? -1f : 1f;
        turnLeft = Mathf.Abs(degrees) > 0.01f ? Mathf.Abs(degrees) : 0f;
        if (Turning) Say(degrees < 0f ? "Turning left " + (-degrees).ToString("0.#") + "°" : "Turning right " + degrees.ToString("0.#") + "°");
    }

    IEnumerator CollectSample()
    {
        yield return BrakeToStop();                      // drill only when standing still
        Collider sample = SampleInReach();
        if (sample == null) { yield return Problem("no sample within " + drillReach + " m of the drill. Get closer to a sample first."); yield break; }
        if (samples >= sampleCapacity)
        {
            yield return Problem("the sample box is full (" + sampleCapacity + " samples). Fit a bigger drill in the garage.");
            yield break;
        }
        while (recharging || energy < drillEnergy * MassFactor)        // the drill needs power
        {
            message = "<color=#FFC63A>Not enough power to drill: waiting for the solar panel...</color>";
            yield return null;
        }
        UseEnergy(drillEnergy);
        if (drillArm)
        {
            message = "Moving the drill arm...";
            yield return drillArm.Reach(sample.bounds.center);       // arm out to the sample
        }
        float time = 3f / drillSpeed;
        for (float t = 0f; t < time; t += Time.deltaTime)
        {
            message = "Drilling... " + Mathf.RoundToInt(t / time * 100f) + "%";
            yield return null;
        }
        sample.gameObject.SetActive(false);
        collected.Add(sample.gameObject);
        samples++;
        if (drillArm) yield return drillArm.Fold();                  // arm back
        Say("Sample collected! (" + samples + " / " + sampleCapacity + " in the sample box)");
    }

    // ---------------- SCIENCE ----------------
    // Same mechanics as Collect Sample: stop, find a rock in reach, wait for power, arm out, work, arm back
    IEnumerator AnalyzeRock()
    {
        yield return BrakeToStop();                      // the drill works only when standing still
        Collider rock = RockInReach();
        if (rock == null) { yield return Problem("no rock within " + drillReach + " m of the drill. Get closer to a rock first."); yield break; }
        if (analyzedRocks.Contains(rock)) { yield return Problem("this rock is already analyzed. Find another rock."); yield break; }
        if (waitingAnalyses.Count >= analysisCapacity)
        {
            yield return Problem("the analysis memory is full (" + analysisCapacity + (analysisCapacity == 1 ? " analysis" : " analyses")
                                 + "). Use Send Data before analyzing another rock.");
            yield break;
        }

        while (recharging || energy < analyzeEnergy * MassFactor)    // the drill needs power
        {
            message = "<color=#FFC63A>Not enough power to analyze: waiting for the solar panel...</color>";
            yield return null;
        }
        UseEnergy(analyzeEnergy);
        if (drillArm)
        {
            message = "Moving the drill arm to the rock...";
            yield return drillArm.Reach(rock.bounds.center);
        }
        float time = 3f / drillSpeed;
        for (float t = 0f; t < time; t += Time.deltaTime)
        {
            message = "Analyzing rock... " + Mathf.RoundToInt(t / time * 100f) + "%";
            yield return null;
        }
        analyzedRocks.Add(rock);
        waitingAnalyses.Add(rock.GetComponentInParent<AnalysisTarget>());   // checked when the data reaches the lander
        if (drillArm)
        {
            message = "Folding the drill arm...";
            yield return drillArm.Fold();
        }
        Say("Rock analyzed! (" + DataWaiting + " waiting to send to the lander)");
    }

    // Stop, take a picture with the onboard camera and keep it until Send Data
    IEnumerator TakePhoto()
    {
        if (!cam.HasCamera) { yield return Problem("no camera fitted. Fit a camera in the garage."); yield break; }
        if (waitingPhotos.Count >= photoCapacity)
        {
            yield return Problem("the photo memory is full (" + photoCapacity + (photoCapacity == 1 ? " photo" : " photos")
                                 + "). Use Send Data before taking another photo.");
            yield break;
        }
        yield return BrakeToStop();                      // stand still for a sharp photo
        while (recharging || energy < photoEnergy)       // the camera doesn't care about mass
        {
            message = "<color=#FFC63A>Not enough power for a photo: waiting for the solar panel...</color>";
            yield return null;
        }
        for (float t = 0f; t < photoTime; t += Time.deltaTime)
        {
            message = "Taking photo...";
            yield return null;
        }
        int rocks = cam.RocksInView, found = cam.SamplesInView;
        PhotoTarget target = cam.TargetInView();         // checked when the data reaches the lander
        Texture2D photo = cam.TakePhoto();
        UseEnergy(photoEnergy, false);
        if (photo != null)
        {
            photos.Add(photo);
            ShowPreview(photo);
        }
        waitingPhotos.Add(target);
        Say("Photo " + (waitingPhotos.Count + photosSent + wrongPhotos) + " taken: " + rocks + (rocks == 1 ? " rock, " : " rocks, ")
            + found + (found == 1 ? " sample" : " samples") + " in view (" + DataWaiting + " waiting to send)");
    }

    // Send everything waiting (analyses first, then photos) to the nearest lander, if it is in range
    IEnumerator SendData()
    {
        yield return BrakeToStop();
        if (DataWaiting == 0) { yield return Problem("no data to send. Use Analyze Rock or Take Photo first."); yield break; }
        Lander lander = Lander.Nearest(transform.position, out float distance);
        if (lander == null) { yield return Problem("there is no lander in this mission."); yield break; }
        if (!lander.InRange(transform.position))
        {
            yield return Problem("the lander is out of range (" + distance.ToString("0.0") + " m away, range "
                                 + lander.range.ToString("0.#") + " m). Drive closer first.");
            yield break;
        }

        int total = DataWaiting, done = 0, sentAnalyses = 0, sentPhotos = 0, wrong = 0, wrongRocks = 0;
        var targets = new List<string>();
        while (DataWaiting > 0)
        {
            bool photo = waitingAnalyses.Count == 0;     // analyses first (small), then photos
            while (recharging || energy < sendEnergy)    // the radio doesn't care about mass
            {
                message = "<color=#FFC63A>Not enough power to send: waiting for the solar panel...</color>";
                yield return null;
            }
            float time = sendTime * (photo ? 2f : 1f);
            for (float t = 0f; t < time; t += Time.deltaTime)
            {
                message = "Sending " + (photo ? "photo" : "rock analysis") + " to the lander... "
                          + (done + 1) + "/" + total + "  " + Mathf.RoundToInt(t / time * 100f) + "%";
                yield return null;
            }
            UseEnergy(sendEnergy, false);
            if (photo)
            {
                PhotoTarget target = waitingPhotos[0];
                waitingPhotos.RemoveAt(0);
                if (target != null)                      // the lander checks the photo: is the target in it?
                {
                    photosSent++;
                    sentPhotos++;
                    if (!targets.Contains(target.targetName)) targets.Add(target.targetName);
                }
                else
                {
                    wrongPhotos++;
                    wrong++;
                }
            }
            else
            {
                AnalysisTarget target = waitingAnalyses[0];
                waitingAnalyses.RemoveAt(0);
                if (target != null)                      // the lander checks the analysis: was it the target rock?
                {
                    analysesSent++;
                    sentAnalyses++;
                    if (!targets.Contains(target.targetName)) targets.Add(target.targetName);
                }
                else
                {
                    wrongAnalyses++;
                    wrongRocks++;
                }
            }
            done++;
        }

        string sent = "Data sent to the lander: " + sentAnalyses + (sentAnalyses == 1 ? " analysis, " : " analyses, ")
                    + sentPhotos + (sentPhotos == 1 ? " photo" : " photos")
                    + (targets.Count > 0 ? " (target: " + string.Join(", ", targets) + ")" : "");
        if (wrong == 0 && wrongRocks == 0)
        {
            Say("<color=#7CFC7C>" + sent + "</color>");
            yield break;
        }

        var errors = new List<string>();
        if (wrongRocks > 0)
            errors.Add("WRONG ANALYSIS! " + (wrongRocks == 1 ? "1 analysis is" : wrongRocks + " analyses are")
                       + " not of the target rock. Analyze the target rock.");
        if (wrong > 0)
            errors.Add("WRONG PHOTO! " + (wrong == 1 ? "1 photo has" : wrong + " photos have")
                       + " no target in it. Take the targeted photo.");
        string error = string.Join("\n", errors);
        Fail(error);                                     // wrong data: the program stops
    }

    // A block that can't do its job: the program stops (RunBlocks ends the run with this error in red)
    IEnumerator Problem(string error)
    {
        Fail("ERROR: " + error);
        yield break;
    }

    // The nearest rock closer than drillReach to the nose, in front of the rover
    Collider RockInReach()
    {
        Collider best = null;
        float bestDistance = drillReach;
        foreach (Collider c in Physics.OverlapSphere(nose.position, drillReach + 1f))
        {
            if (!cam.IsRock(c) || cam.IsSample(c)) continue;
            Vector3 point = c.bounds.ClosestPoint(nose.position);
            if (Vector3.Dot(point - nose.position, transform.forward) < -0.2f) continue;   // behind the nose
            float distance = cam.GroundDistance(point);
            if (distance < bestDistance) { bestDistance = distance; best = c; }
        }
        return best;
    }

    // Restart: no data, no photos
    void ClearData()
    {
        foreach (Texture2D photo in photos) if (photo) Destroy(photo);
        photos.Clear();
        analyzedRocks.Clear();
        waitingPhotos.Clear();
        waitingAnalyses.Clear();
        analysesSent = photosSent = wrongPhotos = wrongAnalyses = 0;
        HidePreview();
    }

    void OnDestroy()
    {
        foreach (Texture2D photo in photos) if (photo) Destroy(photo);
    }

    // ---------------- PHOTO PREVIEW ----------------
    // Shows the photo for previewTime seconds, then fades it out. A new photo starts the timer again.
    // Fades a CanvasGroup on the preview if it has one (frame and children fade too), otherwise the RawImage's alpha.
    void ShowPreview(Texture2D photo)
    {
        if (photoPreview == null) return;
        if (previewRoutine != null) StopCoroutine(previewRoutine);
        photoPreview.texture = photo;
        previewRoutine = StartCoroutine(PreviewThenFade());
    }

    IEnumerator PreviewThenFade()
    {
        photoPreview.gameObject.SetActive(true);
        SetPreviewAlpha(1f);
        yield return new WaitForSeconds(previewTime);
        for (float t = 0f; t < previewFadeTime; t += Time.deltaTime)
        {
            SetPreviewAlpha(1f - t / previewFadeTime);
            yield return null;
        }
        previewRoutine = null;
        HidePreview();
    }

    void HidePreview()
    {
        if (previewRoutine != null) { StopCoroutine(previewRoutine); previewRoutine = null; }
        if (photoPreview == null) return;
        SetPreviewAlpha(0f);
        photoPreview.texture = null;
        photoPreview.gameObject.SetActive(false);
    }

    void SetPreviewAlpha(float alpha)
    {
        CanvasGroup group = photoPreview.GetComponent<CanvasGroup>();
        if (group != null) { group.alpha = alpha; return; }
        Color color = photoPreview.color;
        color.a = alpha;
        photoPreview.color = color;
    }

    // The nearest sample closer than drillReach to the nose, in front of the rover
    Collider SampleInReach()
    {
        Collider best = null;
        float bestDistance = drillReach;
        foreach (Collider c in Physics.OverlapSphere(nose.position, drillReach + 1f))
        {
            if (!cam.IsSample(c)) continue;
            Vector3 point = c.bounds.ClosestPoint(nose.position);
            if (Vector3.Dot(point - nose.position, transform.forward) < -0.2f) continue;   // behind the nose
            float distance = cam.GroundDistance(point);
            if (distance < bestDistance) { bestDistance = distance; best = c; }
        }
        return best;
    }

    // ---------------- WHEELS ----------------
    void SpinWheels(float forward, float turnDegrees)
    {
        if (suspension != null) { suspension.Roll(forward, turnDegrees); return; }     // the suspension turns the wheels
        foreach (Transform wheel in wheels)
        {
            float side = Vector3.Dot(wheel.position - transform.position, transform.right);   // left < 0 < right
            float roll = forward - turnDegrees * Mathf.Deg2Rad * side;                         // sides spin opposite when turning
            wheel.Rotate(transform.right, roll / wheelRadius * Mathf.Rad2Deg, Space.World);
        }
    }

    // ---------------- UI ----------------
    void Highlight(CodeBlock block)          // the running block gets a black outline and is drawn on top
    {
        if (highlighted) { SetOutline(highlighted, false); SetOnTop(highlighted, false); }
        highlighted = block;
        if (block) { SetOutline(block, true); SetOnTop(block, true); }
    }

    static void SetOutline(CodeBlock block, bool on)
    {
        Graphic g = block.body.GetComponent<Graphic>();
        if (g == null) return;
        Outline o = g.GetComponent<Outline>();
        if (on)
        {
            if (o == null) o = g.gameObject.AddComponent<Outline>();
            o.effectColor = Color.black;
            o.effectDistance = new Vector2(4f, -4f);   // outline thickness in pixels
            o.enabled = true;
        }
        else if (o != null) o.enabled = false;
    }

    static void SetOnTop(CodeBlock block, bool on)   // draws this block above all the others
    {
        Canvas top = block.GetComponent<Canvas>();
        if (on)
        {
            if (top == null) top = block.gameObject.AddComponent<Canvas>();
            top.overrideSorting = true;
            top.sortingOrder = 10;
        }
        else if (top != null)
        {
            // Remove it NOW (Destroy waits until the frame ends) and re-clip: otherwise the block's images and
            // texts keep ignoring the Code Editor frame's RectMask2D and draw outside it when zoomed or panned
            DestroyImmediate(top);
            foreach (MaskableGraphic g in block.GetComponentsInChildren<MaskableGraphic>(true))
            {
                g.RecalculateClipping();
                g.RecalculateMasking();
            }
        }
    }

    void SetRunLabel()
    {
        if (runButtonLabel) runButtonLabel.text = IsRunning ? "STOP" : "RUN";
        if (runButtonImage) runButtonImage.color = IsRunning ? stopColor : runColor;
    }

    public void Message(string text) => Say(text);      // other scripts (the AI button) write to the Rover Status

    void Say(string text)
    {
        message = text;
        Debug.Log("[Rover] " + text);
    }

    // Minimal panel: what the rover is doing, battery, this mission's goal (from the MissionHandler) and time.
    // Distances, angles and sizes are on the camera's detection boxes (If blocks can still read them all).
    const string Divider = "  |  ";

    void ShowStatus()
    {
        if (statusText == null && statsText == null) return;
        if (statusText == null) { statusText = statsText; statsText = null; }   // only one text given: use it for both
        if (IsRunning) runTime = Time.time - runStart;

        string stats = "Battery " + BatteryPercent.ToString("0") + "%"
                     + (recharging ? " <color=#FFC63A>charging</color>" : "");
        string goal = MissionHandler.Current != null ? MissionHandler.Current.Objective() : "";   // depends on the mission
        if (goal != "") stats += Divider + goal.Replace("    ", Divider);   // the goal's own parts too

        string time = IsRunning ? Divider + "Time " + runTime.ToString("0") + "s"
                    : runTime > 0f ? Divider + "Last run " + runTime.ToString("0.0") + "s" : "";

        if (statsText != null)
        {
            statusText.text = message;
            statsText.text = stats + time;
        }
        else statusText.text = message + "\n\n" + stats + time;
    }
}