using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The AI button: builds this mission's solution in the Code Editor, block by block.
// Put ONE on each mission scene (e.g. on the Code Editor) and fill "Solution" in the Inspector.
// "When mission starts" is added by itself, don't list it. The AI button's OnClick -> ProgramBuilder.BuildSolution.
//
// Each step copies the matching block from the Blocks panel (like dragging it in) and sets:
//   option - the If / Repeat While chip, e.g. "sample detected", "rock dist"   (empty = the block's default)
//   sign   - "<", ">" or "="                                                   (empty = the block's default)
//   value  - the number slot: a number or a reading, e.g. "2", "90", "sample angle"   (empty = the block's default)
public class ProgramBuilder : MonoBehaviour
{
    [System.Serializable]
    public class SolutionStep
    {
        public Block type = Block.MoveForward;
        [Tooltip("If / Repeat While chip, e.g. sample detected, rock dist. Empty = the block's default")]
        public string option;
        [Tooltip("<  >  or  =. Empty = the block's default")]
        public string sign;
        [Tooltip("The number slot: a number or a reading, e.g. 2, 90, sample angle. Empty = the block's default")]
        public string value;
    }

    [Header("Links")]
    public Transform blocksPanel;           // the Blocks list (the blocks to copy)
    public RectTransform programArea;       // the Code Editor's block list
    public Rover rover;                     // optional: shows the AI's messages in the Rover Status
    public CodeEditorView editorView;       // optional (found by itself): scrolls so each new block is in view

    [Header("Solution (this mission)")]
    public List<SolutionStep> solution = new List<SolutionStep>();

    [Header("Equipment Needed (this mission)")]
    [Tooltip("Parts the rover must have for this solution. Missing ones show as a warning in the Rover Status.")]
    [UnityEngine.Serialization.FormerlySerializedAs("toolsNeeded")]
    [PartName] public List<string> equipmentNeeded = new List<string>();
    public PartDatabase partDatabase;       // optional: found from the rover's RoverLoadout if empty

    [Header("Look")]
    [Min(0f)] public float stepDelay = 0.15f;   // seconds between blocks (0 = all at once)

    // True while the AI is placing blocks: RUN, CLEAR and dragging wait for it
    public static bool IsBuilding { get; private set; }

    static readonly string[] Signs = { "<", ">", "=" };

    // ---------------- AI BUTTON ----------------
    public void BuildSolution()
    {
        if (Rover.IsRunning || IsBuilding) return;

        if (blocksPanel == null || programArea == null)
        {
            Debug.LogError("[AI] Assign Blocks Panel and Program Area on the ProgramBuilder.", this);
            return;
        }

        if (solution == null || solution.Count == 0)
        {
            Tell("<color=#FFC63A>No AI solution for this mission yet</color>");
            return;
        }

        StartCoroutine(Build());
    }

    IEnumerator Build()
    {
        IsBuilding = true;
        try
        {
            if (editorView == null) editorView = programArea.GetComponentInParent<CodeEditorView>();
            Dictionary<Block, CodeBlock> templates = Templates();
            int warnings = Check(templates);

            ClearProgram();
            if (!HasStartBlock())
            {
                if (templates.TryGetValue(Block.Start, out CodeBlock start)) Place(start);
                else { Debug.LogWarning("[AI] No 'When mission starts' block in the Blocks panel.", this); warnings++; }
            }
            Refresh(null);
            if (editorView != null) editorView.ScrollToTop();      // keeps the player's zoom

            int placed = 0;
            for (int i = 0; i < solution.Count; i++)
            {
                SolutionStep step = solution[i];
                if (step == null || step.type == Block.Start) continue;
                if (!templates.TryGetValue(step.type, out CodeBlock template)) continue;   // warned in Check

                if (!TryPlace(template, step, i)) { warnings++; continue; }   // one bad step doesn't stop the rest
                placed++;

                Tell("AI is building the program... " + placed + "/" + solution.Count);
                if (stepDelay > 0f) yield return new WaitForSecondsRealtime(stepDelay);   // same at any run speed
            }

            Refresh(null);
            if (editorView != null) editorView.ScrollToTop();      // done: back to the first block
            string done = warnings == 0
                ? "<color=#7CFC7C>AI built the program (" + placed + " blocks). Press RUN.</color>"
                : "<color=#FFC63A>AI built the program with " + warnings + (warnings == 1 ? " problem" : " problems")
                  + " (see the Console). Press RUN.</color>";
            string missing = MissingEquipment();
            if (missing != null)
                done += "\n<color=#FFC63A>" + missing + " needed to complete this mission.</color>";
            Tell(done);
        }
        finally
        {
            IsBuilding = false;
        }
    }

    void OnDisable() => IsBuilding = false;     // a stopped coroutine skips its finally

    // Puts every block in its place now (not at the end of the frame), then scrolls so 'newest' is in view
    void Refresh(CodeBlock newest)
    {
        CodeBlock.UpdateIndents(programArea);
        LayoutRebuilder.ForceRebuildLayoutImmediate(programArea);
        if (editorView == null) return;
        if (newest != null) editorView.Reveal(newest.body);
        else editorView.Refresh();
    }

    // ---------------- BLOCKS ----------------
    // One block to copy per type: the first one in the Blocks panel
    Dictionary<Block, CodeBlock> Templates()
    {
        var templates = new Dictionary<Block, CodeBlock>();
        foreach (CodeBlock block in blocksPanel.GetComponentsInChildren<CodeBlock>(true))
            if (!block.transform.IsChildOf(programArea) && !templates.ContainsKey(block.type))
                templates.Add(block.type, block);
        return templates;
    }

    // Places one step; false (and the error in the Console) if anything went wrong
    bool TryPlace(CodeBlock template, SolutionStep step, int index)
    {
        try
        {
            CodeBlock block = Place(template);
            block.Setup(step.option, step.sign, step.value);
            Refresh(block);
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError("[AI] Step " + (index + 1) + " (" + step.type + ") could not be placed: " + e.Message, this);
            Debug.LogException(e, this);
            return false;
        }
    }

    // A copy of a Blocks-panel block at the end of the program (what a drag and drop does)
    CodeBlock Place(CodeBlock template)
    {
        CodeBlock block = Instantiate(template, programArea, false);
        block.name = template.name;
        block.gameObject.SetActive(true);
        block.transform.localScale = Vector3.one;
        block.body.anchoredPosition = Vector2.zero;
        block.transform.SetAsLastSibling();
        Reclip(block);
        return block;
    }

    // A block scrolled out of view in the Blocks panel is hidden (culled) by the panel's mask, and a copy of it
    // keeps that. Show it again and let the Code Editor frame decide what is clipped.
    static void Reclip(CodeBlock block)
    {
        foreach (MaskableGraphic g in block.GetComponentsInChildren<MaskableGraphic>(true))
        {
            g.canvasRenderer.cull = false;
            g.RecalculateClipping();
            g.RecalculateMasking();
        }
    }

    // Removes every block but "When mission starts" (at once, so the new blocks don't mix with them)
    void ClearProgram()
    {
        var old = new List<Transform>();
        foreach (Transform child in programArea)
        {
            CodeBlock block = child.GetComponent<CodeBlock>();
            if (block == null || block.type != Block.Start) old.Add(child);
        }
        foreach (Transform child in old)
        {
            child.SetParent(null, false);
            Destroy(child.gameObject);
        }
    }

    bool HasStartBlock()
    {
        foreach (Transform child in programArea)
        {
            CodeBlock block = child.GetComponent<CodeBlock>();
            if (block != null && block.type == Block.Start) return true;
        }
        return false;
    }

    // ---------------- EQUIPMENT NEEDED ----------------
    // "HD Cam, Drill 2" for the needed parts that are NOT on the rover; null if nothing is missing
    string MissingEquipment()
    {
        if (equipmentNeeded == null || equipmentNeeded.Count == 0) return null;

        if (partDatabase == null)
        {
            RoverLoadout loadout = rover != null ? rover.GetComponentInParent<RoverLoadout>() : null;
            if (loadout == null) loadout = FindAnyObjectByType<RoverLoadout>();
            if (loadout != null) partDatabase = loadout.database;
        }

        var fitted = new HashSet<string>();
        if (partDatabase != null)
            foreach (PartType type in System.Enum.GetValues(typeof(PartType)))
            {
                PartData part = partDatabase.Equipped(type);
                if (part != null) fitted.Add(part.name.Trim().ToLowerInvariant());
            }

        var missing = new List<string>();
        foreach (string entry in equipmentNeeded)
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            bool ok;
            if (PartNameAttribute.IsAny(entry, out PartType type))         // "Any Camera": any real part of that type
                ok = partDatabase != null && !PartNameAttribute.IsNothing(partDatabase.Equipped(type));
            else                                                            // one exact part
                ok = fitted.Contains(entry.Trim().ToLowerInvariant());

            string name = PartNameAttribute.Show(entry.Trim());
            if (!ok && !missing.Contains(name)) missing.Add(name);
        }
        return missing.Count > 0 ? string.Join(", ", missing) : null;
    }

    // ---------------- CHECK THE SOLUTION ----------------
    // Warns about steps that can't be built or a program that RUN would refuse. Returns how many problems.
    int Check(Dictionary<Block, CodeBlock> templates)
    {
        int problems = 0;
        var open = new Stack<Block>();

        for (int i = 0; i < solution.Count; i++)
        {
            SolutionStep step = solution[i];
            string where = "[AI] Step " + (i + 1) + " (" + (step != null ? step.type.ToString() : "empty") + "): ";
            if (step == null) { Warn(where + "is empty.", ref problems); continue; }
            if (step.type == Block.Start) { Warn(where + "'When mission starts' is added by itself, remove this step.", ref problems); continue; }

            if (!templates.TryGetValue(step.type, out CodeBlock template))
                Warn(where + "there is no such block in the Blocks panel (it is skipped).", ref problems);
            else
            {
                if (!string.IsNullOrWhiteSpace(step.option) && !template.HasOption(step.option))
                    Warn(where + "the block has no option '" + step.option.Trim() + "'.", ref problems);
                if (!string.IsNullOrWhiteSpace(step.sign) && System.Array.IndexOf(Signs, step.sign.Trim()) < 0)
                    Warn(where + "sign '" + step.sign.Trim() + "' must be <, > or =.", ref problems);
            }

            switch (step.type)                  // the same If / Repeat pairing RUN checks
            {
                case Block.If:
                case Block.RepeatTimes:
                case Block.RepeatWhile:
                case Block.RepeatForever:
                    open.Push(step.type);
                    break;
                case Block.Else:
                    if (open.Count == 0 || open.Peek() != Block.If) Warn(where + "has no If before it.", ref problems);
                    else { open.Pop(); open.Push(Block.Else); }
                    break;
                case Block.EndIf:
                    if (open.Count == 0 || (open.Peek() != Block.If && open.Peek() != Block.Else)) Warn(where + "has no If to close.", ref problems);
                    else open.Pop();
                    break;
                case Block.EndRepeat:
                    if (open.Count == 0 || open.Peek() == Block.If || open.Peek() == Block.Else) Warn(where + "has no Repeat to close.", ref problems);
                    else open.Pop();
                    break;
            }
        }

        if (open.Count > 0)
            Warn("[AI] The solution has " + open.Count + " If / Repeat without its End.", ref problems);
        return problems;
    }

    void Warn(string text, ref int problems)
    {
        problems++;
        Debug.LogWarning(text, this);
    }

    void Tell(string text)
    {
        if (rover != null) rover.Message(text);
        else Debug.Log("[AI] " + text);
    }
}
