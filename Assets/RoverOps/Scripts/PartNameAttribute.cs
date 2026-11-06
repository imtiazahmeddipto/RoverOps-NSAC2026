using UnityEngine;

// Put [PartName] on a string (or a list of strings) to pick a part from the Part Database in a dropdown.
// Besides single parts it offers whole categories ("Any Camera"), saved as "Any:Camera".
public class PartNameAttribute : PropertyAttribute
{
    public const string AnyPrefix = "Any:";

    // "Any:Camera" -> true and PartType.Camera
    public static bool IsAny(string value, out PartType type)
    {
        type = default;
        return value != null && value.StartsWith(AnyPrefix)
            && System.Enum.TryParse(value.Substring(AnyPrefix.Length), out type);
    }

    // A friendly name for the player: "Camera", "Drill", or the part's own name
    public static string Show(string value)
    {
        if (!IsAny(value, out PartType type)) return value;
        switch (type)
        {
            case PartType.Science: return "Drill";
            case PartType.Solar: return "Solar Panel";
            default: return type.ToString();
        }
    }

    // "No Camera" / "No Solar" are placeholders, not real equipment
    public static bool IsNothing(PartData part) =>
        part == null || part.name.Trim().StartsWith("No ", System.StringComparison.OrdinalIgnoreCase);
}

#if UNITY_EDITOR
[UnityEditor.CustomPropertyDrawer(typeof(PartNameAttribute))]
public class PartNameDrawer : UnityEditor.PropertyDrawer
{
    public override void OnGUI(Rect position, UnityEditor.SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != UnityEditor.SerializedPropertyType.String)
        {
            UnityEditor.EditorGUI.PropertyField(position, property, label);
            return;
        }

        // "Any/Camera" etc. first, then every part in every Part Database, shown as "Camera/HD Cam" (a sub-menu per type)
        var names = new System.Collections.Generic.List<string>();
        var menu = new System.Collections.Generic.List<string>();
        foreach (PartType type in System.Enum.GetValues(typeof(PartType)))
        {
            names.Add(PartNameAttribute.AnyPrefix + type);
            menu.Add("Any (whole category)/Any " + PartNameAttribute.Show(PartNameAttribute.AnyPrefix + type));
        }
        int anyCount = names.Count;
        foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:PartDatabase"))
        {
            var db = UnityEditor.AssetDatabase.LoadAssetAtPath<PartDatabase>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
            if (db == null) continue;
            foreach (PartData part in db.parts)
                if (part != null && !string.IsNullOrWhiteSpace(part.name) && !names.Contains(part.name))
                {
                    names.Add(part.name);
                    menu.Add(part.type + "/" + part.name);
                }
        }

        if (names.Count == anyCount)                // no database yet: type the name
        {
            UnityEditor.EditorGUI.PropertyField(position, property, label);
            return;
        }

        int index = names.IndexOf(property.stringValue);
        if (index < 0 && !string.IsNullOrEmpty(property.stringValue))
        {
            names.Insert(0, property.stringValue);  // a part that was renamed or removed: keep it, marked
            menu.Insert(0, "(missing) " + property.stringValue);
            index = 0;
        }
        if (index < 0) index = 0;

        int picked = UnityEditor.EditorGUI.Popup(position, label.text, index, menu.ToArray());
        if (property.stringValue != names[picked]) property.stringValue = names[picked];
    }
}
#endif
