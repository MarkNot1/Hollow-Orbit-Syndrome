using System;
using System.Collections.Generic;
using UnityEngine;

public enum PuzzleEndpointHost
{
    Component,
    OpenSlot
}

[Serializable]
public struct PuzzleEndpointRecord
{
    public PuzzleEndpointHost host;
    public int recordId;
    [Range(0, 1)]
    public int terminalIndex;
}

[Serializable]
public sealed class PuzzleComponentRecord
{
    public int recordId;
    public int componentId;
    public Vector3Int gridPosition;
    public Vector2Int footprint = Vector2Int.one;
    [Range(0, 3)]
    public int rotationQuarterTurns;
    public double electricalValue;
    public CircuitComponentState state;
    public string failureReason;
}

[Serializable]
public sealed class PuzzleOpenSlotRecord
{
    public int recordId;
    public Vector3Int gridPosition;
    public Vector2Int footprint = Vector2Int.one;
    [Range(0, 3)]
    public int rotationQuarterTurns;
    public Vector3 firstTerminalBoardLocalPosition;
    public Vector3 secondTerminalBoardLocalPosition;
}

[Serializable]
public sealed class PuzzleWireRecord
{
    public int recordId;
    public PuzzleEndpointRecord start;
    public PuzzleEndpointRecord end;
    public List<Vector2Int> routedCells = new();
}

[CreateAssetMenu(
    fileName = "CircuitPuzzle",
    menuName = "Circuit/Circuit Puzzle Snapshot")]
public sealed class CircuitPuzzleSnapshotSO : ScriptableObject
{
    public const int CurrentSchemaVersion = 1;

    [SerializeField]
    private string puzzleName;

    [SerializeField, HideInInspector]
    private int schemaVersion = CurrentSchemaVersion;

    [SerializeField, Min(1)]
    private int gridWidth = 10;

    [SerializeField, Min(1)]
    private int gridHeight = 10;

    [SerializeField]
    private List<PuzzleComponentRecord> components = new();

    [SerializeField]
    private List<PuzzleOpenSlotRecord> openSlots = new();

    [SerializeField]
    private List<PuzzleWireRecord> wires = new();

    public string PuzzleName => string.IsNullOrWhiteSpace(puzzleName) ? name : puzzleName;
    public int SchemaVersion => schemaVersion;
    public int GridWidth => gridWidth;
    public int GridHeight => gridHeight;
    public IReadOnlyList<PuzzleComponentRecord> Components => components;
    public IReadOnlyList<PuzzleOpenSlotRecord> OpenSlots => openSlots;
    public IReadOnlyList<PuzzleWireRecord> Wires => wires;

    public void ReplaceData(
        string displayName,
        int width,
        int height,
        List<PuzzleComponentRecord> componentRecords,
        List<PuzzleOpenSlotRecord> slotRecords,
        List<PuzzleWireRecord> wireRecords)
    {
        puzzleName = displayName?.Trim() ?? string.Empty;
        schemaVersion = CurrentSchemaVersion;
        gridWidth = Mathf.Max(1, width);
        gridHeight = Mathf.Max(1, height);
        components = componentRecords ?? new List<PuzzleComponentRecord>();
        openSlots = slotRecords ?? new List<PuzzleOpenSlotRecord>();
        wires = wireRecords ?? new List<PuzzleWireRecord>();
    }
}
