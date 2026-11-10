using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > RoverOps > Move Scene To Canvas Prefab
// For a scene that has BOTH an old (plain) Canvas and the new Canvas prefab:
//   1. every reference in the scene that points into the old canvas is moved to the same object in the new one
//      (matched by its path in the hierarchy, e.g. "Programming/Code Editor/Viewport/Program")
//   2. links the old canvas had to scene objects (button OnClick targets, Rover, MissionHandler...) are copied
//      onto the new canvas where it has none yet
//   3. the old canvas is deleted
// Everything is one Undo step. Check the Console for anything it couldn't match, then save the scene.
public static class CanvasSwapTool
{
    [MenuItem("Tools/RoverOps/Move Scene To Canvas Prefab")]
    static void Run()
    {
        GameObject oldRoot = null, newRoot = null;
        foreach (GameObject go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (go.GetComponent<Canvas>() == null) continue;
            if (PrefabUtility.IsPartOfPrefabInstance(go)) { if (newRoot == null) newRoot = go; }
            else if (go.name == "Canvas" && oldRoot == null) oldRoot = go;
        }
        if (oldRoot == null || newRoot == null)
        {
            EditorUtility.DisplayDialog("Move Scene To Canvas Prefab",
                "This scene needs both an old plain 'Canvas' and a Canvas prefab instance at the top of the Hierarchy.", "OK");
            return;
        }

        // ---------- map every object / component of the old canvas to the new one ----------
        var map = new Dictionary<Object, Object>();
        var missing = new List<string>();
        foreach (Transform t in oldRoot.GetComponentsInChildren<Transform>(true))
        {
            string path = PathOf(t, oldRoot.transform);
            Transform n = Resolve(newRoot.transform, path);
            if (n == null) { missing.Add(path); continue; }
            map[t.gameObject] = n.gameObject;
            var seen = new Dictionary<System.Type, int>();
            foreach (Component c in t.GetComponents<Component>())
            {
                if (c == null) continue;
                System.Type type = c.GetType();
                seen.TryGetValue(type, out int index);
                seen[type] = index + 1;
                Component[] same = n.GetComponents(type);
                if (index < same.Length) map[c] = same[index];
            }
        }

        var oldObjects = new HashSet<Object>();
        foreach (Transform t in oldRoot.GetComponentsInChildren<Transform>(true))
        {
            oldObjects.Add(t.gameObject);
            foreach (Component c in t.GetComponents<Component>()) if (c != null) oldObjects.Add(c);
        }

        if (!EditorUtility.DisplayDialog("Move Scene To Canvas Prefab",
                "Old canvas: " + oldRoot.name + "\nNew canvas: " + newRoot.name + "\n\n" + map.Count + " objects matched, "
                + missing.Count + " not found in the new canvas (listed in the Console).\n\nMove all references and delete the old canvas?",
                "Do it", "Cancel")) return;

        Undo.SetCurrentGroupName("Move Scene To Canvas Prefab");
        int group = Undo.GetCurrentGroup();
        int moved = 0, copied = 0;
        var unmatched = new StringBuilder();

        // ---------- 1. everything outside the old canvas: point to the new canvas instead ----------
        foreach (GameObject root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root == oldRoot) continue;
            foreach (Component c in root.GetComponentsInChildren<Component>(true))
            {
                if (c == null || c is Transform) continue;
                var so = new SerializedObject(c);
                bool changed = false;
                SerializedProperty p = so.GetIterator();
                while (p.Next(true))
                {
                    if (p.propertyType != SerializedPropertyType.ObjectReference || Skip(p.name)) continue;
                    Object v = p.objectReferenceValue;
                    if (v == null || !oldObjects.Contains(v)) continue;
                    if (map.TryGetValue(v, out Object nv)) { p.objectReferenceValue = nv; changed = true; moved++; }
                    else unmatched.AppendLine(c.gameObject.name + " (" + c.GetType().Name + ")." + p.propertyPath + " -> " + v.name);
                }
                if (changed) { Undo.RecordObject(c, "Move references"); so.ApplyModifiedProperties(); }
            }
        }

        // ---------- 2. the old canvas's own links to scene objects: copy where the new canvas has none ----------
        foreach (var pair in map)
        {
            if (!(pair.Key is Component oc) || oc is Transform) continue;
            Component nc = (Component)pair.Value;
            var oso = new SerializedObject(oc);
            var nso = new SerializedObject(nc);
            bool changed = false;
            SerializedProperty p = oso.GetIterator();
            while (p.Next(true))
            {
                if (p.propertyType != SerializedPropertyType.ObjectReference || Skip(p.name)) continue;
                Object v = p.objectReferenceValue;
                if (v == null || oldObjects.Contains(v)) continue;            // only links that leave the old canvas
                SerializedProperty np = nso.FindProperty(p.propertyPath);
                if (np == null || np.propertyType != SerializedPropertyType.ObjectReference || np.objectReferenceValue != null) continue;
                np.objectReferenceValue = v;
                changed = true; copied++;
            }
            if (changed) { Undo.RecordObject(nc, "Copy scene links"); nso.ApplyModifiedProperties(); }
        }

        // ---------- 3. remove the old canvas ----------
        Undo.DestroyObjectImmediate(oldRoot);
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

        var log = new StringBuilder();
        log.AppendLine("[Canvas Swap] Done: " + moved + " references moved to the new canvas, " + copied + " scene links copied onto it. Save the scene.");
        if (missing.Count > 0) log.AppendLine("Not in the new canvas (" + missing.Count + "):\n  " + string.Join("\n  ", missing));
        if (unmatched.Length > 0) log.AppendLine("References that had nothing to move to (now empty, set them by hand):\n" + unmatched);
        Debug.Log(log.ToString());
        Selection.activeGameObject = newRoot;
    }

    // Unity's own wiring between objects: never touched
    static bool Skip(string n) => n == "m_GameObject" || n == "m_Script" || n == "m_Father" || n == "m_CorrespondingSourceObject"
                                  || n == "m_PrefabInstance" || n == "m_PrefabAsset";

    // "Programming/Code Editor#1/Viewport": name, plus #k when siblings share a name
    static string PathOf(Transform t, Transform root)
    {
        var parts = new List<string>();
        for (Transform x = t; x != null && x != root; x = x.parent) parts.Insert(0, Segment(x));
        return string.Join("/", parts);
    }

    static string Segment(Transform x)
    {
        if (x.parent == null) return x.name;
        int k = 0;
        for (int i = 0; i < x.GetSiblingIndex(); i++) if (x.parent.GetChild(i).name == x.name) k++;
        return k == 0 ? x.name : x.name + "#" + k;
    }

    static Transform Resolve(Transform root, string path)
    {
        if (path == "") return root;
        Transform cur = root;
        foreach (string seg in path.Split('/'))
        {
            string name = seg; int k = 0;
            int hash = seg.LastIndexOf('#');
            if (hash > 0 && int.TryParse(seg.Substring(hash + 1), out int parsed)) { name = seg.Substring(0, hash); k = parsed; }
            Transform found = null;
            foreach (Transform ch in cur) if (ch.name == name && k-- == 0) { found = ch; break; }
            if (found == null) return null;
            cur = found;
        }
        return cur;
    }
}
