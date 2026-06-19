using System;
using System.Text;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

public class VillaPCG_v4EvaluationRunner : MonoBehaviour
{
    [Header("Target")]
    public VillaPCG_v4 pcg;

    [Header("Evaluation Setup")]
    [Range(1, 25)]
    public int seedCount = 5;
    public int firstSeed = 12345;
    public bool compareEnemyLayout = true;

    [Header("Evaluation Result")]
    [TextArea(6, 14)]
    public string lastEvaluationSummary;

    void Reset()
    {
        pcg = GetComponent<VillaPCG_v4>();

        if (pcg == null)
            pcg = FindFirstObjectByType<VillaPCG_v4>();
    }

    void OnValidate()
    {
        seedCount = Mathf.Max(1, seedCount);
    }

    public void RunEvaluationBatch()
    {
        if (pcg == null)
        {
            Debug.LogWarning("[VillaPCG Evaluation] Missing VillaPCG_v4 target.");
            return;
        }

        int originalSeed = pcg.seed;
        int originalLevel = pcg.currentLevel;
        bool originalRandomizeSeed = pcg.randomizeSeedOnPlay;
        bool originalGenerateEnemies = pcg.generateEnemyAgents;

        pcg.randomizeSeedOnPlay = false;

        int runCount = Mathf.Max(1, seedCount);
        StringBuilder summary = new StringBuilder();
        summary.AppendLine("VillaPCG v4 Evaluation Batch");
        summary.AppendLine($"level={originalLevel}, firstSeed={firstSeed}, seedCount={runCount}, compareEnemyLayout={compareEnemyLayout}");
        summary.AppendLine("criteria: reachability, generated layout scale, enemy coverage, generated route evidence");
        summary.AppendLine("run,mode,seed,rooms,connections,spawnToPrimary,primaryToSecondary,patrolEnemies,standingGuards,routePoints");

        for (int i = 0; i < runCount; i++)
        {
            int evaluationSeed = firstSeed + i;
            RunSingleEvaluation(summary, i + 1, "enemy", evaluationSeed, true, originalLevel);

            if (compareEnemyLayout)
                RunSingleEvaluation(summary, i + 1, "noEnemy", evaluationSeed, false, originalLevel);
        }

        pcg.seed = originalSeed;
        pcg.currentLevel = originalLevel;
        pcg.randomizeSeedOnPlay = originalRandomizeSeed;
        pcg.generateEnemyAgents = originalGenerateEnemies;
        lastEvaluationSummary = summary.ToString();

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            EditorUtility.SetDirty(this);
            EditorUtility.SetDirty(pcg);
            EditorSceneManager.MarkSceneDirty(gameObject.scene);

            if (pcg.gameObject.scene.handle != gameObject.scene.handle)
                EditorSceneManager.MarkSceneDirty(pcg.gameObject.scene);
        }
#endif
    }

    void RunSingleEvaluation(StringBuilder summary, int runIndex, string mode, int evaluationSeed, bool generateEnemies, int level)
    {
        pcg.seed = evaluationSeed;
        pcg.currentLevel = level;
        pcg.generateEnemyAgents = generateEnemies;
        pcg.Generate();
        AppendEvaluationRow(summary, runIndex, mode, evaluationSeed);
    }

    void AppendEvaluationRow(StringBuilder summary, int runIndex, string mode, int evaluationSeed)
    {
        int rooms = ReadIntMetric(pcg.lastMapRuleSummary, "rooms=", 0);
        int connections = ReadIntMetric(pcg.lastMapRuleSummary, "connections=", 0);
        bool spawnToPrimary = ReadBoolMetric(pcg.lastMapRuleSummary, "spawnToPrimary=", false);
        bool primaryToSecondary = ReadBoolMetric(pcg.lastMapRuleSummary, "primaryToSecondary=", false);

        Transform enemyRoot = pcg.transform.Find("Generated_EnemyAgents");
        Transform routeRoot = pcg.transform.Find("Generated_EnemyRoutes");
        int patrolCount = CountChildrenWithPrefix(enemyRoot, "patrolEnemyTest_PCG_");
        int standCount = CountChildrenWithPrefix(enemyRoot, "standEnemyTest_PCG_");
        int routePointCount = routeRoot != null ? routeRoot.childCount : 0;

        summary.AppendLine($"{runIndex},{mode},{evaluationSeed},{rooms},{connections},{spawnToPrimary},{primaryToSecondary},{patrolCount},{standCount},{routePointCount}");
    }

    int ReadIntMetric(string text, string token, int fallback)
    {
        string value = ReadMetricValue(text, token);
        return int.TryParse(value, out int result) ? result : fallback;
    }

    bool ReadBoolMetric(string text, string token, bool fallback)
    {
        string value = ReadMetricValue(text, token);
        return bool.TryParse(value, out bool result) ? result : fallback;
    }

    string ReadMetricValue(string text, string token)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        int start = text.IndexOf(token, StringComparison.Ordinal);
        if (start < 0)
            return string.Empty;

        start += token.Length;
        int end = start;
        while (end < text.Length && text[end] != ',' && text[end] != '\r' && text[end] != '\n')
            end++;

        return text.Substring(start, end - start).Trim();
    }

    int CountChildrenWithPrefix(Transform root, string prefix)
    {
        if (root == null)
            return 0;

        int count = 0;
        for (int i = 0; i < root.childCount; i++)
        {
            if (root.GetChild(i).name.StartsWith(prefix, StringComparison.Ordinal))
                count++;
        }

        return count;
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(VillaPCG_v4EvaluationRunner))]
[CanEditMultipleObjects]
public class VillaPCG_v4EvaluationRunnerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8);

        if (GUILayout.Button("Run Evaluation Batch"))
        {
            foreach (UnityEngine.Object selectedTarget in targets)
            {
                if (selectedTarget is not VillaPCG_v4EvaluationRunner runner)
                    continue;

                Undo.RecordObject(runner, "Run Villa PCG Evaluation Batch");

                if (runner.pcg != null)
                    Undo.RecordObject(runner.pcg, "Run Villa PCG Evaluation Batch");

                runner.RunEvaluationBatch();
            }

            SceneView.RepaintAll();
        }
    }
}
#endif
