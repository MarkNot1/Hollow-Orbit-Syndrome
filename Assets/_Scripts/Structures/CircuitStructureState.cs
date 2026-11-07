using System;
using UnityEngine;

public sealed class CircuitStructureState : MonoBehaviour, IStructurePrefabState
{
    public string CaptureState() => WithBoard(null);
    public void RestoreState(string json) => WithBoard(json);

    private string WithBoard(string json)
    {
        placementSystem placement = FindFirstObjectByType<placementSystem>(FindObjectsInactive.Include);
        CircuitPuzzleSystem puzzles = FindFirstObjectByType<CircuitPuzzleSystem>(FindObjectsInactive.Include);
        if (placement == null || puzzles == null)
            throw new InvalidOperationException("Circuit systems are required to save or load a circuit.");
        SpecialPrefabInteractable previous = placement.ActiveBoard?.Circuit;
        SpecialPrefabInteractable circuit = GetComponent<SpecialPrefabInteractable>();
        CircuitPuzzleSnapshotSO snapshot = ScriptableObject.CreateInstance<CircuitPuzzleSnapshotSO>();
        try
        {
            if (json == null)
            {
                placement.SetActiveCircuit(circuit);
                if (!puzzles.Capture(snapshot, out string error)) throw new InvalidOperationException(error);
                return JsonUtility.ToJson(snapshot);
            }
            JsonUtility.FromJsonOverwrite(json, snapshot);
            if (!puzzles.LoadSnapshot(snapshot, circuit, out string loadError)) throw new InvalidOperationException(loadError);
            return json;
        }
        finally
        {
            if (previous != null) placement.SetActiveCircuit(previous);
            else placement.ClearActiveCircuit();
            VoxelStructure.Release(snapshot);
        }
    }

    private void OnDestroy()
    {
        SpecialPrefabInteractable circuit = GetComponent<SpecialPrefabInteractable>();
        if (circuit == null) return;
        FindFirstObjectByType<ObjectPlacer>(FindObjectsInactive.Include)?.ForgetCircuit(circuit);
        FindFirstObjectByType<CircuitWireSystem>(FindObjectsInactive.Include)?.ForgetCircuit(circuit);
    }
}
