using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The Garage screen. Put it on the root of Garage.prefab and assign the database and the garage rover.
// It finds its UI by the names in Garage.prefab:
//   Modules/Wheels, Camera, Battery, Solar, Science   - the type tabs
//   PartsListItem                                     - the card template (Parts Name, Icon, Weight TXT, Stats rows)
//   Total Mass (Mass Txt + its Slider)                - the rover's total mass
// Click a tab to see that type's parts, click a card to put that part on the rover.
public class GarageUI : MonoBehaviour
{
    public PartDatabase database;
    public RoverLoadout garageRover;                    // the display rover in this scene
    public Camera viewCamera;                           // optional: a camera that films the rover only while the garage is open
    public string missionScene = "";       // PLAY loads this scene (it must be in File > Build Settings)
    public float turntableSpeed = 12f;                  // the display rover slowly turns (degrees per second, 0 = off)
    public Color equippedCardColor = new Color(1f, 0.87f, 0.55f);
    public Color selectedTabColor = new Color(1f, 0.62f, 0.3f);

    Transform tabs;
    GameObject cardTemplate;
    TMP_Text massText;
    Slider massBar;
    Color cardColor, tabColor;
    PartType current = PartType.Wheels;
    readonly List<GameObject> cards = new List<GameObject>();

    void Start()
    {
        tabs = Find(transform, "Modules");
        cardTemplate = Find(transform, "PartsListItem").gameObject;
        Transform totalMass = Find(transform, "Total Mass");
        massText = Find(totalMass, "Mass Txt").GetComponent<TMP_Text>();
        massBar = totalMass.GetComponentInChildren<Slider>();
        ReadOnly(massBar);

        cardColor = cardTemplate.GetComponent<Image>().color;
        cardTemplate.SetActive(false);

        foreach (PartType type in Enum.GetValues(typeof(PartType)))
        {
            Transform tab = tabs.Find(type.ToString());
            if (tab == null) continue;
            tabColor = tab.GetComponent<Image>().color;
            PartType t = type;
            tab.GetComponent<Button>().onClick.AddListener(() => Show(t));
        }
        Show(PartType.Wheels);
    }

    public void Play()
    {
        string scene = MissionDatabase.SelectedScene;
        SceneManager.LoadScene(string.IsNullOrEmpty(scene) ? missionScene : scene);
    }
    void Update()
    {
        if (garageRover != null && turntableSpeed != 0f) garageRover.transform.Rotate(0f, turntableSpeed * Time.deltaTime, 0f);
    }

    void OnEnable() { if (viewCamera) viewCamera.enabled = true; }
    void OnDisable() { if (viewCamera) viewCamera.enabled = false; }

    public void Show(PartType type)
    {
        current = type;
        Refresh();
    }

    void Equip(PartData part)
    {
        if (Rover.IsRunning) { Debug.LogWarning("[Garage] Stop the program before changing parts."); return; }
        if (garageRover != null) garageRover.Equip(part);
        else database.Equip(part);
        Refresh();
    }

    void Refresh()
    {
        // tabs
        foreach (PartType type in Enum.GetValues(typeof(PartType)))
        {
            Transform tab = tabs.Find(type.ToString());
            if (tab != null) tab.GetComponent<Image>().color = type == current ? selectedTabColor : tabColor;
        }

        // cards
        foreach (GameObject card in cards) Destroy(card);
        cards.Clear();
        foreach (PartData part in database.OfType(current)) cards.Add(MakeCard(part));

        // total mass
        float mass = database.TotalMass();
        massText.text = mass.ToString("0") + " kg";
        massBar.value = mass / database.MaxMass();
    }

    GameObject MakeCard(PartData part)
    {
        GameObject card = Instantiate(cardTemplate, cardTemplate.transform.parent);
        card.SetActive(true);
        card.name = part.name;
        card.GetComponent<Image>().color = database.IsEquipped(part) ? equippedCardColor : cardColor;
        card.GetComponent<Button>().onClick.AddListener(() => Equip(part));

        Find(card.transform, "Parts Name").GetComponent<TMP_Text>().text = part.name;
        Find(card.transform, "Weight TXT").GetComponent<TMP_Text>().text = part.mass.ToString("0") + " KG";
        Image icon = Find(card.transform, "Icon").GetComponent<Image>();
        icon.sprite = part.icon;
        icon.enabled = part.icon != null;

        // one row per stat: "Speed  1.5 m/s" + a bar (full bar = best part of this type)
        string[] names = PartDatabase.StatNames(part.type), units = PartDatabase.StatUnits(part.type);
        Transform rows = Find(card.transform, "Stats");
        for (int i = 0; i < rows.childCount; i++)
        {
            Transform row = rows.GetChild(i);
            row.gameObject.SetActive(i < names.Length);
            if (i >= names.Length) continue;
            row.GetComponentInChildren<TMP_Text>().text = names[i] + "  " + part.Stat(i).ToString("0.##") + " " + units[i];
            Slider bar = row.GetComponent<Slider>();
            ReadOnly(bar);
            bar.value = part.Stat(i) / database.BestStat(part.type, i);
        }

        // only the card itself takes clicks (not the bars or texts)
        foreach (Graphic g in card.GetComponentsInChildren<Graphic>(true))
            if (g.gameObject != card) g.raycastTarget = false;
        return card;
    }

    static void ReadOnly(Slider bar)                    // a bar that shows a value, not a control
    {
        if (bar == null) return;
        bar.transition = Selectable.Transition.None;
        bar.interactable = false;
        bar.minValue = 0f;
        bar.maxValue = 1f;
    }

    static Transform Find(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        throw new Exception("GarageUI: no object named '" + name + "' under " + root.name);
    }
}
