using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GarageUI : MonoBehaviour
{
    [Header("Database")]
    public PartDatabase database;
    public MissionDatabase missionDatabase;
    public RoverLoadout garageRover;

    [Header("UI References")]
    public Transform tabs;
    public GameObject cardTemplate;
    public TMP_Text massText;
    public Slider massBar;
    public Image massStatusImage;
    public TMP_Text massStatusText;
    public Button playButton;

    [Header("Rover Display")]
    public float turntableSpeed = 12f;

    [Header("UI Colors")]
    public Color equippedCardColor = new Color(1f, 0.87f, 0.55f);
    public Color selectedTabColor = new Color(1f, 0.62f, 0.3f);
    public Color validMassColor = new Color(0.25f, 0.8f, 0.35f);
    public Color overweightMassColor = Color.red;

    [Header("Scene Transition")]
    public Transition transition;

    private Color cardColor;
    private PartType current = PartType.Wheels;

    private Image massFill;
    private float missionMassLimit;
    private bool hasValidMission;

    private readonly List<GameObject> cards = new List<GameObject>();
    private readonly Dictionary<PartType, Color> originalTabColors =
        new Dictionary<PartType, Color>();

    private void Start()
    {
        InitializeUI();
        LoadSelectedMission();
        Show(PartType.Wheels);
    }

    private void InitializeUI()
    {
        // Find UI objects if they were not assigned in the Inspector.
        if (tabs == null)
            tabs = Find(transform, "Modules");

        if (cardTemplate == null)
            cardTemplate = Find(transform, "PartsListItem").gameObject;

        Transform totalMass = Find(transform, "Total Mass");

        if (massText == null)
            massText = Find(totalMass, "Mass Txt").GetComponent<TMP_Text>();

        if (massBar == null)
            massBar = totalMass.GetComponentInChildren<Slider>();

        if (massBar != null)
        {
            ReadOnly(massBar);

            if (massBar.fillRect != null)
                massFill = massBar.fillRect.GetComponent<Image>();
        }

        cardColor = cardTemplate.GetComponent<Image>().color;
        cardTemplate.SetActive(false);

        // Save the original color of each tab.
        foreach (PartType type in Enum.GetValues(typeof(PartType)))
        {
            Transform tab = tabs.Find(type.ToString());

            if (tab == null)
                continue;

            Image tabImage = tab.GetComponent<Image>();

            if (tabImage != null)
                originalTabColors[type] = tabImage.color;

            Button tabButton = tab.GetComponent<Button>();

            if (tabButton != null)
            {
                PartType selectedType = type;
                tabButton.onClick.AddListener(() => Show(selectedType));
            }
        }

        if (database == null)
            Debug.LogError("[Garage] Part Database is not assigned.", this);

        if (missionDatabase == null)
            Debug.LogError("[Garage] Mission Database is not assigned.", this);

        if (playButton == null)
            Debug.LogError("[Garage] PLAY button is not assigned.", this);
    }

    // Loads the mass limit belonging to the selected mission.
    private void LoadSelectedMission()
    {
        hasValidMission = false;
        missionMassLimit = 0f;

        string scene = MissionDatabase.SelectedScene;

        if (missionDatabase == null || string.IsNullOrEmpty(scene))
        {
            Debug.LogWarning("[Garage] No mission selected or Mission Database is missing.");
            UpdateMassUI();
            return;
        }

        MissionData mission = missionDatabase.Find(scene, out _, out _);

        if (mission == null)
        {
            Debug.LogError(
                "[Garage] Mission '" + scene + "' was not found in the Mission Database.");

            UpdateMassUI();
            return;
        }

        if (mission.maxRoverMass <= 0f)
        {
            Debug.LogError(
                "[Garage] Mission '" + mission.title + "' has an invalid mass limit.");

            UpdateMassUI();
            return;
        }

        missionMassLimit = mission.maxRoverMass;
        hasValidMission = true;
    }

    // Called by the PLAY button.
    public void Play()
    {
        if (database == null)
        {
            Debug.LogError("[Garage] Part Database is missing.");
            return;
        }

        string scene = MissionDatabase.SelectedScene;

        if (string.IsNullOrEmpty(scene))
        {
            Debug.LogWarning("[Garage] No mission selected.");
            return;
        }

        // Recheck the selected mission and current mass before loading.
        LoadSelectedMission();

        float currentMass = database.TotalMass();

        if (!hasValidMission)
        {
            Debug.LogWarning("[Garage] Cannot start: mission mass limit is unavailable.");
            UpdateMassStatus(true);
            return;
        }

        if (currentMass > missionMassLimit)
        {
            Debug.LogWarning(
                "[Garage] Cannot start mission. Rover mass: " +
                currentMass.ToString("0.#") + " kg, maximum allowed: " +
                missionMassLimit.ToString("0.#") + " kg.");

            UpdateMassUI();
            return;
        }

        if (transition != null)
            transition.LoadScene(scene);
        else
            SceneManager.LoadScene(scene);
    }

    private void Update()
    {
        if (garageRover != null && turntableSpeed != 0f)
        {
            garageRover.transform.Rotate(
                0f,
                turntableSpeed * Time.deltaTime,
                0f);
        }
    }

    public void Show(PartType type)
    {
        current = type;
        Refresh();
    }

    private void Equip(PartData part)
    {
        if (Rover.IsRunning)
        {
            Debug.LogWarning(
                "[Garage] Stop the program before changing rover parts.");

            return;
        }

        if (garageRover != null)
            garageRover.Equip(part);
        else if (database != null)
            database.Equip(part);
        else
        {
            Debug.LogError("[Garage] Part Database is missing.");
            return;
        }

        Refresh();
    }

    private void Refresh()
    {
        if (database == null)
            return;

        // Update tab colors.
        foreach (PartType type in Enum.GetValues(typeof(PartType)))
        {
            Transform tab = tabs.Find(type.ToString());

            if (tab == null)
                continue;

            Image tabImage = tab.GetComponent<Image>();

            if (tabImage == null)
                continue;

            if (type == current)
            {
                tabImage.color = selectedTabColor;
            }
            else if (originalTabColors.TryGetValue(type, out Color originalColor))
            {
                tabImage.color = originalColor;
            }
        }

        // Remove old cards.
        foreach (GameObject card in cards)
        {
            if (card != null)
                Destroy(card);
        }

        cards.Clear();

        // Create cards for the selected part category.
        foreach (PartData part in database.OfType(current))
        {
            cards.Add(MakeCard(part));
        }

        // Update mass information and PLAY button.
        UpdateMassUI();
    }

    private void UpdateMassUI()
    {
        if (database == null)
        {
            if (playButton != null)
                playButton.interactable = false;

            return;
        }

        float currentMass = database.TotalMass();

        if (massText == null || massBar == null)
            return;

        if (!hasValidMission)
        {
            massText.text = currentMass.ToString("0.#") + " / -- kg";
            massBar.value = 0f;

            if (massFill != null)
                massFill.color = overweightMassColor;

            if (playButton != null)
                playButton.interactable = false;

            return;
        }

        bool overweight = currentMass > missionMassLimit;
        UpdateMassStatus(overweight);

        // Display current mass and the selected mission's limit.
        massText.text =
            currentMass.ToString("0.#") + " / " +
            missionMassLimit.ToString("0.#") + " kg";

        // Fill the slider according to the mission's mass limit.
        massBar.minValue = 0f;
        massBar.maxValue = 1f;
        massBar.value = Mathf.Clamp01(currentMass / missionMassLimit);

        // Change the fill color.
        if (massFill != null)
        {
            massFill.color = overweight
                ? overweightMassColor
                : validMassColor;
        }

        // Prevent starting an overweight rover.
        if (playButton != null)
            playButton.interactable = !overweight;
    }

    private GameObject MakeCard(PartData part)
    {
        GameObject card = Instantiate(
            cardTemplate,
            cardTemplate.transform.parent);

        card.SetActive(true);
        card.name = part.name;

        Image cardImage = card.GetComponent<Image>();

        if (cardImage != null)
        {
            cardImage.color = database.IsEquipped(part)
                ? equippedCardColor
                : cardColor;
        }

        Button cardButton = card.GetComponent<Button>();

        if (cardButton != null)
            cardButton.onClick.AddListener(() => Equip(part));

        Find(card.transform, "Parts Name")
            .GetComponent<TMP_Text>().text = part.name;

        Find(card.transform, "Weight TXT")
            .GetComponent<TMP_Text>().text = part.mass.ToString("0.#") + " KG";

        Image icon = Find(card.transform, "Icon").GetComponent<Image>();

        icon.sprite = part.icon;
        icon.enabled = part.icon != null;

        // Display the part's stats.
        string[] names = PartDatabase.StatNames(part.type);
        string[] units = PartDatabase.StatUnits(part.type);

        Transform rows = Find(card.transform, "Stats");

        for (int i = 0; i < rows.childCount; i++)
        {
            Transform row = rows.GetChild(i);

            row.gameObject.SetActive(i < names.Length);

            if (i >= names.Length)
                continue;

            TMP_Text rowText = row.GetComponentInChildren<TMP_Text>();

            if (rowText != null)
            {
                rowText.text =
                    names[i] + "  " +
                    part.Stat(i).ToString("0.##") + " " +
                    units[i];
            }

            Slider bar = row.GetComponent<Slider>();

            if (bar != null)
            {
                ReadOnly(bar);

                float bestStat = database.BestStat(part.type, i);

                bar.value = bestStat > 0f
                    ? Mathf.Clamp01(part.Stat(i) / bestStat)
                    : 0f;
            }
        }

        // Prevent child graphics from blocking clicks on the card.
        foreach (Graphic graphic in card.GetComponentsInChildren<Graphic>(true))
        {
            if (graphic.gameObject != card)
                graphic.raycastTarget = false;
        }

        return card;
    }

    private static void ReadOnly(Slider bar)
    {
        if (bar == null)
            return;

        bar.transition = Selectable.Transition.None;
        bar.interactable = false;
        bar.minValue = 0f;
        bar.maxValue = 1f;
    }
    private void UpdateMassStatus(bool overweight)
    {
        if (massStatusImage == null)
            return;

        massStatusImage.color = overweight ? overweightMassColor : validMassColor;

        massStatusText.text = overweight ? "OVER LIMIT" : "OK";

    }

    public void RefreshMissionMassLimit()
    {
        LoadSelectedMission();
        UpdateMassUI();
    }

    private static Transform Find(Transform root, string objectName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == objectName)
                return child;
        }

        throw new Exception(
            "GarageUI: No object named '" + objectName +
            "' found under " + root.name);
    }
}