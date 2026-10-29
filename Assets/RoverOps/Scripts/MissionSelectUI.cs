using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The Missions screen.
//   Pages          - one child per sector, in the database's sector order. Each page holds that sector's mission buttons.
//   Mission button - nothing to set up: the page's buttons in hierarchy order are its missions (1st button = mission 1).
//                    Clicking one shows its mission. A locked button is greyed out and shows its "LockIcon" child.
//                    The page's buttons in hierarchy order are its missions (1st button = mission 1).
//                    A locked button is greyed out and shows its "LockIcon" child.
//   < / > buttons  - OnClick > PrevPage / NextPage (keep them outside Pages)
//   Prepare Rover  - OnClick > PrepareRover: remembers the mission and opens the garage (its PLAY starts that mission)
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

    [Header("Mission panel")]
    public TMP_Text missionNumber;          // "MISSION 1" (number inside its sector)
    public TMP_Text missionTitle;
    public TMP_Text missionDescription;
    public Image missionImage;
    public Transform starGoals;             // one row per goal: the star Image first, then the text
    public Sprite starEmpty, starEarned;    // optional
    public Button prepareButton;            // optional: greyed out for a locked mission

    int page;
    string selected;
    bool selectedOpen;

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

    // The page's buttons, in hierarchy order, are its missions: a click shows the mission, locked ones are greyed out
    void LockButtons(SectorData sector)
    {
        Button[] buttons = pages.GetChild(page).GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length && i < sector.missions.Count; i++)
        {
            bool open = IsOpen(page, i);
            buttons[i].interactable = open;
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
        missionImage.sprite = mission.image;
        missionImage.enabled = mission.image != null;

        for (int i = 0; i < starGoals.childCount; i++)
        {
            Transform row = starGoals.GetChild(i);
            row.gameObject.SetActive(i < mission.starGoals.Length);
            if (i >= mission.starGoals.Length) continue;
            row.GetComponentInChildren<TMP_Text>().text = mission.starGoals[i].text;
            Image star = row.GetComponentInChildren<Image>();
            if (star && starEmpty && starEarned) star.sprite = MissionDatabase.HasStar(scene, i) ? starEarned : starEmpty;
        }
    }

    // Prepare Rover OnClick: remember the mission and open the garage
public void PrepareRover()
{
    if (string.IsNullOrEmpty(selected) || !selectedOpen)
        return;

    // Save the newly selected mission.
    MissionDatabase.SelectedScene = selected;

    // Open the garage panel.
    if (garagePanel != null)
    {
        if (missionsPanel != null)
            missionsPanel.SetActive(false);

        garagePanel.SetActive(true);

        // Refresh the mass limit for the newly selected mission.
        GarageUI garageUI = GetComponent<GarageUI>();

        if (garageUI != null)
        {
            garageUI.RefreshMissionMassLimit();
        }
        else
        {
            Debug.LogError("[Missions] GarageUI component not found in Garage Panel.");
        }
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