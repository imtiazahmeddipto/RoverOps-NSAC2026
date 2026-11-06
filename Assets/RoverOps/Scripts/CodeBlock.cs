using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// One code block. Drag it from the Blocks panel into the Code Editor; drag it out to delete it.
// A block can have up to three parts the player changes:
//   selector  - a chip you click to pick a reading or variable (rock dist, sample dist, ..., A, B, C)
//   compare   - a chip you click to pick  <  >  =
//   valueInput- a slot you type in: a number, or a reading / variable name
public class CodeBlock : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public Block type;
    public RectTransform body;          // the visible block (moves right inside If / Else / Repeat)
    public TMP_Text selector;           // optional reading / variable chip
    [System.Serializable]
    public class OptionSetting
    {
        public string name;
        public bool hasComparison;
    }
    public OptionSetting[] options;
    public TMP_Text compare;            // optional "<" / ">" / "=" chip
    public TMP_InputField valueInput;   // optional number slot
    public RectTransform programArea;   // the Code Editor's block list (zooms and moves)
    public RectTransform dropArea;      // the Code Editor's visible frame: drop blocks here
    public float wideSelectorWidth = 190f;  // selector chip width when the option has no comparison (no < > = and number)

    static readonly string[] Signs = { "<", ">", "=" };
    RectTransform dragged;
    int optionIndex = -1;               // the option the selector shows, by POSITION (duplicate names can't trap the cycle)
    static readonly System.Collections.Generic.HashSet<string> warned = new System.Collections.Generic.HashSet<string>();
    [SerializeField, HideInInspector] float normalSelectorWidth = -1f;   // saved in Awake; copies of this block keep it

    void Awake()
    {
        RectTransform chip = SelectorChip;
        if (chip != null && normalSelectorWidth < 0f) normalSelectorWidth = chip.rect.width;
    }

    // The selector's chip (its button), or the text itself if it has no button
    RectTransform SelectorChip
    {
        get
        {
            if (selector == null) return null;
            Button b = selector.GetComponentInParent<Button>();
            return b != null ? (RectTransform)b.transform : selector.rectTransform;
        }
    }

    void SetSelectorWidth(float width)
    {
        RectTransform chip = SelectorChip;
        if (chip == null || width < 0f) return;
        chip.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        LayoutElement le = chip.GetComponent<LayoutElement>();      // if a layout group sizes the chip
        if (le != null) le.preferredWidth = width;
    }

    public string Selected => selector != null ? selector.text : "";
    public string Sign => compare != null ? compare.text : "<";
    public string ValueText => valueInput != null ? valueInput.text.Trim() : "";

    void Start()
    {
        if (selector != null)
        {
            Button selectorButton = selector.GetComponentInParent<Button>();

            if (selectorButton != null)
                selectorButton.onClick.AddListener(CycleOption);

            optionIndex = FindOption(selector.text);
            CheckOptions();
            UpdateOptionControls();
        }

        if (compare != null)
        {
            Button compareButton = compare.GetComponentInParent<Button>();

            if (compareButton != null)
                compareButton.onClick.AddListener(() => Cycle(compare, Signs));
        }
    }
    void CycleOption()
    {
        if (Rover.IsRunning || options == null || options.Length == 0)
            return;

        int index = optionIndex;
        for (int tries = 0; tries < options.Length; tries++)            // the next option this rover can use
        {
            index = (index + 1) % options.Length;
            if (!Available(options[index])) continue;
            optionIndex = index;
            selector.text = options[index].name.Trim();
            break;
        }

        UpdateOptionControls();
    }

    // ---------------- SET BY THE AI (ProgramBuilder) ----------------
    // Sets the chip, sign and number slot. Empty = keep the block's default.
    public void Setup(string option, string sign, string value)
    {
        if (selector != null && !string.IsNullOrWhiteSpace(option))
        {
            optionIndex = FindOption(option);
            selector.text = optionIndex >= 0 ? options[optionIndex].name.Trim() : option.Trim();
        }

        if (compare != null && !string.IsNullOrWhiteSpace(sign))
            compare.text = sign.Trim();

        if (valueInput != null && !string.IsNullOrWhiteSpace(value))
            valueInput.text = value.Trim();

        UpdateOptionControls();
    }

    public bool HasOption(string option) => FindOption(option) >= 0;

    // Skipped: options with no name, and "rock size" / "sample size" unless the HD Cam is fitted
    static bool Available(OptionSetting o) =>
        o != null && !string.IsNullOrWhiteSpace(o.name)
        && (RoverCamera.HasHDCamera || !RoverCamera.IsHDOnly(o.name));

    // The option with this name (spaces and capitals don't matter), -1 if none
    int FindOption(string text)
    {
        if (options == null || text == null) return -1;
        return System.Array.FindIndex(options, o => o != null && o.name != null
            && string.Equals(o.name.Trim(), text.Trim(), System.StringComparison.OrdinalIgnoreCase));
    }

    // Tell the designer once about option lists that can't work well (empty or repeated names)
    void CheckOptions()
    {
        if (options == null) return;
        var names = new System.Collections.Generic.HashSet<string>();
        foreach (OptionSetting o in options)
        {
            string problem = null;
            if (o == null || string.IsNullOrWhiteSpace(o.name)) problem = "has an option with no name (it is skipped)";
            else if (!names.Add(o.name.Trim().ToLowerInvariant())) problem = "lists '" + o.name.Trim() + "' twice";
            if (problem == null) continue;
            string text = "[CodeBlock] The " + type + " block " + problem + ". Fix its Options in the Inspector.";
            if (warned.Add(text)) Debug.LogWarning(text, this);
        }
    }

    void UpdateOptionControls()
    {
        if (options == null || selector == null)
            return;

        if (optionIndex < 0 || optionIndex >= options.Length) optionIndex = FindOption(selector.text);

        bool show = optionIndex >= 0 && options[optionIndex] != null && options[optionIndex].hasComparison;

        if (compare != null)
            compare.transform.parent.gameObject.SetActive(show);

        if (valueInput != null)
            valueInput.gameObject.SetActive(show);

        SetSelectorWidth(show ? normalSelectorWidth : wideSelectorWidth);   // no comparison = wider chip
    }

    static void Cycle(TMP_Text chip, string[] list)
    {
        if (Rover.IsRunning || list == null || list.Length == 0) return;
        int index = System.Array.IndexOf(list, chip.text);
        chip.text = list[(index + 1) % list.Length];
    }

    // ---------------- DRAG & DROP ----------------
    public void OnBeginDrag(PointerEventData e)
    {
        if (Rover.IsRunning || ProgramBuilder.IsBuilding) return;
        if (type == Block.Start) return;                // "When mission starts" is fixed at the top
        Transform canvas = GetComponentInParent<Canvas>().rootCanvas.transform;

        // from the Code Editor: move this block. From the Blocks panel: drag a new copy.
        bool inProgram = transform.parent == programArea;
        dragged = inProgram ? (RectTransform)transform : (RectTransform)Instantiate(gameObject, canvas).transform;
        dragged.SetParent(canvas, false);
        dragged.localScale = Vector3.one * programArea.localScale.x;          // same size as the zoomed editor
        dragged.GetComponent<CodeBlock>().body.anchoredPosition = Vector2.zero;
        dragged.position = e.position;
        UpdateIndents(programArea);
    }

    public void OnDrag(PointerEventData e)
    {
        if (dragged) dragged.position = e.position;
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (dragged == null) return;
        RectTransform area = dropArea != null ? dropArea : programArea;
        if (RectTransformUtility.RectangleContainsScreenPoint(area, e.position))
        {
            // drop between the blocks, at the pointer's height
            int index = programArea.childCount;
            for (int i = 0; i < programArea.childCount; i++)
                if (e.position.y > programArea.GetChild(i).position.y) { index = i; break; }
            if (programArea.childCount > 0 && programArea.GetChild(0).GetComponent<CodeBlock>().type == Block.Start)
                index = Mathf.Max(index, 1);            // never above "When mission starts"
            dragged.SetParent(programArea, false);
            dragged.localScale = Vector3.one;
            dragged.SetSiblingIndex(index);
        }
        else Destroy(dragged.gameObject);    // dropped outside the editor = delete
        dragged = null;
        UpdateIndents(programArea);
    }

    // ---------------- C-SHAPE INDENTS ----------------
    public static bool Opens(Block t) => t == Block.If || t == Block.Else || t == Block.RepeatTimes || t == Block.RepeatWhile || t == Block.RepeatForever;
    public static bool Closes(Block t) => t == Block.Else || t == Block.EndIf || t == Block.EndRepeat;

    // Blocks inside If / Else / Repeat move right
    public static void UpdateIndents(Transform area)
    {
        int depth = 0;
        foreach (Transform child in area)
        {
            CodeBlock block = child.GetComponent<CodeBlock>();
            if (block.type == Block.Start) depth = 0;
            if (Closes(block.type)) depth = Mathf.Max(0, depth - 1);
            block.body.anchoredPosition = new Vector2(depth * block.body.rect.width * 60f / 520f, 0f);   // 60 px indent in the 520 px block art
            if (Opens(block.type)) depth++;
        }
    }
}
