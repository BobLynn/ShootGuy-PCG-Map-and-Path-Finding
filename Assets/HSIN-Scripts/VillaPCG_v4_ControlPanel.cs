using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

public class VillaPCG_v4ControlPanel : MonoBehaviour
{
    [Header("Control Target")]
    public VillaPCG_v4 targetPcg;

    void Reset()
    {
        AutoAssignTarget();
    }

    void OnValidate()
    {
        if (targetPcg == null)
            AutoAssignTarget();
    }

    public void AutoAssignTarget()
    {
        targetPcg = GetComponent<VillaPCG_v4>();

        if (targetPcg == null)
            targetPcg = GetComponentInParent<VillaPCG_v4>();

        if (targetPcg == null)
            targetPcg = GetComponentInChildren<VillaPCG_v4>();

        if (targetPcg == null)
            targetPcg = Object.FindFirstObjectByType<VillaPCG_v4>();
    }

    public bool HasTarget()
    {
        return targetPcg != null;
    }

    public void GenerateCurrentLevel()
    {
        if (targetPcg != null)
            targetPcg.Generate();
    }

    public void ClearGeneratedVilla()
    {
        if (targetPcg != null)
            targetPcg.ClearGeneratedVilla();
    }

    public void RegenerateWithNewSeed()
    {
        if (targetPcg != null)
            targetPcg.RegenerateWithNewSeed();
    }

    public void GenerateLevelOne()
    {
        if (targetPcg != null)
            targetPcg.GenerateLevelOne();
    }

    public void GenerateLevelTwo()
    {
        if (targetPcg != null)
            targetPcg.GenerateLevelTwo();
    }

    public void GenerateFinalPcgLevel()
    {
        if (targetPcg != null)
            targetPcg.GenerateFinalPcgLevel();
    }

    public void ShowWaypointGraph3D()
    {
        if (targetPcg != null)
            targetPcg.SetWaypointGraph3DVisible(true);
    }

    public void HideWaypointGraph3D()
    {
        if (targetPcg != null)
            targetPcg.SetWaypointGraph3DVisible(false);
    }

    public void ShowGridMap3D()
    {
        if (targetPcg != null)
            targetPcg.SetGridMap3DVisible(true);
    }

    public void HideGridMap3D()
    {
        if (targetPcg != null)
            targetPcg.SetGridMap3DVisible(false);
    }

    public void ShowNavigationDebug()
    {
        if (targetPcg != null)
            targetPcg.SetNavigationDebugVisible(true);
    }

    public void HideNavigationDebug()
    {
        if (targetPcg != null)
            targetPcg.SetNavigationDebugVisible(false);
    }

    public void ShowAgentDebug()
    {
        if (targetPcg != null)
            targetPcg.SetAgentDebugGizmosVisible(true);
    }

    public void HideAgentDebug()
    {
        if (targetPcg != null)
            targetPcg.SetAgentDebugGizmosVisible(false);
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(VillaPCG_v4ControlPanel))]
[CanEditMultipleObjects]
public class VillaPCG_v4ControlPanelEditor : Editor
{
    SerializedProperty targetPcgProperty;

    void OnEnable()
    {
        targetPcgProperty = serializedObject.FindProperty("targetPcg");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(targetPcgProperty);

        if (GUILayout.Button("Auto Assign Target"))
            ForEachPanel("Auto Assign Villa PCG Control Target", panel => panel.AutoAssignTarget(), markTargetDirty: false);

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(8);

        VillaPCG_v4 pcg = GetSingleTargetPcg();
        using (new EditorGUI.DisabledScope(pcg == null || targets.Length != 1))
        {
            DrawTargetQuickFields(pcg);
        }

        bool hasAnyTarget = HasAnyTargetPcg();
        using (new EditorGUI.DisabledScope(!hasAnyTarget))
        {
            DrawMapGenerationControls();
            DrawNavigationDebugControls();
            DrawAgentDebugControls();
        }
    }

    void DrawTargetQuickFields(VillaPCG_v4 pcg)
    {
        if (pcg == null)
        {
            EditorGUILayout.HelpBox("Assign a VillaPCG_v4 target to enable the control panel.", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField("Quick Settings", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        int seed = EditorGUILayout.IntField("Seed", pcg.seed);
        int currentLevel = EditorGUILayout.IntSlider("Current Level", pcg.currentLevel, 1, Mathf.Max(1, pcg.finalPcgLevel));

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Final Villa PCG", EditorStyles.boldLabel);
        int finalVillaComplexity = EditorGUILayout.IntSlider("Final Villa Complexity", pcg.finalVillaComplexity, 40, 100);
        VillaPCG_v4.FinalVillaStyle finalVillaStyle = (VillaPCG_v4.FinalVillaStyle)EditorGUILayout.EnumPopup("Final Villa Style", pcg.finalVillaStyle);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Enemy Count By Level", EditorStyles.boldLabel);
        int level1PatrolEnemyCount = EditorGUILayout.IntSlider("Level 1 Patrol", pcg.level1PatrolEnemyCount, 0, 16);
        int level1StandEnemyCount = EditorGUILayout.IntSlider("Level 1 Stand", pcg.level1StandEnemyCount, 0, 16);
        int level2PatrolEnemyCount = EditorGUILayout.IntSlider("Level 2 Patrol", pcg.level2PatrolEnemyCount, 0, 16);
        int level2StandEnemyCount = EditorGUILayout.IntSlider("Level 2 Stand", pcg.level2StandEnemyCount, 0, 16);
        int level3PatrolEnemyCount = EditorGUILayout.IntSlider("Level 3 Patrol", pcg.level3PatrolEnemyCount, 0, 32);
        int level3StandEnemyCount = EditorGUILayout.IntSlider("Level 3 Stand", pcg.level3StandEnemyCount, 0, 32);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Patrol Behavior Variants", EditorStyles.boldLabel);
        int roomPairPatrolEnemyCount = EditorGUILayout.IntSlider("Room Pair Patrol", pcg.roomPairPatrolEnemyCount, 0, 8);
        float roomPairPatrolAnchorWaitTime = EditorGUILayout.FloatField("Room Pair Anchor Wait", pcg.roomPairPatrolAnchorWaitTime);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(pcg, "Edit Villa PCG Quick Settings");
            pcg.seed = seed;
            pcg.currentLevel = currentLevel;
            pcg.finalVillaComplexity = finalVillaComplexity;
            pcg.finalVillaStyle = finalVillaStyle;
            pcg.level1PatrolEnemyCount = level1PatrolEnemyCount;
            pcg.level1StandEnemyCount = level1StandEnemyCount;
            pcg.level2PatrolEnemyCount = level2PatrolEnemyCount;
            pcg.level2StandEnemyCount = level2StandEnemyCount;
            pcg.level3PatrolEnemyCount = level3PatrolEnemyCount;
            pcg.level3StandEnemyCount = level3StandEnemyCount;
            pcg.roomPairPatrolEnemyCount = roomPairPatrolEnemyCount;
            pcg.roomPairPatrolAnchorWaitTime = Mathf.Max(0f, roomPairPatrolAnchorWaitTime);
            MarkPcgDirty(pcg);
            SceneView.RepaintAll();
        }

        EditorGUILayout.Space(8);
    }

    void DrawMapGenerationControls()
    {
        EditorGUILayout.LabelField("Map Generation", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Generate Current Level"))
                ForEachPanel("Generate Current Villa Level", panel => panel.GenerateCurrentLevel());

            if (GUILayout.Button("Clear Generated Villa"))
                ForEachPanel("Clear Generated Villa", panel => panel.ClearGeneratedVilla());
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Regenerate With New Seed"))
                ForEachPanel("Regenerate Villa With New Seed", panel => panel.RegenerateWithNewSeed());
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Generate Level 1"))
                ForEachPanel("Generate Villa Level 1", panel => panel.GenerateLevelOne());

            if (GUILayout.Button("Generate Level 2"))
                ForEachPanel("Generate Villa Level 2", panel => panel.GenerateLevelTwo());

            if (GUILayout.Button("Generate Final PCG Level"))
                ForEachPanel("Generate Final PCG Villa Level", panel => panel.GenerateFinalPcgLevel());
        }

        EditorGUILayout.Space(8);
    }

    void DrawNavigationDebugControls()
    {
        EditorGUILayout.LabelField("Navigation Debug Display", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Show Waypoint Graph 3D"))
                ForEachPanel("Show Waypoint Graph 3D", panel => panel.ShowWaypointGraph3D());

            if (GUILayout.Button("Hide Waypoint Graph 3D"))
                ForEachPanel("Hide Waypoint Graph 3D", panel => panel.HideWaypointGraph3D());
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Show Grid Map 3D"))
                ForEachPanel("Show Grid Map 3D", panel => panel.ShowGridMap3D());

            if (GUILayout.Button("Hide Grid Map 3D"))
                ForEachPanel("Hide Grid Map 3D", panel => panel.HideGridMap3D());
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Show Both"))
                ForEachPanel("Show Navigation Debug", panel => panel.ShowNavigationDebug());

            if (GUILayout.Button("Hide Both"))
                ForEachPanel("Hide Navigation Debug", panel => panel.HideNavigationDebug());
        }
    }

    void DrawAgentDebugControls()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Agent Debug Display", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Show Agent Debug"))
                ForEachPanel("Show Agent Debug Gizmos", panel => panel.ShowAgentDebug());

            if (GUILayout.Button("Hide Agent Debug"))
                ForEachPanel("Hide Agent Debug Gizmos", panel => panel.HideAgentDebug());
        }
    }

    void ForEachPanel(string undoName, System.Action<VillaPCG_v4ControlPanel> action, bool markTargetDirty = true)
    {
        serializedObject.ApplyModifiedProperties();

        foreach (Object selectedTarget in targets)
        {
            if (selectedTarget is not VillaPCG_v4ControlPanel panel)
                continue;

            Undo.RecordObject(panel, undoName);
            VillaPCG_v4 pcg = panel.targetPcg;

            if (markTargetDirty && pcg != null)
                Undo.RecordObject(pcg, undoName);

            action(panel);
            EditorUtility.SetDirty(panel);

            if (markTargetDirty && pcg != null)
                MarkPcgDirty(pcg);
        }

        serializedObject.Update();
        SceneView.RepaintAll();
    }

    VillaPCG_v4 GetSingleTargetPcg()
    {
        if (targets.Length != 1 || target is not VillaPCG_v4ControlPanel panel)
            return null;

        return panel.targetPcg;
    }

    bool HasAnyTargetPcg()
    {
        foreach (Object selectedTarget in targets)
        {
            if (selectedTarget is VillaPCG_v4ControlPanel panel && panel.targetPcg != null)
                return true;
        }

        return false;
    }

    static void MarkPcgDirty(VillaPCG_v4 pcg)
    {
        EditorUtility.SetDirty(pcg);

        if (pcg.waypointGraph != null)
            EditorUtility.SetDirty(pcg.waypointGraph);

        if (pcg.gridMap != null)
            EditorUtility.SetDirty(pcg.gridMap);

        if (!Application.isPlaying)
            EditorSceneManager.MarkSceneDirty(pcg.gameObject.scene);
    }
}
#endif
