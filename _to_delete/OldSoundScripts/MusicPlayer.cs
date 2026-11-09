using UnityEngine;

// Background music for the whole game. It starts by itself when the game starts: nothing to set up.
// One player for every scene: the music keeps playing smoothly when scenes change (no restart).
// The track is Resources/Music/bg_music (replace that file, same name, to change it). It loops seamlessly.
public class MusicPlayer : MonoBehaviour
{
    public static float volume = 0.3f;          // background level: quiet under the sound effects
    public static float fadeInTime = 3f;        // seconds to fade in when the game starts

    static MusicPlayer instance;
    AudioSource source;
    float duck = 1f;                            // 1 = normal; lower for a moment (mission complete)
    float duckUntil;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (instance != null) return;
        GameObject go = new GameObject("Music");
        DontDestroyOnLoad(go);                  // survives scene loads: one track for the whole game
        go.AddComponent<MusicPlayer>();
    }

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(this); return; }
        instance = this;
        source = gameObject.AddComponent<AudioSource>();
        source.clip = Resources.Load<AudioClip>("Music/bg_music");
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.priority = 0;                    // never cut off by sound effects
        source.volume = 0f;
        if (source.clip != null) source.Play();
        else Debug.LogWarning("[Music] No track at Resources/Music/bg_music.");
    }

    void OnDestroy() { if (instance == this) instance = null; }

    void Update()
    {
        // duck: drop quickly, come back slowly
        float targetDuck = Time.unscaledTime < duckUntil ? 0.35f : 1f;
        duck = Mathf.MoveTowards(duck, targetDuck, Time.unscaledDeltaTime * (targetDuck < duck ? 3f : 0.5f));

        float target = volume * duck;
        source.volume = Mathf.MoveTowards(source.volume, target, Time.unscaledDeltaTime * volume / Mathf.Max(0.01f, fadeInTime));
        if (source.volume > target) source.volume = target;     // volume turned down: follow at once
    }

    // Lower the music for a few seconds so an important sound stands out (e.g. mission complete)
    public static void Duck(float seconds)
    {
        if (instance != null) instance.duckUntil = Time.unscaledTime + seconds;
    }
}
