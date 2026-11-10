using UnityEditor;
using UnityEngine;

// Tools > RoverOps > Space Points: see and change the player's Space Points (SP) while testing.
// Works in Edit mode and in Play mode (the garage's SP text updates at once). Editor only: not in the build.
public class SpacePointsWindow : EditorWindow
{
    PartDatabase database;
    int amount = 100;
    int exact;

    [MenuItem("Tools/RoverOps/Space Points")]
    static void Open()
    {
        SpacePointsWindow w = GetWindow<SpacePointsWindow>("Space Points");
        w.minSize = new Vector2(280, 280);
    }

    void OnEnable()
    {
        if (database != null) return;
        string[] found = AssetDatabase.FindAssets("t:PartDatabase");
        if (found.Length > 0) database = AssetDatabase.LoadAssetAtPath<PartDatabase>(AssetDatabase.GUIDToAssetPath(found[0]));
    }

    void OnGUI()
    {
        database = (PartDatabase)EditorGUILayout.ObjectField("Part Database", database, typeof(PartDatabase), false);
        if (database == null)
        {
            EditorGUILayout.HelpBox("No Part Database found. Drag yours in above.", MessageType.Warning);
            return;
        }

        int sp = database.SpacePoints;
        EditorGUILayout.Space(6);
        GUIStyle big = new GUIStyle(EditorStyles.boldLabel) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
        EditorGUILayout.LabelField(sp.ToString("N0") + " SP", big, GUILayout.Height(32));
        EditorGUILayout.Space(4);

        // quick buttons
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("-1000")) Change(-1000);
        if (GUILayout.Button("-100")) Change(-100);
        if (GUILayout.Button("+100")) Change(100);
        if (GUILayout.Button("+1000")) Change(1000);
        EditorGUILayout.EndHorizontal();

        // any amount
        EditorGUILayout.Space(4);
        EditorGUILayout.BeginHorizontal();
        amount = Mathf.Max(0, EditorGUILayout.IntField("Amount", amount));
        if (GUILayout.Button("−", GUILayout.Width(30))) Change(-amount);
        if (GUILayout.Button("+", GUILayout.Width(30))) Change(amount);
        EditorGUILayout.EndHorizontal();

        // exact value
        EditorGUILayout.BeginHorizontal();
        exact = Mathf.Max(0, EditorGUILayout.IntField("Set to", exact));
        if (GUILayout.Button("Set", GUILayout.Width(64))) Apply(() => database.SetSpacePoints(exact));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(6);
        if (GUILayout.Button("Reset to starting SP (" + database.startingSpacePoints.ToString("N0") + ")"))
            Apply(database.ResetSpacePoints);

        // NEW GAME: wipes every save (SP, bought + equipped parts, finished missions, stars, rewards)
        EditorGUILayout.Space(10);
        GUI.backgroundColor = new Color(1f, 0.45f, 0.45f);
        if (GUILayout.Button("RESET GAME (start completely new)", GUILayout.Height(28)))
        {
            if (EditorUtility.DisplayDialog("Reset game?",
                "This deletes ALL saved progress:\n\n• Space Points\n• Bought and equipped parts\n• Finished missions and stars\n• Star rewards already paid\n\nThe game will start like the first time. This can't be undone.",
                "Reset everything", "Cancel"))
                Apply(ResetGame);
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space(4);
        EditorGUILayout.HelpBox("SP are saved on this computer (PlayerPrefs), the same balance the garage shop and the AI use.", MessageType.None);
    }

    void Change(int delta) => Apply(() => database.SetSpacePoints(database.SpacePoints + delta));   // never below 0

    void ResetGame()
    {
        PlayerPrefs.DeleteAll();                 // every RoverOps save lives in PlayerPrefs
        database.ResetSpacePoints();             // back to Starting Space Points (0)
        PlayerPrefs.Save();
        Debug.Log("[RoverOps] Game reset: all progress deleted, starting a new game.");
    }

    void Apply(System.Action action)
    {
        action();
        // in Play mode, refresh the garage's SP text right away
        if (Application.isPlaying)
            foreach (GarageUI garage in Object.FindObjectsByType<GarageUI>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                garage.SendMessage("UpdateSpacePointsUI", SendMessageOptions.DontRequireReceiver);
        Repaint();
    }

    void OnInspectorUpdate() => Repaint();   // shows changes made by the game (buying, AI, rewards) while it runs
}
