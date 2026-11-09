using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Sounds for the menus and the Code Editor, in every scene. It starts by itself when the game starts: nothing to set up.
//   Every Button gets a click (and a very soft hover) through ClickSound; copies of buttons keep it.
//   Other scripts call UISounds.Play(...) for block pick up / drop (and delete, which uses the drop sound), buying and fitting parts,
//   page turns, "not allowed" and mission complete.
// The sounds are in Resources/Sounds (replace a file there, same name, to change it).
public class UISounds : MonoBehaviour
{
    public enum Cue { Click, Hover, BlockPickup, BlockDrop, Error, Purchase, Equip, PageTurn, MissionComplete,
                      Transition }

    static readonly string[] Files =
    {
        "ui_click", "ui_hover", "ui_block_pickup", "ui_block_drop",
        "ui_error", "ui_purchase", "ui_equip", "ui_page_turn", "mission_complete",
        "ui_transition",
    };
    static readonly float[] Volumes = { 0.6f, 0.25f, 0.5f, 0.6f, 0.6f, 0.7f, 0.7f, 0.5f, 0.75f, 0.55f };

    public static float volume = 0.8f;          // all UI sounds
    public static bool hoverSounds = true;      // the soft tick when the mouse goes over a button

    static UISounds instance;
    AudioSource source;
    AudioClip[] clips;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (instance != null) return;
        GameObject go = new GameObject("UI Sounds");
        DontDestroyOnLoad(go);                  // one for the whole game
        go.AddComponent<UISounds>();
    }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(this); return; }   // only one (never removes the object it's on)
        instance = this;
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        clips = new AudioClip[Files.Length];
        for (int i = 0; i < Files.Length; i++) clips[i] = Resources.Load<AudioClip>("Sounds/" + Files[i]);

        SceneManager.sceneLoaded += OnSceneLoaded;
        AddToButtons();                         // the first scene is already loaded
    }

    void OnDestroy()
    {
        if (instance != this) return;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        instance = null;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode) => AddToButtons();

    static void AddToButtons()
    {
        foreach (Button b in FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (!b.GetComponent<ClickSound>()) b.gameObject.AddComponent<ClickSound>();
    }

    public static void Play(Cue cue)
    {
        if (instance == null) return;
        if (cue == Cue.Hover && !hoverSounds) return;
        AudioClip clip = instance.clips[(int)cue];
        if (clip) instance.source.PlayOneShot(clip, Volumes[(int)cue] * volume);
    }

    public static void PlayClick() => Play(Cue.Click);
}
