using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The small ▼ button next to a block's number slot (Forward, Back, Turn Left, Turn Right...).
// Each click picks the next value: Number -> sample dist -> rock dist -> ... -> Number again.
//   Number  - the player types a number (the last number comes back)
//   reading - the slot shows the reading's name and can't be typed in
// Put it on the ▼ button and fill "Options" in the Inspector (e.g. Forward: sample dist, rock dist).
// Each option (and Number) has its own slot width and text alignment.
// The Rover already reads names like "sample dist" from the number slot, so nothing else changes.
[RequireComponent(typeof(Button))]
public class ValuePicker : MonoBehaviour
{
    [System.Serializable]
    public class Option
    {
        public string name = "sample dist";
        [Tooltip("Slot width for this option. 0 = the slot's own width")]
        public float width = 125f;
        public TextAlignmentOptions alignment = TextAlignmentOptions.Center;
    }

    public TMP_InputField valueInput;           // the block's number slot (found by itself if empty)
    public Option[] options;

    [Header("Number")]
    [Tooltip("Slot width when a number is typed. 0 = the slot's own width")]
    public float numberWidth = 0f;
    public TextAlignmentOptions numberAlignment = TextAlignmentOptions.Center;

    [SerializeField, HideInInspector] string lastNumber = "";   // remembered while a reading is shown (copies keep it)
    [SerializeField, HideInInspector] float normalWidth = -1f;  // the slot's own width, saved once (copies keep it)

    void Awake()
    {
        if (valueInput == null)
        {
            CodeBlock block = GetComponentInParent<CodeBlock>();
            if (block != null) valueInput = block.valueInput;
        }
        GetComponent<Button>().onClick.AddListener(Next);

        // the block's own number (e.g. 2 on Forward) is the one to come back to
        if (valueInput != null && string.IsNullOrEmpty(lastNumber) && Find(valueInput.text) < 0)
            lastNumber = valueInput.text.Trim();

        if (valueInput != null && normalWidth < 0f)
            normalWidth = ((RectTransform)valueInput.transform).rect.width;
    }

    void Start() => Refresh();

    // Next value in the list
    public void Next()
    {
        if (Rover.IsRunning || ProgramBuilder.IsBuilding || valueInput == null) return;
        if (options == null || options.Length == 0) return;

        int current = Find(valueInput.text);                 // -1 = a number
        if (current < 0) lastNumber = valueInput.text.Trim();

        // the next option this rover can use (rock size / sample size need the HD Cam); past the end = Number
        int next = current;
        while (true)
        {
            next++;
            if (next >= options.Length) { next = -1; break; }
            if (Usable(options[next])) break;
        }

        valueInput.text = next < 0 ? lastNumber : options[next].name.Trim();
        Refresh();
    }

    // A reading can't be typed over; a number can
    void Refresh()
    {
        if (valueInput == null) return;
        int index = Find(valueInput.text);
        valueInput.readOnly = index >= 0;

        SetWidth(WantedWidth(index));
        if (valueInput.textComponent != null)
            valueInput.textComponent.alignment = index >= 0 ? options[index].alignment : numberAlignment;
    }

    // The width for this option (-1 = a number); 0 in the Inspector = the slot's own width
    float WantedWidth(int index)
    {
        float width = index >= 0 ? options[index].width : numberWidth;
        return width > 0f ? width : normalWidth;
    }

    void SetWidth(float width)
    {
        if (width < 0f) return;
        RectTransform slot = (RectTransform)valueInput.transform;
        slot.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        LayoutElement le = slot.GetComponent<LayoutElement>();      // if a layout group sizes the slot
        if (le != null) le.preferredWidth = width;
    }

    void LateUpdate()
    {
        // the AI (or a copy) may set the slot's text: keep the slot in step with it
        if (valueInput == null) return;
        int index = Find(valueInput.text);
        float want = WantedWidth(index);
        bool widthWrong = want >= 0f && !Mathf.Approximately(((RectTransform)valueInput.transform).rect.width, want);
        if (valueInput.readOnly != (index >= 0) || widthWrong) Refresh();
    }

    static bool Usable(Option o) =>
        o != null && !string.IsNullOrWhiteSpace(o.name) && (RoverCamera.HasHDCamera || !RoverCamera.IsHDOnly(o.name));

    // The option with this name (spaces and capitals don't matter), -1 if none
    int Find(string text)
    {
        if (options == null || string.IsNullOrWhiteSpace(text)) return -1;
        string key = text.Replace(" ", "").ToLowerInvariant();
        for (int i = 0; i < options.Length; i++)
            if (options[i] != null && options[i].name != null
                && options[i].name.Replace(" ", "").ToLowerInvariant() == key) return i;
        return -1;
    }
}
