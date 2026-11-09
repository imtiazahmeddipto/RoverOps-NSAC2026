using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// ALL the game's audio in one place: background music, rover loops, sound effects and UI sounds.
// It starts by itself when the game starts and lives for the whole game (nothing to set up, nothing to attach).
//
//   GameAudio.Play(GameAudio.Sfx.Photo)   - play a one-shot sound from any script
//   GameAudio.DuckMusic(3.5f)             - lower the music for a few seconds (e.g. mission complete)
//   Buttons click / hover by themselves   - every Button in every scene, also ones made at runtime
//   Rover loops follow Rover.Current      - driving, drilling, charging (fade in / out)
//
// Sounds: Resources/Sounds/<file>.wav   Music: Resources/Music/bg_music   (replace a file, same name, to change it)
public class GameAudio : MonoBehaviour
{
    // ---------------- SETTINGS (change here) ----------------
    public static float musicVolume = 0.3f;
    public static float effectsVolume = 0.8f;     // every sound effect, UI and rover
    public static bool hoverSounds = true;

    const float LoopFade = 0.15f;                 // seconds for a rover loop to fade in / out
    const float MusicFadeIn = 3f;

    // ---------------- ONE-SHOT SOUNDS ----------------
    public enum Sfx
    {
        Click, Hover, BlockPickup, BlockDrop, Error, Purchase, Equip, PageTurn, MissionComplete, Transition,
        Photo, SendData, SampleCollected, RockAnalyzed, Crash, WheelSlip, BatteryEmpty, RunStart, ProgramError,
    }

    // file and volume for each Sfx, in the same order
    static readonly (string file, float volume)[] Sounds =
    {
        ("ui_click", 0.6f), ("ui_hover", 0.25f), ("ui_block_pickup", 0.5f), ("ui_block_drop", 0.6f),
        ("ui_error", 0.6f), ("ui_purchase", 0.7f), ("ui_equip", 0.7f), ("ui_page_turn", 0.5f),
        ("mission_complete", 0.75f), ("ui_transition", 0.55f),
        ("rover_photo", 0.8f), ("rover_send_data", 0.8f), ("rover_sample_collected", 0.8f), ("rover_rock_analyzed", 0.8f),
        ("rover_crash", 0.8f), ("rover_wheel_slip", 0.8f), ("rover_battery_empty", 0.8f), ("rover_run_start", 0.8f),
        ("program_error", 0.8f),
    };

    // ---------------- ROVER LOOPS ----------------
    class Loop
    {
        public AudioSource source;
        public float volume;
        public Loop(AudioSource s, float v) { source = s; volume = v; }
    }

    static GameAudio instance;
    AudioClip[] clips;
    AudioSource effects, music;
    Loop drive, drill, charging;

    float duck = 1f, duckUntil;

    // UI pointer (click / hover for every button)
    readonly List<RaycastResult> hits = new List<RaycastResult>();
    PointerEventData pointer;
    EventSystem pointerSystem;                    // the EventSystem 'pointer' was made for (each scene has its own)
    Vector2 lastPointer;
    Button hovered, pressed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (instance != null) return;
        GameObject go = new GameObject("Game Audio");
        DontDestroyOnLoad(go);                    // one for the whole game: music never restarts between scenes
        go.AddComponent<GameAudio>();
    }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(this); return; }
        instance = this;

        clips = new AudioClip[Sounds.Length];
        for (int i = 0; i < Sounds.Length; i++) clips[i] = Load("Sounds/" + Sounds[i].file);

        effects = NewSource(null, false);
        music = NewSource(Load("Music/bg_music"), true);
        music.priority = 0;                       // never cut off by effects
        if (music.clip) music.Play();

        drive = new Loop(NewSource(Load("Sounds/rover_drive_loop"), true), 0.45f);
        drill = new Loop(NewSource(Load("Sounds/rover_drill_loop"), true), 0.55f);
        charging = new Loop(NewSource(Load("Sounds/rover_charging_loop"), true), 0.525f);
    }

    void OnDestroy() { if (instance == this) instance = null; }

    static AudioClip Load(string path)
    {
        AudioClip clip = Resources.Load<AudioClip>(path);
        if (clip == null) Debug.LogWarning("[GameAudio] Missing sound: Resources/" + path);
        return clip;
    }

    AudioSource NewSource(AudioClip clip, bool loop)
    {
        AudioSource s = gameObject.AddComponent<AudioSource>();
        s.clip = clip;
        s.loop = loop;
        s.playOnAwake = false;
        s.spatialBlend = 0f;                      // 2D
        s.volume = loop ? 0f : 1f;
        return s;
    }

    // ---------------- PUBLIC ----------------
    public static void Play(Sfx sfx)
    {
        if (instance == null) return;
        if (sfx == Sfx.Hover && !hoverSounds) return;
        AudioClip clip = instance.clips[(int)sfx];
        if (clip) instance.effects.PlayOneShot(clip, Sounds[(int)sfx].volume * effectsVolume);
    }

    public static void DuckMusic(float seconds)
    {
        if (instance != null) instance.duckUntil = Time.unscaledTime + seconds;
    }

    // ---------------- EVERY FRAME ----------------
    void Update()
    {
        float dt = Time.unscaledDeltaTime;        // audio fades ignore the run speed

        // music: fade in, dip for a moment when ducked
        float targetDuck = Time.unscaledTime < duckUntil ? 0.35f : 1f;
        duck = Mathf.MoveTowards(duck, targetDuck, dt * (targetDuck < duck ? 3f : 0.5f));
        float musicTarget = musicVolume * duck;
        music.volume = Mathf.Min(musicTarget, Mathf.MoveTowards(music.volume, musicTarget, dt * musicVolume / MusicFadeIn));

        // rover loops (silent when there's no rover in the scene)
        Rover rover = Rover.Current;
        FadeLoop(drive, rover != null && rover.IsDriving, dt);
        if (rover != null) drive.source.pitch = 0.85f + 0.3f * rover.SpeedPercent;   // the motor rises with speed
        FadeLoop(drill, rover != null && rover.IsDrilling, dt);
        FadeLoop(charging, rover != null && rover.IsCharging, dt);

        UiPointer();
    }

    static void FadeLoop(Loop loop, bool on, float dt)
    {
        AudioSource s = loop.source;
        if (s.clip == null) return;
        float target = on ? loop.volume * effectsVolume : 0f;
        s.volume = Mathf.MoveTowards(s.volume, target, dt / LoopFade);
        if (s.volume > 0f && !s.isPlaying) s.Play();
        else if (s.volume <= 0f && s.isPlaying) s.Stop();    // silent loops cost nothing
    }

    // Click and hover for every Button, without adding anything to the buttons.
    // Only looks at the UI when the pointer moves or is pressed / released.
    void UiPointer()
    {
        EventSystem es = EventSystem.current;
        if (es == null) return;
#if ENABLE_INPUT_SYSTEM
        UnityEngine.InputSystem.Pointer p = UnityEngine.InputSystem.Pointer.current;
        if (p == null) return;
        Vector2 pos = p.position.ReadValue();
        bool down = p.press.wasPressedThisFrame, up = p.press.wasReleasedThisFrame;
#else
        Vector2 pos = Input.mousePosition;
        bool down = Input.GetMouseButtonDown(0), up = Input.GetMouseButtonUp(0);
#endif
        bool moved = (pos - lastPointer).sqrMagnitude > 0.25f;
        lastPointer = pos;
        if (!moved && !down && !up) return;

        if (pointer == null || pointerSystem != es) { pointer = new PointerEventData(es); pointerSystem = es; }
        pointer.position = pos;
        hits.Clear();
        es.RaycastAll(pointer, hits);
        Button over = hits.Count > 0 ? hits[0].gameObject.GetComponentInParent<Button>() : null;

        if (over != hovered)
        {
            hovered = over;
            if (over != null && pressed == null && over.IsInteractable()) Play(Sfx.Hover);
        }
        if (down) pressed = over;
        if (up)
        {
            if (over != null && over == pressed && over.IsInteractable()) Play(Sfx.Click);   // a real click: pressed and released on it
            pressed = null;
        }
    }
}
