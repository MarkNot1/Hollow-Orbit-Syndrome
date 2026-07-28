using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed class CircuitPuzzleSystem : MonoBehaviour
{
    [SerializeField]
    private placementSystem placementSystem;

    [SerializeField]
    private ObjectPlacer objectPlacer;

    [SerializeField]
    private CircuitWireSystem wireSystem;

    private void Awake()
        => ResolveReferences();

    private void Reset()
        => ResolveReferences();

    public bool ValidateForActiveBoard(CircuitPuzzleSnapshotSO snapshot, out string error)
    {
        ResolveReferences();
        return Validate(snapshot, placementSystem?.ActiveBoard, objectPlacer, out error);
    }

    public bool Capture(CircuitPuzzleSnapshotSO snapshot, out string error)
    {
        ResolveReferences();
        error = string.Empty;
        CircuitBoardContext board = placementSystem?.ActiveBoard;
        if (snapshot == null || board?.Circuit == null || objectPlacer == null || wireSystem == null)
        {
            error = "A snapshot, active circuit board, ObjectPlacer, and CircuitWireSystem are required.";
            return false;
        }

        List<PlacedCircuitComponent> components = objectPlacer.GetActiveComponents()
            .Where(component => component != null && component.Element != null)
            .OrderBy(component => component.GridPosition.y)
            .ThenBy(component => component.GridPosition.x)
            .ToList();
        List<OpenCircuitSlot> slots = wireSystem.GetActiveOpenSlots()
            .Where(slot => slot != null)
            .OrderBy(slot => slot.GridPosition.y)
            .ThenBy(slot => slot.GridPosition.x)
            .ToList();

        Dictionary<CircuitNode, PuzzleEndpointRecord> endpoints = new();
        List<PuzzleComponentRecord> componentRecords = new();
        for (int i = 0; i < components.Count; i++)
        {
            PlacedCircuitComponent component = components[i];
            int recordId = i + 1;
            componentRecords.Add(new PuzzleComponentRecord
            {
                recordId = recordId,
                componentId = component.ComponentId,
                gridPosition = component.GridPosition,
                footprint = component.FootprintSize,
                rotationQuarterTurns = component.RotationQuarterTurns,
                electricalValue = component.Element.Value,
                state = component.Element.State == CircuitComponentState.Broken
                    ? CircuitComponentState.Broken
                    : CircuitComponentState.Healthy,
                failureReason = component.Element.FailureReason
            });
            AddEndpoint(endpoints, component.Element.Terminal1, PuzzleEndpointHost.Component, recordId, 0);
            AddEndpoint(endpoints, component.Element.Terminal2, PuzzleEndpointHost.Component, recordId, 1);
        }

        List<PuzzleOpenSlotRecord> slotRecords = new();
        for (int i = 0; i < slots.Count; i++)
        {
            OpenCircuitSlot slot = slots[i];
            int recordId = i + 1;
            slotRecords.Add(new PuzzleOpenSlotRecord
            {
                recordId = recordId,
                gridPosition = slot.GridPosition,
                footprint = slot.FootprintSize,
                rotationQuarterTurns = slot.RotationQuarterTurns,
                firstTerminalBoardLocalPosition =
                    board.TileGrid.InverseTransformPoint(slot.Terminal1.ConnectionPoint.position),
                secondTerminalBoardLocalPosition =
                    board.TileGrid.InverseTransformPoint(slot.Terminal2.ConnectionPoint.position)
            });
            AddEndpoint(endpoints, slot.Terminal1, PuzzleEndpointHost.OpenSlot, recordId, 0);
            AddEndpoint(endpoints, slot.Terminal2, PuzzleEndpointHost.OpenSlot, recordId, 1);
        }

        List<PuzzleWireRecord> wireRecords = new();
        int wireId = 1;
        foreach (WireConnection wire in wireSystem.GetActiveWires())
        {
            if (wire == null || !endpoints.TryGetValue(wire.StartNode, out PuzzleEndpointRecord start) ||
                !endpoints.TryGetValue(wire.EndNode, out PuzzleEndpointRecord end))
            {
                error = $"Wire '{wire?.name}' has an endpoint outside the active puzzle.";
                return false;
            }

            wireRecords.Add(new PuzzleWireRecord
            {
                recordId = wireId++,
                start = start,
                end = end,
                routedCells = new List<Vector2Int>(wire.RoutedCells)
            });
        }

        snapshot.ReplaceData(
            string.IsNullOrWhiteSpace(snapshot.PuzzleName) ? board.Circuit.DisplayName : snapshot.PuzzleName,
            board.GridWidth,
            board.GridHeight,
            componentRecords,
            slotRecords,
            wireRecords);
        return true;
    }

    public bool LoadSnapshot(
        CircuitPuzzleSnapshotSO snapshot,
        SpecialPrefabInteractable targetBoard,
        out string error)
    {
        ResolveReferences();
        error = string.Empty;
        if (placementSystem == null || objectPlacer == null || wireSystem == null || targetBoard == null)
        {
            error = "Puzzle system references and a target board are required.";
            return false;
        }

        placementSystem.SetActiveCircuit(targetBoard);
        CircuitBoardContext board = placementSystem.ActiveBoard;
        if (!Validate(snapshot, board, objectPlacer, out error))
            return false;

        wireSystem.ClearActiveWiresAndSlots();
        objectPlacer.ClearActiveComponentsForSnapshot();

        Dictionary<(PuzzleEndpointHost, int, int), CircuitNode> endpoints = new();
        foreach (PuzzleComponentRecord record in snapshot.Components)
        {
            if (!objectPlacer.TryPlaceSnapshotComponent(
                    record.componentId,
                    record.gridPosition,
                    record.footprint,
                    record.rotationQuarterTurns,
                    record.electricalValue,
                    record.state,
                    record.failureReason,
                    out PlacedCircuitComponent placed))
            {
                error = $"Failed to instantiate component record {record.recordId}.";
                return false;
            }

            endpoints[(PuzzleEndpointHost.Component, record.recordId, 0)] = placed.Element.Terminal1;
            endpoints[(PuzzleEndpointHost.Component, record.recordId, 1)] = placed.Element.Terminal2;
        }

        foreach (PuzzleOpenSlotRecord record in snapshot.OpenSlots)
        {
            OpenCircuitSlot slot = wireSystem.CreateOpenSlot(
                $"snapshot-{record.recordId}",
                record.gridPosition,
                record.footprint,
                record.rotationQuarterTurns,
                record.firstTerminalBoardLocalPosition,
                record.secondTerminalBoardLocalPosition);
            endpoints[(PuzzleEndpointHost.OpenSlot, record.recordId, 0)] = slot.Terminal1;
            endpoints[(PuzzleEndpointHost.OpenSlot, record.recordId, 1)] = slot.Terminal2;
        }

        foreach (PuzzleWireRecord record in snapshot.Wires)
        {
            CircuitNode start = endpoints[Key(record.start)];
            CircuitNode end = endpoints[Key(record.end)];
            wireSystem.RestoreWire(start, end, record.routedCells);
        }

        foreach (OpenCircuitSlot slot in wireSystem.GetActiveOpenSlots())
            slot.RefreshLabels();
        CircuitSimulationSystem simulation =
            FindFirstObjectByType<CircuitSimulationSystem>(FindObjectsInactive.Include);
        simulation?.SuppressNextBatteryShortBurst();
        placementSystem.NotifyCircuitChanged();
        return true;
    }

    public static bool Validate(
        CircuitPuzzleSnapshotSO snapshot,
        CircuitBoardContext board,
        ObjectPlacer placer,
        out string error)
    {
        error = string.Empty;
        if (snapshot == null)
            return Fail("Snapshot is missing.", out error);
        if (snapshot.SchemaVersion != CircuitPuzzleSnapshotSO.CurrentSchemaVersion)
            return Fail($"Unsupported snapshot schema {snapshot.SchemaVersion}.", out error);
        if (board == null || snapshot.GridWidth != board.GridWidth || snapshot.GridHeight != board.GridHeight)
            return Fail("Snapshot and target board grid dimensions do not match.", out error);

        HashSet<int> componentIds = new();
        HashSet<int> slotIds = new();
        HashSet<int> wireIds = new();
        HashSet<Vector3Int> occupied = new();
        List<(Vector3Int position, Vector2Int size)> componentFootprints = new();
        List<(Vector3Int position, Vector2Int size)> slotFootprints = new();
        foreach (PuzzleComponentRecord record in snapshot.Components)
        {
            if (record == null || record.recordId <= 0 || !componentIds.Add(record.recordId))
                return Fail("Component record IDs must be positive and unique.", out error);
            if (placer == null || !placer.CanRestoreComponent(
                    record.componentId,
                    record.footprint,
                    record.rotationQuarterTurns))
                return Fail(
                    $"Component database ID {record.componentId} is unavailable or its footprint does not match.",
                    out error);
            if (!ValidateFootprint(record.gridPosition, record.footprint, board, occupied))
                return Fail($"Component record {record.recordId} has an invalid or overlapping footprint.", out error);
            componentFootprints.Add((record.gridPosition, record.footprint));
            if (!double.IsFinite(record.electricalValue) || record.electricalValue <= 0d)
                return Fail($"Component record {record.recordId} has an invalid electrical value.", out error);
            if (record.componentId == 4 && record.state == CircuitComponentState.Broken)
                return Fail("Battery records cannot be broken.", out error);
        }

        foreach (PuzzleOpenSlotRecord record in snapshot.OpenSlots)
        {
            if (record == null || record.recordId <= 0 || !slotIds.Add(record.recordId))
                return Fail("Open-slot record IDs must be positive and unique.", out error);
            if (record.footprint.x <= 0 || record.footprint.y <= 0 ||
                record.gridPosition.x < 0 || record.gridPosition.y < 0 ||
                record.gridPosition.x + record.footprint.x > board.GridWidth ||
                record.gridPosition.y + record.footprint.y > board.GridHeight)
                return Fail($"Open-slot record {record.recordId} is outside the board.", out error);
            foreach ((Vector3Int position, Vector2Int size) footprint in componentFootprints)
            {
                if (Overlaps(record.gridPosition, record.footprint, footprint.position, footprint.size))
                    return Fail($"Open-slot record {record.recordId} overlaps a component.", out error);
            }
            foreach ((Vector3Int position, Vector2Int size) footprint in slotFootprints)
            {
                if (Overlaps(record.gridPosition, record.footprint, footprint.position, footprint.size))
                    return Fail($"Open-slot record {record.recordId} overlaps another open slot.", out error);
            }
            slotFootprints.Add((record.gridPosition, record.footprint));
        }

        foreach (PuzzleWireRecord record in snapshot.Wires)
        {
            if (record == null || record.recordId <= 0 || !wireIds.Add(record.recordId))
                return Fail("Wire record IDs must be positive and unique.", out error);
            if (!EndpointExists(record.start, componentIds, slotIds) ||
                !EndpointExists(record.end, componentIds, slotIds))
                return Fail($"Wire record {record.recordId} references a missing endpoint.", out error);
            foreach (Vector2Int cell in record.routedCells)
            {
                if (cell.x < 0 || cell.y < 0 || cell.x >= board.GridWidth || cell.y >= board.GridHeight)
                    return Fail($"Wire record {record.recordId} has an out-of-bounds route.", out error);
            }
        }
        return true;
    }

    private static void AddEndpoint(
        IDictionary<CircuitNode, PuzzleEndpointRecord> endpoints,
        CircuitNode node,
        PuzzleEndpointHost host,
        int recordId,
        int terminalIndex)
    {
        if (node != null)
            endpoints[node] = new PuzzleEndpointRecord
            {
                host = host,
                recordId = recordId,
                terminalIndex = terminalIndex
            };
    }

    private static (PuzzleEndpointHost, int, int) Key(PuzzleEndpointRecord endpoint)
        => (endpoint.host, endpoint.recordId, endpoint.terminalIndex);

    private static bool EndpointExists(
        PuzzleEndpointRecord endpoint,
        ISet<int> components,
        ISet<int> slots)
        => endpoint.terminalIndex is 0 or 1 &&
            (endpoint.host == PuzzleEndpointHost.Component
                ? components.Contains(endpoint.recordId)
                : slots.Contains(endpoint.recordId));

    private static bool ValidateFootprint(
        Vector3Int position,
        Vector2Int size,
        CircuitBoardContext board,
        ISet<Vector3Int> occupied)
    {
        if (size.x <= 0 || size.y <= 0 || position.x < 0 || position.y < 0 ||
            position.x + size.x > board.GridWidth || position.y + size.y > board.GridHeight)
            return false;
        for (int x = 0; x < size.x; x++)
        for (int y = 0; y < size.y; y++)
        {
            if (!occupied.Add(position + new Vector3Int(x, y, 0)))
                return false;
        }
        return true;
    }

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }

    private static bool Overlaps(
        Vector3Int firstPosition,
        Vector2Int firstSize,
        Vector3Int secondPosition,
        Vector2Int secondSize)
        => firstPosition.x < secondPosition.x + secondSize.x &&
            firstPosition.x + firstSize.x > secondPosition.x &&
            firstPosition.y < secondPosition.y + secondSize.y &&
            firstPosition.y + firstSize.y > secondPosition.y;

    private void ResolveReferences()
    {
        placementSystem ??= GetComponent<placementSystem>() ??
            FindFirstObjectByType<placementSystem>(FindObjectsInactive.Include);
        objectPlacer ??= GetComponent<ObjectPlacer>() ??
            FindFirstObjectByType<ObjectPlacer>(FindObjectsInactive.Include);
        wireSystem ??= GetComponent<CircuitWireSystem>() ??
            FindFirstObjectByType<CircuitWireSystem>(FindObjectsInactive.Include);
    }
}
