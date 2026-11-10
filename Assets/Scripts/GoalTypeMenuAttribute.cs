using UnityEngine;

// Put [GoalTypeMenu] on a StarGoal's "type" field: its dropdown lists the goal types,
// plus a "Blocks" sub-menu with every block. Picking a block sets the type to UseBlock and the goal's "block".
public class GoalTypeMenuAttribute : PropertyAttribute { }

#if UNITY_EDITOR
[UnityEditor.CustomPropertyDrawer(typeof(GoalTypeMenuAttribute))]
public class GoalTypeMenuDrawer : UnityEditor.PropertyDrawer
{
    public override void OnGUI(Rect position, UnityEditor.SerializedProperty property, GUIContent label)
    {
        // the goal's "block" field, next to "type"
        string path = property.propertyPath;
        string blockPath = path.Substring(0, path.Length - property.name.Length) + "block";
        UnityEditor.SerializedProperty blockProp = property.serializedObject.FindProperty(blockPath);
        if (property.propertyType != UnityEditor.SerializedPropertyType.Enum || blockProp == null)
        {
            UnityEditor.EditorGUI.PropertyField(position, property, label);
            return;
        }

        // menu: every goal type except UseBlock, then "Blocks/<block>" (not "When mission starts": it's always there)
        var menu = new System.Collections.Generic.List<string>();
        var types = new System.Collections.Generic.List<GoalType>();
        var blocks = new System.Collections.Generic.List<Block>();
        foreach (GoalType t in System.Enum.GetValues(typeof(GoalType)))
        {
            if (t == GoalType.UseBlock) continue;
            menu.Add(UnityEditor.ObjectNames.NicifyVariableName(t.ToString()));
            types.Add(t);
            blocks.Add(default);
        }
        foreach (Block b in System.Enum.GetValues(typeof(Block)))
        {
            if (b == Block.Start) continue;
            menu.Add("Blocks/" + StarGoal.BlockName(b));
            types.Add(GoalType.UseBlock);
            blocks.Add(b);
        }

        GoalType current = (GoalType)property.intValue;
        Block currentBlock = (Block)blockProp.intValue;
        int index = 0;
        for (int i = 0; i < types.Count; i++)
            if (types[i] == current && (current != GoalType.UseBlock || blocks[i] == currentBlock)) { index = i; break; }

        UnityEditor.EditorGUI.BeginChangeCheck();
        int picked = UnityEditor.EditorGUI.Popup(position, label.text, index, menu.ToArray());
        if (UnityEditor.EditorGUI.EndChangeCheck())
        {
            property.intValue = (int)types[picked];
            if (types[picked] == GoalType.UseBlock) blockProp.intValue = (int)blocks[picked];
        }
    }
}
#endif
