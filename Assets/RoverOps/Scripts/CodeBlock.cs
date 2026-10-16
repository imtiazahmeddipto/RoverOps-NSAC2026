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
    public string[] options;            // what the selector chip cycles through
    public TMP_Text compare;            // optional "<" / ">" / "=" chip
    public TMP_InputField valueInput;   // optional number slot
    public RectTransform programArea;   // the Code Editor's block list (zooms and moves)
    public RectTransform dropArea;      // the Code Editor's visible frame: drop blocks here

    static readonly string[] Signs = { "<", ">", "=" };
    RectTransform dragged;

    public string Selected => selector != null ? selector.text : "";
    public string Sign => compare != null ? compare.text : "<";
    public string ValueText => valueInput != null ? valueInput.text.Trim() : "";

    void Start()
    {
        if (selector != null) selector.GetComponentInParent<Button>().onClick.AddListener(() => Cycle(selector, options));
        if (compare != null) compare.GetComponentInParent<Button>().onClick.AddListener(() => Cycle(compare, Signs));
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
        if (Rover.IsRunning) return;
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
