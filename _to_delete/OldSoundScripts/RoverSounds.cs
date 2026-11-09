using UnityEngine;

// The rover's sound effects. Rover adds this by itself: nothing to set up.
//   Loops (fade in / out while it happens): driving and turning, drilling, solar charging
//   One-shots (Rover calls Play): photo, send data, sample collected, rock analyzed, crash, wheel slip,
//                                 battery empty, program start, program error
// Empty clip fields use the default sounds in Resources/Sounds. Drag in your own to replace one.
[RequireComponent(typeof(Rover))]
public class RoverSounds : MonoBehaviour
{
    public enum Cue { Photo, SendData, SampleCollected, RockAnalyzed, Crash, WheelSlip, BatteryEmpty, RunStart, ProgramError }

    [Header("Loops (play while it happens)")]
    public AudioClip drive;
    public AudioClip drill;
    public AudioClip charging;

    [Header("One-shots")]
    public AudioClip photo;
    public AudioClip sendData;
    public AudioClip sampleCollected;
    public AudioClip rockAnalyzed;
    public AudioClip crash;
    public AudioClip wheelSlip;
    public AudioClip batteryEmpty;
    public AudioClip runStart;
    public AudioClip programError;

    [Header("Volume")]
    [Range(0f, 1f)] public float volume = 0.8f;             // everything
    [Range(0f, 1f)] public float driveVolume = 0.45f;
    [Range(0f, 1f)] public float drillVolume = 0.55f;
    [Range(0f, 1f)] public float chargingVolume = 0.525f;   // 30% lower than 0.75
    [Range(0f, 1f)] public float effectsVolume = 0.8f;      // the one-shots
    public float fadeTime = 0.15f;                          // seconds for a loop to fade in / out (no clicks)

    Rover rover;
    AudioSource driveSource, drillSource, chargeSource, effects;

    void Awake()
    {
        rover = GetComponent<Rover>();

        if (!drive) drive = Load("rover_drive_loop");
        if (!drill) drill = Load("rover_drill_loop");
        if (!charging) charging = Load("rover_charging_loop");
        if (!photo) photo = Load("rover_photo");
        if (!sendData) sendData = Load("rover_send_data");
        if (!sampleCollected) sampleCollected = Load("rover_sample_collected");
        if (!rockAnalyzed) rockAnalyzed = Load("rover_rock_analyzed");
        if (!crash) crash = Load("rover_crash");
        if (!wheelSlip) wheelSlip = Load("rover_wheel_slip");
        if (!batteryEmpty) batteryEmpty = Load("rover_battery_empty");
        if (!runStart) runStart = Load("rover_run_start");
        if (!programError) programError = Load("program_error");

        driveSource = MakeSource(drive, true);
        drillSource = MakeSource(drill, true);
        chargeSource = MakeSource(charging, true);
        effects = MakeSource(null, false);
    }

    static AudioClip Load(string name) => Resources.Load<AudioClip>("Sounds/" + name);

    AudioSource MakeSource(AudioClip clip, bool loop)
    {
        AudioSource s = gameObject.AddComponent<AudioSource>();
        s.clip = clip;
        s.loop = loop;
        s.playOnAwake = false;
        s.spatialBlend = 0f;            // 2D: heard the same whichever camera is shown
        s.volume = loop ? 0f : 1f;
        return s;
    }

    void Update()
    {
        Fade(driveSource, rover.IsDriving ? driveVolume : 0f);
        driveSource.pitch = 0.85f + 0.3f * rover.SpeedPercent;     // the motor sounds higher when driving faster
        Fade(drillSource, rover.IsDrilling ? drillVolume : 0f);
        Fade(chargeSource, rover.IsCharging ? chargingVolume : 0f);
    }

    // Smoothly toward the target volume; starts / stops the loop at silence
    void Fade(AudioSource s, float target)
    {
        if (s.clip == null) return;
        target *= volume;
        s.volume = Mathf.MoveTowards(s.volume, target, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeTime));
        if (s.volume > 0f && !s.isPlaying) s.Play();
        else if (s.volume <= 0f && s.isPlaying) s.Stop();
    }

    // Rover calls this for the one-shot sounds
    public void Play(Cue cue)
    {
        AudioClip clip;
        switch (cue)
        {
            case Cue.Photo: clip = photo; break;
            case Cue.SendData: clip = sendData; break;
            case Cue.SampleCollected: clip = sampleCollected; break;
            case Cue.RockAnalyzed: clip = rockAnalyzed; break;
            case Cue.Crash: clip = crash; break;
            case Cue.WheelSlip: clip = wheelSlip; break;
            case Cue.BatteryEmpty: clip = batteryEmpty; break;
            case Cue.RunStart: clip = runStart; break;
            default: clip = programError; break;
        }
        if (clip) effects.PlayOneShot(clip, effectsVolume * volume);
    }
}
