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

    [Header("Space Points UI")]
    [Tooltip("Optional. If empty, GarageUI searches for a TMP object named SpacePointsTXT.")]
    public TMP_Text spacePointsText;
    [Tooltip("Optional. If empty, GarageUI searches for ErrorTXT or ErrorText.")]
    public TMP_Text purchaseErrorText;
    public float errorMessageSeconds = 2.5f;

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
    private Coroutine errorRoutine;

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

        if (spacePointsText == null)
            spacePointsText = FindOptional(transform, "SpacePointsTXT")?.GetComponent<TMP_Text>();

        if (purchaseErrorText == null)
        {
            Transform error = FindOptional(transform, "ErrorTXT");
            if (error == null) error = FindOptional(transform, "ErrorText");
            if (error != null) purchaseErrorText = error.GetComponent<TMP_Text>();
        }

        if (purchaseErrorText != null)
            purchaseErrorText.gameObject.SetActive(false);

        if (database != null)
            database.InitializeSpacePoints();

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
        if (part == null || database == null || !database.IsUnlocked(part))
            return;

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

        // Update currency and mass information.
        UpdateSpacePointsUI();
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

        bool unlocked = database.IsUnlocked(part);

        Button cardButton = card.GetComponent<Button>();
        if (cardButton != null)
        {
            cardButton.interactable = unlocked;
            if (unlocked)
                cardButton.onClick.AddListener(() => Equip(part));
        }

        // Lock overlay is a child of PartsListItem. Locked parts cannot be equipped.
        Transform lockPanel = FindOptional(card.transform, "Lock");
        if (lockPanel != null)
        {
            lockPanel.gameObject.SetActive(!unlocked);

            if (!unlocked)
            {
                Transform costTransform = FindOptional(lockPanel, "CostTXT");
                if (costTransform != null)
                {
                    TMP_Text costText = costTransform.GetComponent<TMP_Text>();
                    if (costText != null)
                        costText.text = part.spacePointCost.ToString("N0");
                }

                Transform buyTransform = FindOptional(lockPanel, "Buy Button");
                if (buyTransform == null) buyTransform = FindOptional(lockPanel, "BuyButton");

                if (buyTransform != null)
                {
                    Button buyButton = buyTransform.GetComponent<Button>();
                    if (buyButton != null)
                    {
                        buyButton.interactable = true;
                        buyButton.onClick.RemoveAllListeners();
                        buyButton.onClick.AddListener(() => BuyPart(part));
                    }
                    else
                    {
                        Debug.LogWarning("[Garage] Buy button object has no Button component.", buyTransform);
                    }
                }
                else
                {
                    Debug.LogWarning("[Garage] Could not find 'Buy Button' under Lock.", lockPanel);
                }
            }
        }
        else if (!unlocked)
        {
            Debug.LogWarning("[Garage] Locked part '" + part.name + "' has no Lock panel in PartsListItem.");
        }

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
            // Keep graphics inside Lock raycastable so the Buy Button can receive clicks.
            Transform lockRoot = FindOptional(card.transform, "Lock");
            bool insideLock = lockRoot != null &&
                              (graphic.transform == lockRoot || graphic.transform.IsChildOf(lockRoot));

            if (graphic.gameObject != card && !insideLock)
                graphic.raycastTarget = false;
        }

        return card;
    }

    private void BuyPart(PartData part)
    {
        if (database == null || part == null)
            return;

        if (database.IsUnlocked(part))
        {
            Refresh();
            return;
        }

        if (database.SpacePoints < part.spacePointCost)
        {
            ShowPurchaseError("Not enough Space Points!");
            return;
        }

        if (!database.TryBuy(part))
        {
            ShowPurchaseError("Could not buy this part.");
            return;
        }

        ClearPurchaseError();
        UpdateSpacePointsUI();
        Refresh();
    }

    private void UpdateSpacePointsUI()
    {
        if (spacePointsText != null && database != null)
            spacePointsText.text = database.SpacePoints.ToString("N0");
    }

    private void ShowPurchaseError(string message)
    {
        if (purchaseErrorText == null)
        {
            Debug.LogWarning("[Garage] " + message);
            return;
        }

        if (errorRoutine != null)
            StopCoroutine(errorRoutine);

        purchaseErrorText.text = message;
        purchaseErrorText.gameObject.SetActive(true);
        errorRoutine = StartCoroutine(HidePurchaseError());
    }

    private System.Collections.IEnumerator HidePurchaseError()
    {
        yield return new WaitForSeconds(Mathf.Max(0.1f, errorMessageSeconds));

        if (purchaseErrorText != null)
            purchaseErrorText.gameObject.SetActive(false);

        errorRoutine = null;
    }

    private void ClearPurchaseError()
    {
        if (errorRoutine != null)
        {
            StopCoroutine(errorRoutine);
            errorRoutine = null;
        }

        if (purchaseErrorText != null)
            purchaseErrorText.gameObject.SetActive(false);
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

    private static Transform FindOptional(Transform root, string objectName)
    {
        if (root == null) return null;

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == objectName)
                return child;
        }

        return null;
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