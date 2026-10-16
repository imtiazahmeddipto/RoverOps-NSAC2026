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
    Stop
}

// The rover: wheels, crash sensor, drill, readings, variables and the code-block runner.
//
// READINGS (the camera is always on):  rock dist, sample dist, sample angle, samples, battery (%)
// Every number slot also accepts a reading or variable name, e.g.  Turn Right [sample angle]
//
// One sensor rule: an "If" placed right after a Move WATCHES the camera during that move.
// The moment its test is true, the move is cancelled and the If part runs.
// If the move finishes first, the Else part runs. Any other If checks once.
public class Rover : MonoBehaviour
{
    public static readonly string[] Readings = { "rock dist", "sample dist", "sample angle", "samples", "battery" };

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

    [Header("Links")]
    public RoverCamera cam;
    public Transform nose;              // front of the rover
    public Transform[] wheels;
    public float wheelRadius = 0.2f;
    public Transform programArea;       // the Code Editor's block list
    public TMP_Text statusText;
    public TMP_Text runButtonLabel;
    public string garageScene = "RoverOps Garage";     // the GARAGE button loads this scene

    public static bool IsRunning { get; private set; }

    class Loop { public int start, left; public bool isWhile; }   // left < 0 = forever

    Rigidbody rb;
    BoxCollider body;
    Vector3 startPosition;
    Quaternion startRotation;
    Coroutine program;
    CodeBlock highlighted, errorBlock;
    string message = "";
    bool crashed, failed;
    int samples;
    readonly List<GameObject> collected = new List<GameObject>();

    // motion order (FixedUpdate carries it out)
    float stopAt = -1f;                 // odometer reading to stop on (-1 = no move, the rover brakes)
    float moveDir = 1f;                 // +1 forward, -1 backward
    float speed, odometer;              // m/s, metres driven
    float energy;
    bool recharging;                    // the battery ran empty: wait for the sun

    public float BatteryPercent => batteryCapacity > 0f ? energy / batteryCapacity * 100f : 0f;

    bool Moving => stopAt >= 0f;
    float MassFactor => mass / 300f;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        body = GetComponent<BoxCollider>();
        rb.mass = mass;
        energy = batteryCapacity;
        startPosition = transform.position;
        startRotation = transform.rotation;
        IsRunning = false;
        SetRunLabel();
        Say("Drag blocks under 'When mission starts', then press RUN");
    }

    void Update() => ShowStatus();      // live telemetry

    // ---------------- WHEELS (every physics step) ----------------
    void FixedUpdate()
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
        Vector3 next = rb.position + rb.rotation * Vector3.forward * (step * moveDir);
        if (Mathf.Abs(next.x) > areaSize / 2f || Mathf.Abs(next.z) > areaSize / 2f) { Crash("The rover drove out of the test area!"); return; }

        rb.MovePosition(next);
        SpinWheels(step * moveDir, 0f);
        odometer += step;
        UseEnergy(step * energyPerMeter);
        if (Touching(out string what)) Crash("The rover crashed into a " + what + "!");
    }

    // Is the rover's body touching anything (except the ground)?
    bool Touching(out string what)
    {
        what = "";
        Vector3 center = rb.position + rb.rotation * body.center;
        foreach (Collider c in Physics.OverlapBox(center, body.size * 0.49f, rb.rotation))
        {
            if (c.transform.IsChildOf(transform) || c.name == "Ground") continue;
            what = RoverCamera.IsSample(c) ? "sample" : "rock";
            return true;
        }
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
        StopProgram("<color=#FF6B6B>ROVER CRASHED. " + text + "\nPress RESTART and try again.</color>");
        Debug.LogWarning("[Rover] Crashed: " + text);
    }

    // ---------------- BUTTONS ----------------
    public void Run()                   // RUN button (shows STOP while a program runs)
    {
        if (IsRunning) { StopProgram("Program stopped"); return; }
        if (crashed) { Say("<color=#FF6B6B>The rover crashed. Press RESTART first.</color>"); return; }
        var all = new List<CodeBlock>(programArea.GetComponentsInChildren<CodeBlock>());
        int start = all.FindIndex(b => b.type == Block.Start);
        if (start < 0) { Say("Add a 'When mission starts' block first"); return; }
        List<CodeBlock> script = all.GetRange(start + 1, all.Count - start - 1);

        MarkError(null);
        string problem = CheckStructure(script, out CodeBlock bad);           // every If / Repeat needs its End
        if (problem != null)
        {
            MarkError(bad);
            Say("<color=#FF6B6B>Can't run: " + problem + " (marked in red)</color>");
            return;
        }

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
        rb.position = startPosition;
        rb.rotation = startRotation;
        transform.SetPositionAndRotation(startPosition, startRotation);
        foreach (GameObject sample in collected) sample.SetActive(true);
        collected.Clear();
        samples = 0;
        Refill();
    }

    // GARAGE button: back to the garage to change parts
    public void OpenGarage()
    {
        if (IsRunning) { Say("Stop the program before going to the garage"); return; }
        SceneManager.LoadScene(garageScene);
    }

    public void Clear()
    {
        if (IsRunning) return;
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
        stopAt = -1f;                   // the rover brakes to a stop by itself
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
        EndProgram("Program finished");
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
                    bool watched = i + 1 < blocks.Count && blocks[i + 1].type == Block.If;
                    if (!watched) while (Moving) yield return null;               // wait for the move to finish
                    break;                                                        // else: the If after it watches the move

                case Block.TurnLeft:
                case Block.TurnRight:
                    float degrees = Num(block, 90f);
                    if (failed) break;
                    yield return Turn(block.type == Block.TurnLeft ? -degrees : degrees);
                    break;

                // ----- sensors & actions -----

                case Block.CollectSample: yield return CollectSample(); break;

                case Block.Wait:
                    float seconds = Num(block, 1f);
                    if (failed) break;
                    Say("Waiting " + seconds + " s");
                    yield return new WaitForSeconds(Mathf.Max(0f, seconds));
                    break;

                // ----- conditions -----
                case Block.If:
                    bool yes = false;
                    bool watching = Moving && i > 0 && (blocks[i - 1].type == Block.MoveForward || blocks[i - 1].type == Block.MoveBackward);
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
                EndProgram("<color=#FF6B6B>" + message + "</color>");
                yield break;
            }
            yield return null;          // every block takes at least one frame (keeps loops safe)
        }
        Highlight(null);
    }

    // ---------------- STRUCTURE CHECK ----------------
    static bool IsLoop(Block t) => t == Block.RepeatTimes || t == Block.RepeatWhile || t == Block.RepeatForever;

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

    void MarkError(CodeBlock block)          // tint a faulty block red
    {
        if (errorBlock) errorBlock.body.GetComponent<Image>().color = Color.white;
        errorBlock = block;
        if (block) block.body.GetComponent<Image>().color = new Color(1f, 0.55f, 0.55f);
    }

    // The If test. While watching a move, it tests every frame until the move ends.
    IEnumerator CheckIf(CodeBlock block, bool watchMove, System.Action<bool> answer)
    {
        if (watchMove)
        {
            Say("Moving and watching: " + block.Selected + " " + block.Sign + " " + block.ValueText + " ?");
            while (Moving)
            {
                if (Test(block, out string hit, false))
                {
                    Say("If " + hit + ". Move cancelled.");
                    yield return BrakeToStop();
                    answer(true);
                    yield break;
                }
                if (failed) { answer(false); yield break; }
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
        if (freshScan) cam.Scan();
        Read(block.Selected, out float left);
        float right = Num(block, 0f);
        bool yes = block.Sign == "<" ? left < right
                 : block.Sign == ">" ? left > right
                 : Mathf.Abs(left - right) < 0.05f;
        text = block.Selected + " (" + Show(block.Selected, left) + ") " + block.Sign + " " + right.ToString("0.##") + ": " + (yes ? "YES" : "NO");
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

    // A reading or variable by name. False if the name is unknown.
    bool Read(string name, out float value)
    {
        switch (Key(name))
        {
            case "rockdist": value = cam.RockDistance; return true;
            case "sampledist": value = cam.SampleDistance; return true;
            case "sampleangle": value = cam.SampleAngle; return true;
            case "samples": value = samples; return true;
            case "battery": value = BatteryPercent; return true;
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
        Fail("ERROR: '" + text + "' is not a number or a reading. Use a number, rock dist, sample dist, sample angle, samples or battery.\");");
        return defaultValue;
    }

    void Fail(string text)
    {
        failed = true;
        message = text;
        Debug.LogError("[Rover] " + text);
    }

    static string Show(string name, float value)
    {
        if (float.IsInfinity(value)) return "none";
        string key = Key(name);
        if (key.EndsWith("dist")) return value.ToString("0.0") + " m";
        if (key == "sampleangle") return value.ToString("0") + "°";
        if (key == "battery") return value.ToString("0") + "%";
        return value.ToString("0.##");
    }

    // ---------------- ORDERS ----------------
    IEnumerator StartMove(float meters)
    {
        float dir = Mathf.Sign(meters);
        if (speed > 0f && dir != moveDir) yield return BrakeToStop();      // stop before changing direction
        Say(meters >= 0f ? "Moving forward " + meters.ToString("0.##") + " m" : "Moving back " + (-meters).ToString("0.##") + " m");
        moveDir = dir;
        if (Mathf.Abs(meters) > 0.005f) stopAt = odometer + Mathf.Abs(meters);
    }

    IEnumerator BrakeToStop()
    {
        stopAt = -1f;
        while (speed > 0f) yield return null;
    }

    IEnumerator Turn(float degrees)                      // negative = left, positive = right
    {
        yield return BrakeToStop();                      // a rover turns on the spot, so it stops first
        Say(degrees < 0f ? "Turning left " + (-degrees).ToString("0.#") + "°" : "Turning right " + degrees.ToString("0.#") + "°");
        float turned = 0f, target = Mathf.Abs(degrees), direction = Mathf.Sign(degrees);
        while (turned < target)
        {
            while (recharging) yield return null;        // empty battery: wait for the sun
            float step = Mathf.Min(turnSpeed * Time.fixedDeltaTime, target - turned);
            UseEnergy(step / 90f * energyPerTurn);
            rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, step * direction, 0f));
            SpinWheels(0f, step * direction);
            turned += step;
            yield return new WaitForFixedUpdate();
            if (Touching(out string what)) { Crash("The rover hit a " + what + " while turning!"); yield break; }
        }
    }

    IEnumerator CollectSample()
    {
        yield return BrakeToStop();                      // drill only when standing still
        Collider sample = SampleInReach();
        if (sample == null)
        {
            string error = "ERROR: no sample within " + drillReach + " m of the drill. Get closer to a sample first.";
            Say("<color=#FF6B6B>" + error + "</color>");
            Debug.LogError("[Rover] " + error);
            yield return new WaitForSeconds(1f);
            yield break;
        }
        if (samples >= sampleCapacity)
        {
            string error = "ERROR: the sample box is full (" + sampleCapacity + " samples). Fit a bigger drill in the garage.";
            Say("<color=#FF6B6B>" + error + "</color>");
            Debug.LogError("[Rover] " + error);
            yield return new WaitForSeconds(1f);
            yield break;
        }
        while (recharging || energy < drillEnergy * MassFactor)        // the drill needs power
        {
            message = "<color=#FFC63A>Not enough power to drill: waiting for the solar panel...</color>";
            yield return null;
        }
        UseEnergy(drillEnergy);
        float time = 3f / drillSpeed;
        for (float t = 0f; t < time; t += Time.deltaTime)
        {
            message = "Drilling... " + Mathf.RoundToInt(t / time * 100f) + "%";
            yield return null;
        }
        // TODO: drill animation
        sample.gameObject.SetActive(false);
        collected.Add(sample.gameObject);
        samples++;
        Say("Sample collected! (" + samples + " / " + sampleCapacity + " in the sample box)");
    }

    // The nearest sample closer than drillReach to the nose, in front of the rover
    Collider SampleInReach()
    {
        Collider best = null;
        float bestDistance = drillReach;
        foreach (Collider c in Physics.OverlapSphere(nose.position, drillReach + 1f))
        {
            if (!RoverCamera.IsSample(c)) continue;
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
        foreach (Transform wheel in wheels)
        {
            float side = Vector3.Dot(wheel.position - transform.position, transform.right);   // left < 0 < right
            float roll = forward - turnDegrees * Mathf.Deg2Rad * side;                         // sides spin opposite when turning
            wheel.Rotate(transform.right, roll / wheelRadius * Mathf.Rad2Deg, Space.World);
        }
    }

    // ---------------- UI ----------------
    void Highlight(CodeBlock block)          // the running block grows a little
    {
        if (highlighted) highlighted.body.localScale = Vector3.one;
        highlighted = block;
        if (block) block.body.localScale = Vector3.one * 1.08f;
    }

    void SetRunLabel()
    {
        if (runButtonLabel) runButtonLabel.text = IsRunning ? "STOP" : "RUN";
    }

    void Say(string text)
    {
        message = text;
        Debug.Log("[Rover] " + text);
    }

    void ShowStatus()
    {
        if (statusText == null) return;
        string readings = "rock dist " + Show("rock dist", cam.RockDistance) + "   sample dist " + Show("sample dist", cam.SampleDistance)
              + "   sample angle " + Show("sample angle", cam.SampleAngle);
        string text = message + "\n\n" + readings + "\nsamples " + samples
                    + "\nspeed " + speed.ToString("0.0") + " m/s   odometer " + odometer.ToString("0.0") + " m   battery " + Show("battery", BatteryPercent)
                    + (recharging ? " <color=#FFC63A>(recharging)</color>" : "");
        statusText.text = text;
    }
}
