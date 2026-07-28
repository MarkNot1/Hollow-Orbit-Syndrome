#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public sealed class CircuitPuzzleEditorWindow : EditorWindow
{
    private CircuitPuzzleSystem puzzleSystem;
    private CircuitPuzzleSnapshotSO snapshot;
    private string newPuzzleName = "New Circuit Puzzle";

    [MenuItem("Tools/Circuit/Puzzle Snapshots")]
    public static void Open()
        => GetWindow<CircuitPuzzleEditorWindow>("Circuit Puzzles");

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Enter Play Mode, inspect the circuit to capture, then create or overwrite a portable snapshot.",
            MessageType.Info);
        puzzleSystem = (CircuitPuzzleSystem)EditorGUILayout.ObjectField(
            "Puzzle System", puzzleSystem, typeof(CircuitPuzzleSystem), true);
        snapshot = (CircuitPuzzleSnapshotSO)EditorGUILayout.ObjectField(
            "Snapshot", snapshot, typeof(CircuitPuzzleSnapshotSO), false);
        newPuzzleName = EditorGUILayout.TextField("New Puzzle Name", newPuzzleName);

        if (puzzleSystem == null)
            puzzleSystem = FindFirstObjectByType<CircuitPuzzleSystem>(FindObjectsInactive.Include);

        using (new EditorGUI.DisabledScope(puzzleSystem == null || !Application.isPlaying))
        {
            if (GUILayout.Button("Capture New Snapshot"))
                CaptureNew();
            if (GUILayout.Button("Overwrite Selected Snapshot"))
                Overwrite();
        }

        using (new EditorGUI.DisabledScope(puzzleSystem == null || snapshot == null))
        {
            if (GUILayout.Button("Validate Selected Snapshot"))
                ValidateSnapshot();
        }
    }

    private void CaptureNew()
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "Save Circuit Puzzle",
            string.IsNullOrWhiteSpace(newPuzzleName) ? "CircuitPuzzle" : newPuzzleName,
            "asset",
            "Choose where to store the portable puzzle snapshot.");
        if (string.IsNullOrWhiteSpace(path))
            return;

        CircuitPuzzleSnapshotSO created = CreateInstance<CircuitPuzzleSnapshotSO>();
        created.name = newPuzzleName;
        if (!puzzleSystem.Capture(created, out string error))
        {
            DestroyImmediate(created);
            EditorUtility.DisplayDialog("Capture failed", error, "OK");
            return;
        }

        AssetDatabase.CreateAsset(created, path);
        AssetDatabase.SaveAssets();
        snapshot = created;
        Selection.activeObject = created;
    }

    private void Overwrite()
    {
        if (snapshot == null)
            return;
        if (!EditorUtility.DisplayDialog(
                "Overwrite circuit puzzle?",
                $"Replace all data in '{snapshot.name}' with the active circuit?",
                "Overwrite",
                "Cancel"))
            return;

        Undo.RecordObject(snapshot, "Overwrite Circuit Puzzle Snapshot");
        if (!puzzleSystem.Capture(snapshot, out string error))
        {
            Undo.PerformUndo();
            EditorUtility.DisplayDialog("Capture failed", error, "OK");
            return;
        }
        EditorUtility.SetDirty(snapshot);
        AssetDatabase.SaveAssets();
    }

    private void ValidateSnapshot()
    {
        bool valid = puzzleSystem.ValidateForActiveBoard(snapshot, out string error);
        EditorUtility.DisplayDialog(
            valid ? "Snapshot is valid" : "Snapshot is invalid",
            valid ? "The snapshot can be loaded on the active circuit board." : error,
            "OK");
    }
}
#endif
