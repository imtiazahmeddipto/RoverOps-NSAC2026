using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

// The Missions screen.
//   Pages          - one child per sector, in the database's sector order. Each page holds that sector's mission buttons.
//   Mission button - nothing to set up: the page's buttons in hierarchy order are its missions (1st button = mission 1).
//                    Clicking one shows its mission, locked or not (preview only: Prepare Rover stays off when locked).
//                    A locked button is dimmed and shows its "LockIcon" child.
//   < / > buttons  - OnClick > PrevPage / NextPage (keep them outside Pages)
//   Prepare Rover  - OnClick > PrepareRover: remembers the mission and opens the garage (its PLAY starts that mission)
//   Mission video  - a RawImage in the mission panel + a VideoPlayer (on the same object or anywhere). Drag both in.
public class MissionSelectUI : MonoBehaviour
{
    public MissionDatabase database;
    public GameObject missionsPanel, garagePanel;
    public bool unlockAll;                  // testing only: every mission open

    [Header("Map card")]
    public Transform pages;                 // only page objects as children
    public TMP_Text sectorTitle;            // "Sector 1"
    public TMP_Text sectorLabel;            // "[Tutorial]" (optional)
    public TMP_Text completedText;          // "1/5 Completed"
    public Button prevButton, nextButton;   // optional: greyed out on the first / last page
    [Tooltip("Tint for locked mission buttons (they stay clickable so the mission can be previewed)")]
    public Color lockedTint = new Color(0.75f, 0.75f, 0.75f, 1f);

    [Header("Mission panel")]
    public TMP_Text missionNumber;          // "MISSION 1" (number inside its sector)
    public TMP_Text missionTitle;
    public TMP_Text missionDescription;
    public Transform starGoals;             // one row per goal: the star Image first, then the text
    public Sprite starEmpty, starEarned;    // optional
    public Button prepareButton;            // optional: greyed out for a locked mission

    [Header("Mission video")]
    public RawImage missionVideo;           // shows the video
    public VideoPlayer videoPlayer;         // plays mission.video in a loop
    [Tooltip("Optional. Leave empty and one is made at the clip's size.")]
    public RenderTexture videoTexture;

    int page;
    string selected;
    bool selectedOpen;
    bool ownTexture;                        // true when this script made the RenderTexture

    void Awake()
    {
        if (!videoPlayer && missionVideo) videoPlayer = missionVideo.GetComponent<VideoPlayer>();
        if (!videoPlayer) { Debug.LogError("[Missions] Assign a VideoPlayer to MissionSelectUI."); return; }

        videoPlayer.playOnAwake = false;
        videoPlayer.isLooping = true;
        videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
        videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        videoPlayer.prepareCompleted += p => { missionVideo.enabled = true; p.Play(); };
        missionVideo.enabled = false;
    }

    // panel shown again (e.g. back from the garage): restart the video
    void OnEnable()
    {
        if (videoPlayer && videoPlayer.clip) videoPlayer.Prepare();
    }

    void OnDestroy()
    {
        if (ownTexture && videoTexture != null) videoTexture.Release();
    }

    void ShowVideo(VideoClip clip)
    {
        if (!videoPlayer || !missionVideo) return;
        videoPlayer.Stop();
        missionVideo.enabled = false;           // hidden until the first frame is ready (no black flash)
        videoPlayer.clip = clip;
        if (clip == null) return;

        int w = (int)clip.width, h = (int)clip.height;
        if (videoTexture == null || (ownTexture && (videoTexture.width != w || videoTexture.height != h)))
        {
            if (ownTexture && videoTexture != null) videoTexture.Release();
            videoTexture = new RenderTexture(w, h, 0);
            ownTexture = true;
        }
        videoPlayer.targetTexture = videoTexture;
        missionVideo.texture = videoTexture;
        videoPlayer.Prepare();
    }

    // Back button OnClick (if this object never turns off): restart the selected mission's video
    public void RefreshVideo() => Select(MissionDatabase.SelectedScene);

    void Start()
    {
        // open on the last picked mission, or the first page
        string last = MissionDatabase.SelectedScene;
        database.Find(last, out int sector, out _);
        ShowPage(Mathf.Max(0, sector));
        if (sector >= 0) Select(last);
    }

    // ---------------- PAGES ----------------
    public void NextPage() => ShowPage(page + 1);
    public void PrevPage() => ShowPage(page - 1);

    void ShowPage(int index)
    {
        int count = Mathf.Min(pages.childCount, database.sectors.Count);
        if (count == 0) return;
        page = Mathf.Clamp(index, 0, count - 1);
        for (int i = 0; i < pages.childCount; i++) pages.GetChild(i).gameObject.SetActive(i == page);

        SectorData sector = database.sectors[page];
        sectorTitle.text = sector.name;
        if (sectorLabel) sectorLabel.text = string.IsNullOrEmpty(sector.label) ? "" : "[" + sector.label + "]";
        completedText.text = MissionDatabase.DoneCount(sector) + "/" + sector.missions.Count + " Completed";
        if (prevButton) prevButton.interactable = page > 0;
        if (nextButton) nextButton.interactable = page < count - 1;

        LockButtons(sector);
        if (sector.missions.Count > 0) Select(sector.missions[0].scene);   // the panel follows the page
    }

    // The page's buttons, in hierarchy order, are its missions: a click shows the mission (locked ones too, as a preview).
    // Locked ones are dimmed and show their lock icon; only Prepare Rover is blocked for them.
    void LockButtons(SectorData sector)
    {
        Button[] buttons = pages.GetChild(page).GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length && i < sector.missions.Count; i++)
        {
            bool open = IsOpen(page, i);
            buttons[i].interactable = true;                     // always clickable: locked missions can be previewed
            if (buttons[i].targetGraphic) buttons[i].targetGraphic.color = open ? Color.white : lockedTint;
            string scene = sector.missions[i].scene;
            buttons[i].onClick.RemoveAllListeners();            // removes only listeners added here, not Inspector ones
            buttons[i].onClick.AddListener(() => Select(scene));
            Transform lockIcon = FindChild(buttons[i].transform, "LockIcon");
            if (lockIcon) lockIcon.gameObject.SetActive(!open);
        }
    }

    // ---------------- MISSION PANEL ----------------
    // Shows a mission in the panel (the map buttons call this)
    public void Select(string scene)
    {
        MissionData mission = database.Find(scene, out int sector, out int index);
        if (mission == null) { Debug.LogWarning("[Missions] No mission with the scene '" + scene + "' in the Mission Database."); return; }
        selected = scene;
        selectedOpen = IsOpen(sector, index);
        if (prepareButton) prepareButton.interactable = selectedOpen;

        missionNumber.text = "MISSION " + (index + 1);
        missionTitle.text = mission.title;
        missionDescription.text = mission.description;
        ShowVideo(mission.video);

        for (int i = 0; i < starGoals.childCount; i++)
        {
            Transform row = starGoals.GetChild(i);
            row.gameObject.SetActive(i < mission.starGoals.Length);
            if (i >= mission.starGoals.Length) continue;
            row.GetComponentInChildren<TMP_Text>().text = mission.starGoals[i].Text;   // auto text from type/value when the text field is empty
            Image star = row.GetComponentInChildren<Image>();
            if (star && starEmpty && starEarned) star.sprite = MissionDatabase.HasStar(scene, i) ? starEarned : starEmpty;
        }
    }

    // Prepare Rover OnClick: remember the mission and open the garage
    public void PrepareRover()
    {
        if (string.IsNullOrEmpty(selected) || !selectedOpen)
            return;

        MissionDatabase.SelectedScene = selected;
        if (videoPlayer) videoPlayer.Stop();   // don't keep decoding while the garage is open

        if (garagePanel != null)
        {
            if (missionsPanel != null) missionsPanel.SetActive(false);
            garagePanel.SetActive(true);

            // Refresh the mass limit for the newly selected mission.
            GarageUI garageUI = GetComponent<GarageUI>();
            if (garageUI != null) garageUI.RefreshMissionMassLimit();
            else Debug.LogError("[Missions] GarageUI component not found on the MissionSelectUI object.");
        }
    }

    // ---------------- HELPERS ----------------
    bool IsOpen(int sector, int index) => unlockAll || database.IsUnlocked(sector, index);

    static Transform FindChild(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }
}