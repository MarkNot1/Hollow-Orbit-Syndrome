using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed class CircuitWireSystem : MonoBehaviour
{
    private const string WireRootName = "Circuit Wires";
    private const string OpenSlotRootName = "Open Circuit Slots";
    private const float DefaultWireWidth = 0.003f;
    private const int ReferenceGridSize = 16;
    private const int WireOverlapCost = 1000;

    [SerializeField]
    private GameObject wirePrefab;

    [SerializeField]
    private GameObject openNodePrefab;

    [SerializeField]
    private Material wireMaterial;

    [SerializeField, Min(0.0005f)]
    private float wireWidth = DefaultWireWidth;

    [SerializeField, Min(1f)]
    private float wireHitboxWidthMultiplier = 4f;

    [SerializeField]
    private float wireSurfaceOffset = 0.004f;

    [SerializeField]
    private Color wireColor = new(0.95f, 0.55f, 0.08f, 1f);

    [SerializeField]
    private Color invalidColor = Color.red;

    [SerializeField]
    private LayerMask nodeLayerMask = ~0;

    [SerializeField]
    private LayerMask wireLayerMask = ~0;

    private readonly Dictionary<SpecialPrefabInteractable, List<WireConnection>> wiresByCircuit = new();
    private readonly Dictionary<SpecialPrefabInteractable, Dictionary<Vector2Int, int>> wireCellsByCircuit = new();
    private readonly List<Vector2Int> committedPath = new();
    private readonly List<Vector2Int> currentTail = new();

    private CircuitBoardContext activeBoard;
    private GridData activeGrid;
    private CircuitNode sourceNode;
    private CircuitNode hoveredNode;
    private WireConnection previewWire;
    private WireConnection hoveredRemovalWire;
    private Vector2Int? lastTargetCell;
    private bool currentTailValid;
    private Material runtimeMaterial;

    public event Action<WireConnection> ConnectionCreated;
    public event Action<WireConnection> ConnectionRemoved;

    public bool IsWiring => sourceNode != null;

    private void OnDestroy()
    {
        EndRemovalPreview();
        if (runtimeMaterial != null)
            Destroy(runtimeMaterial);
    }

    public void SetActiveBoard(CircuitBoardContext board, GridData grid)
    {
        CancelWiring();
        activeBoard = board;
        activeGrid = grid;
        if (board == null || board.Circuit == null)
            return;

        if (!wiresByCircuit.ContainsKey(board.Circuit))
            wiresByCircuit.Add(board.Circuit, new List<WireConnection>());
        if (!wireCellsByCircuit.ContainsKey(board.Circuit))
            wireCellsByCircuit.Add(board.Circuit, new Dictionary<Vector2Int, int>());

        RegisterExistingWires();
    }

    public void ClearActiveBoard()
    {
        CancelWiring();
        EndRemovalPreview();
        activeBoard = null;
        activeGrid = null;
    }

    public bool TryGetNode(placementInputManager inputManager, out CircuitNode node)
    {
        node = null;
        if (activeBoard == null || inputManager == null || inputManager.IsPointerOverUI() ||
            !inputManager.TryGetRaycastComponent(nodeLayerMask, out CircuitNode candidate))
            return false;

        if (!candidate.HasHost)
            candidate.Initialize(candidate.GetComponentInParent<PlacedCircuitComponent>());

        if (!candidate.HasHost || !candidate.transform.IsChildOf(activeBoard.Circuit.transform))
            return false;

        node = candidate;
        return true;
    }

    public bool BeginWiring(CircuitNode node)
    {
        CancelWiring();
        if (activeBoard == null || activeGrid == null || node == null || !node.HasHost)
            return false;

        if (!TryGetEscapeCell(node, out Vector2Int sourceEscape))
            return false;

        sourceNode = node;
        committedPath.Add(sourceEscape);
        previewWire = CreateWire("Wire Preview", EnsureWireRoot());
        lastTargetCell = null;
        return previewWire != null;
    }

    public void UpdatePreview(placementInputManager inputManager)
    {
        if (!IsWiring || inputManager == null || previewWire == null)
            return;

        CircuitNode targetNode = null;
        TryGetNode(inputManager, out targetNode);
        Vector3Int pointerCell;
        if (targetNode != null)
        {
            pointerCell = targetNode.GetGridCell();
        }
        else if (!activeBoard.TryGetPointerCell(inputManager, out pointerCell, out _))
        {
            previewWire.gameObject.SetActive(false);
            hoveredNode = null;
            lastTargetCell = null;
            return;
        }

        Vector2Int targetCell = targetNode != null
            ? ToVector2(targetNode.GetGridCell())
            : ToVector2(pointerCell);

        if (lastTargetCell.HasValue && lastTargetCell.Value == targetCell && hoveredNode == targetNode)
            return;

        previewWire.gameObject.SetActive(true);
        lastTargetCell = targetCell;
        hoveredNode = targetNode;
        CircuitNode destination = GetValidDestination(targetNode);
        RebuildTail(targetCell, destination);
        if (targetNode != null && destination == null)
            currentTailValid = false;
        UpdatePreviewLine(destination);
    }

    public bool HandlePrimaryAction(placementInputManager inputManager)
    {
        if (!IsWiring || inputManager == null || inputManager.IsPointerOverUI())
            return false;

        UpdatePreview(inputManager);
        CircuitNode destination = GetValidDestination(hoveredNode);
        if (destination != null && currentTailValid)
        {
            CompleteWire(destination);
            return true;
        }

        if (hoveredNode != null || !currentTailValid || currentTail.Count < 2)
            return false;

        for (int i = 1; i < currentTail.Count; i++)
            committedPath.Add(currentTail[i]);

        currentTail.Clear();
        lastTargetCell = null;
        return false;
    }

    public void CancelWiring()
    {
        sourceNode = null;
        hoveredNode = null;
        committedPath.Clear();
        currentTail.Clear();
        lastTargetCell = null;
        currentTailValid = false;
        if (previewWire != null)
            Destroy(previewWire.gameObject);
        previewWire = null;
    }

    public bool IsFootprintClear(Vector3Int position, Vector2Int size)
    {
        if (activeBoard == null || !wireCellsByCircuit.TryGetValue(activeBoard.Circuit, out var occupied))
            return true;

        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                if (occupied.ContainsKey(new Vector2Int(position.x + x, position.y + y)))
                    return false;
            }
        }

        return true;
    }

    public bool TryRemoveSelectedWire(placementInputManager inputManager)
    {
        if (activeBoard == null || inputManager == null || inputManager.IsPointerOverUI() ||
            !inputManager.TryGetRaycastComponent(wireLayerMask, out WireConnection wire))
            return false;

        if (!wire.transform.IsChildOf(activeBoard.Circuit.transform))
            return false;

        if (hoveredRemovalWire == wire)
            hoveredRemovalWire = null;
        RemoveWire(wire);
        return true;
    }

    public bool CanInsertComponentIntoWire(
        Vector3Int position,
        Vector2Int size,
        CircuitElement element)
        => element != null && element.HasValidTerminals &&
            FindInsertion(position, size, element.Terminal1, element.Terminal2).wire != null;

    public bool TryInsertComponentIntoWire(PlacedCircuitComponent component)
    {
        if (component?.Element == null || !component.Element.HasValidTerminals)
            return false;

        var match = FindInsertion(
            component.GridPosition,
            component.FootprintSize,
            component.Element.Terminal1,
            component.Element.Terminal2);
        if (match.wire == null)
            return false;

        CircuitNode start = match.wire.StartNode;
        CircuitNode end = match.wire.EndNode;
        List<Vector2Int> left = match.wire.RoutedCells.Take(match.before + 1).ToList();
        List<Vector2Int> right = match.wire.RoutedCells.Skip(match.after).ToList();
        RemoveWire(match.wire);
        RestoreWire(start, match.entry, left);
        RestoreWire(match.exit, end, right);
        return true;
    }

    public void UpdateRemovalPreview(placementInputManager inputManager)
    {
        WireConnection candidate = null;
        if (activeBoard != null && inputManager != null && !inputManager.IsPointerOverUI() &&
            inputManager.TryGetRaycastComponent(
                wireLayerMask,
                out WireConnection raycastWire) &&
            raycastWire.transform.IsChildOf(activeBoard.Circuit.transform))
        {
            candidate = raycastWire;
        }

        if (candidate == hoveredRemovalWire)
            return;

        EndRemovalPreview();
        hoveredRemovalWire = candidate;
        if (hoveredRemovalWire != null)
            hoveredRemovalWire.SetAppearance(EnsureMaterial(), invalidColor);
    }

    public void EndRemovalPreview()
    {
        if (hoveredRemovalWire == null)
            return;

        hoveredRemovalWire.SetAppearance(EnsureMaterial(), wireColor);
        hoveredRemovalWire = null;
    }

    public OpenCircuitSlot PreserveConnections(PlacedCircuitComponent component)
    {
        if (component?.Element == null || !component.Element.HasValidTerminals || activeBoard == null)
            return null;

        CircuitNode first = component.Element.Terminal1;
        CircuitNode second = component.Element.Terminal2;
        OpenCircuitSlot slot = CreateOpenSlot(
            Guid.NewGuid().ToString("N"),
            component.GridPosition,
            component.FootprintSize,
            component.RotationQuarterTurns,
            activeBoard.TileGrid.InverseTransformPoint(first.ConnectionPoint.position),
            activeBoard.TileGrid.InverseTransformPoint(second.ConnectionPoint.position));
        RebindConnections(first, slot.Terminal1);
        RebindConnections(second, slot.Terminal2);
        slot.RefreshLabels();
        return slot;
    }

    public bool HasBlockingOpenSlot(Vector3Int position, Vector2Int size, out OpenCircuitSlot exact)
    {
        exact = null;
        if (activeBoard?.Circuit == null)
            return false;

        foreach (OpenCircuitSlot slot in activeBoard.Circuit.GetComponentsInChildren<OpenCircuitSlot>(true))
        {
            if (!slot.Overlaps(position, size))
                continue;
            if (slot.IsExactReplacement(position, size))
            {
                exact = slot;
                continue;
            }
            return true;
        }
        return false;
    }

    public bool TryClaimOpenSlot(PlacedCircuitComponent component)
    {
        if (component?.Element == null || !component.Element.HasValidTerminals ||
            HasBlockingOpenSlot(component.GridPosition, component.FootprintSize, out OpenCircuitSlot slot) ||
            slot == null)
            return false;

        CircuitNode first = component.Element.Terminal1;
        CircuitNode second = component.Element.Terminal2;
        float direct = DistanceSquared(slot.Terminal1, first) + DistanceSquared(slot.Terminal2, second);
        float crossed = DistanceSquared(slot.Terminal1, second) + DistanceSquared(slot.Terminal2, first);
        CircuitNode replacement1 = direct <= crossed ? first : second;
        CircuitNode replacement2 = direct <= crossed ? second : first;
        RebindConnections(slot.Terminal1, replacement1);
        RebindConnections(slot.Terminal2, replacement2);
        Destroy(slot.gameObject);
        return true;
    }

    public IReadOnlyList<WireConnection> GetActiveWires()
    {
        if (activeBoard?.Circuit != null &&
            wiresByCircuit.TryGetValue(activeBoard.Circuit, out List<WireConnection> wires))
            return wires;
        return Array.Empty<WireConnection>();
    }

    public OpenCircuitSlot[] GetActiveOpenSlots()
        => activeBoard?.Circuit == null
            ? Array.Empty<OpenCircuitSlot>()
            : activeBoard.Circuit.GetComponentsInChildren<OpenCircuitSlot>(true);

    public OpenCircuitSlot CreateOpenSlot(
        string id,
        Vector3Int position,
        Vector2Int size,
        int quarterTurns,
        Vector3 firstBoardLocalPosition,
        Vector3 secondBoardLocalPosition)
    {
        if (activeBoard == null)
            return null;

        Transform root = activeBoard.TileGrid.Find(OpenSlotRootName);
        if (root == null)
        {
            root = new GameObject(OpenSlotRootName).transform;
            root.SetParent(activeBoard.TileGrid, false);
        }

        GameObject slotObject = new($"Open Slot {id}");
        slotObject.transform.SetParent(root, false);
        OpenCircuitSlot slot = slotObject.AddComponent<OpenCircuitSlot>();
        CircuitNode first = CreateOpenNode(slotObject.transform, "A", firstBoardLocalPosition);
        CircuitNode second = CreateOpenNode(slotObject.transform, "B", secondBoardLocalPosition);
        slot.Initialize(id, position, size, quarterTurns, first, second);
        return slot;
    }

    public WireConnection RestoreWire(
        CircuitNode start,
        CircuitNode end,
        IReadOnlyList<Vector2Int> route)
    {
        if (activeBoard == null || start == null || end == null)
            return null;

        Transform root = EnsureWireRoot();
        List<Vector3> points = BuildLocalPoints(
            route,
            start.ConnectionPoint.position,
            end.ConnectionPoint.position);
        WireConnection wire = CreateWire($"Wire {start.TerminalId}-{end.TerminalId}", root);
        wire.Initialize(
            start, end, route, points, GetScaledWireWidth(), EnsureMaterial(), wireColor,
            wireHitboxWidthMultiplier, true);
        wiresByCircuit[activeBoard.Circuit].Add(wire);
        RegisterCells(activeBoard.Circuit, route);
        start.SlotOwner?.RefreshLabels();
        end.SlotOwner?.RefreshLabels();
        ConnectionCreated?.Invoke(wire);
        return wire;
    }

    public void ClearActiveWiresAndSlots()
    {
        if (activeBoard?.Circuit == null)
            return;

        if (wiresByCircuit.TryGetValue(activeBoard.Circuit, out List<WireConnection> wires))
        {
            foreach (WireConnection wire in new List<WireConnection>(wires))
                RemoveWire(wire);
            wires.Clear();
        }
        if (wireCellsByCircuit.TryGetValue(activeBoard.Circuit, out var occupied))
            occupied.Clear();
        foreach (OpenCircuitSlot slot in GetActiveOpenSlots())
        {
            if (slot != null)
                Destroy(slot.gameObject);
        }
    }

    public void RemoveWire(WireConnection wire)
    {
        if (wire == null)
            return;

        if (hoveredRemovalWire == wire)
            hoveredRemovalWire = null;

        SpecialPrefabInteractable circuit = wire.GetComponentInParent<SpecialPrefabInteractable>();
        if (circuit != null)
        {
            if (wiresByCircuit.TryGetValue(circuit, out List<WireConnection> wires))
                wires.Remove(wire);
            UnregisterCells(circuit, wire.RoutedCells);
        }

        CircuitNode formerStart = wire.StartNode;
        CircuitNode formerEnd = wire.EndNode;
        wire.Disconnect();
        ConnectionRemoved?.Invoke(wire);
        Destroy(wire.gameObject);
        formerStart?.SlotOwner?.RefreshLabels();
        formerEnd?.SlotOwner?.RefreshLabels();
    }

    private void RebuildTail(Vector2Int targetCell, CircuitNode destination)
    {
        currentTail.Clear();
        Vector2Int start = committedPath[^1];
        if (destination != null && !TryGetEscapeCell(destination, out targetCell))
        {
            currentTailValid = false;
            currentTail.Add(start);
            return;
        }

        HashSet<Vector2Int> committed = new(committedPath);
        committed.Remove(start);

        currentTailValid = CircuitWireRouter.TryFindPath(
            activeBoard.GridWidth,
            activeBoard.GridHeight,
            start,
            targetCell,
            cell => committed.Contains(cell) || IsComponentBlocked(cell),
            IsCommittedMoveBlocked,
            GetWireOverlapCost,
            out List<Vector2Int> route);

        if (currentTailValid)
            currentTail.AddRange(route);
        else
            // Never draw a fallback segment through an occupied footprint.
            // Keep only the safe committed endpoint until a route exists.
            currentTail.Add(start);
    }

    private bool IsCommittedMoveBlocked(Vector2Int from, Vector2Int to)
    {
        Vector2Int direction = to - from;
        if (Mathf.Abs(direction.x) != 1 || Mathf.Abs(direction.y) != 1)
            return false;

        for (int i = 0; i < committedPath.Count - 1; i++)
        {
            Vector2Int first = committedPath[i];
            Vector2Int second = committedPath[i + 1];
            Vector2Int committedDirection = second - first;
            if (Mathf.Abs(committedDirection.x) != 1 || Mathf.Abs(committedDirection.y) != 1)
                continue;

            if (from != first && from != second && to != first && to != second &&
                from + to == first + second)
                return true;
        }

        return false;
    }

    private bool IsComponentBlocked(Vector2Int cell)
        => activeGrid.IsOccupied(new Vector3Int(cell.x, cell.y, 0));

    private int GetWireOverlapCost(Vector2Int cell)
    {
        if (activeBoard != null &&
            wireCellsByCircuit.TryGetValue(activeBoard.Circuit, out Dictionary<Vector2Int, int> occupied) &&
            occupied.TryGetValue(cell, out int wireCount))
        {
            return WireOverlapCost * wireCount;
        }

        return 0;
    }

    private (WireConnection wire, int before, int after, CircuitNode entry, CircuitNode exit)
        FindInsertion(
        Vector3Int position,
        Vector2Int size,
        CircuitNode firstTerminal,
        CircuitNode secondTerminal)
    {
        if (activeBoard?.Circuit == null || firstTerminal == null || secondTerminal == null ||
            firstTerminal == secondTerminal ||
            !wiresByCircuit.TryGetValue(activeBoard.Circuit, out List<WireConnection> wires))
            return default;

        List<WireConnection> matches = wires.Where(wire => wire != null &&
            wire.RoutedCells.Any(cell =>
                cell.x >= position.x && cell.x < position.x + size.x &&
                cell.y >= position.y && cell.y < position.y + size.y)).ToList();
        if (matches.Count != 1)
            return default;

        WireConnection match = matches[0];
        if (!CircuitWireRouter.TryFindStraightPassThrough(
                match.RoutedCells, position, size,
                out int before, out int after, out Vector2Int wireDirection))
            return default;

        Vector3 worldDelta = secondTerminal.ConnectionPoint.position -
            firstTerminal.ConnectionPoint.position;
        Vector2 terminalAxis = new(
            Vector3.Dot(worldDelta, activeBoard.GridRight) /
                Mathf.Max(activeBoard.CellWorldWidth, 0.0001f),
            Vector3.Dot(worldDelta, activeBoard.GridForward) /
                Mathf.Max(activeBoard.CellWorldHeight, 0.0001f));
        if (match == null || terminalAxis.sqrMagnitude < 0.0001f ||
            Mathf.Abs(Vector2.Dot(terminalAxis.normalized, wireDirection)) < 0.95f)
            return default;

        Vector3 worldAxis = activeBoard.GridRight * wireDirection.x +
            activeBoard.GridForward * wireDirection.y;
        bool firstIsEntry = Vector3.Dot(firstTerminal.ConnectionPoint.position, worldAxis) <=
            Vector3.Dot(secondTerminal.ConnectionPoint.position, worldAxis);
        return (match, before, after,
            firstIsEntry ? firstTerminal : secondTerminal,
            firstIsEntry ? secondTerminal : firstTerminal);
    }

    private bool TryGetEscapeCell(CircuitNode node, out Vector2Int escapeCell)
    {
        escapeCell = default;
        if (node == null || !node.HasHost || activeBoard == null || activeGrid == null)
            return false;

        Vector3Int origin = node.Owner != null ? node.Owner.GridPosition : node.SlotOwner.GridPosition;
        Vector2Int size = node.Owner != null ? node.Owner.FootprintSize : node.SlotOwner.FootprintSize;
        Vector3 footprintCenter = activeBoard.GetFootprintCenter(origin, size);
        Vector3 terminalOffset = node.ConnectionPoint.position - footprintCenter;
        float horizontalOffset = Vector3.Dot(terminalOffset, activeBoard.GridRight);
        float verticalOffset = Vector3.Dot(terminalOffset, activeBoard.GridForward);
        float halfWidth = activeBoard.CellWorldWidth * size.x * 0.5f;
        float halfHeight = activeBoard.CellWorldHeight * size.y * 0.5f;

        int relativeX = Mathf.Clamp(
            Mathf.FloorToInt(horizontalOffset / activeBoard.CellWorldWidth + size.x * 0.5f),
            0,
            size.x - 1);
        int relativeY = Mathf.Clamp(
            Mathf.FloorToInt(verticalOffset / activeBoard.CellWorldHeight + size.y * 0.5f),
            0,
            size.y - 1);

        EscapeCandidate[] candidates =
        {
            new(new Vector2Int(origin.x - 1, origin.y + relativeY),
                Mathf.Abs(horizontalOffset + halfWidth)),
            new(new Vector2Int(origin.x + size.x, origin.y + relativeY),
                Mathf.Abs(halfWidth - horizontalOffset)),
            new(new Vector2Int(origin.x + relativeX, origin.y - 1),
                Mathf.Abs(verticalOffset + halfHeight)),
            new(new Vector2Int(origin.x + relativeX, origin.y + size.y),
                Mathf.Abs(halfHeight - verticalOffset))
        };
        Array.Sort(candidates, (first, second) => first.Distance.CompareTo(second.Distance));

        for (int i = 0; i < candidates.Length; i++)
        {
            Vector2Int candidate = candidates[i].Cell;
            Vector3Int candidate3 = new(candidate.x, candidate.y, 0);
            if (activeBoard.IsInside(candidate3) && !activeGrid.IsOccupied(candidate3))
            {
                escapeCell = candidate;
                return true;
            }
        }

        return false;
    }

    private CircuitNode GetValidDestination(CircuitNode candidate)
    {
        if (candidate == null || candidate == sourceNode || !candidate.HasHost ||
            IsSameHost(candidate, sourceNode) || sourceNode.IsConnectedTo(candidate))
            return null;

        return activeBoard.IsInside(candidate.GetGridCell()) ? candidate : null;
    }

    private void UpdatePreviewLine(CircuitNode destination)
    {
        List<Vector2Int> fullPath = MergePaths();
        List<Vector3> points = BuildLocalPoints(
            fullPath,
            sourceNode.ConnectionPoint.position,
            destination != null ? destination.ConnectionPoint.position : (Vector3?)null);
        previewWire.UpdatePreview(
            points,
            GetScaledWireWidth(),
            EnsureMaterial(),
            currentTailValid ? wireColor : invalidColor);
    }

    private void CompleteWire(CircuitNode destination)
    {
        List<Vector2Int> route = MergePaths();
        List<Vector3> points = BuildLocalPoints(
            route,
            sourceNode.ConnectionPoint.position,
            destination.ConnectionPoint.position);
        Transform root = EnsureWireRoot();
        WireConnection wire = CreateWire($"Wire {sourceNode.TerminalId}-{destination.TerminalId}", root);
        wire.Initialize(
            sourceNode,
            destination,
            route,
            points,
            GetScaledWireWidth(),
            EnsureMaterial(),
            wireColor,
            wireHitboxWidthMultiplier,
            true);

        List<WireConnection> wires = wiresByCircuit[activeBoard.Circuit];
        wires.Add(wire);
        RegisterCells(activeBoard.Circuit, route);
        ConnectionCreated?.Invoke(wire);
        sourceNode.SlotOwner?.RefreshLabels();
        destination.SlotOwner?.RefreshLabels();
        HideOpenNode(sourceNode);
        HideOpenNode(destination);

        WireConnection preview = previewWire;
        previewWire = null;
        if (preview != null)
            Destroy(preview.gameObject);
        sourceNode = null;
        hoveredNode = null;
        committedPath.Clear();
        currentTail.Clear();
        lastTargetCell = null;
        currentTailValid = false;
    }

    private List<Vector2Int> MergePaths()
    {
        List<Vector2Int> route = new(committedPath);
        for (int i = 1; i < currentTail.Count; i++)
            route.Add(currentTail[i]);
        return route;
    }

    private List<Vector3> BuildLocalPoints(
        IReadOnlyList<Vector2Int> route,
        Vector3 startPoint,
        Vector3? endPoint)
    {
        List<Vector2Int> compressed = CircuitWireRouter.Compress(route);
        List<Vector3> points = new(compressed.Count + 2)
        {
            activeBoard.TileGrid.InverseTransformPoint(startPoint)
        };

        foreach (Vector2Int cell in compressed)
        {
            Vector3 world = activeBoard.GetCellCenter(new Vector3Int(cell.x, cell.y, 0), wireSurfaceOffset);
            points.Add(activeBoard.TileGrid.InverseTransformPoint(world));
        }

        if (endPoint.HasValue)
            points.Add(activeBoard.TileGrid.InverseTransformPoint(endPoint.Value));

        RemoveDuplicatePoints(points);
        return points;
    }

    private WireConnection CreateWire(string objectName, Transform parent)
    {
        GameObject instance = wirePrefab != null
            ? Instantiate(wirePrefab, parent)
            : new GameObject(objectName, typeof(MeshFilter), typeof(MeshRenderer), typeof(WireConnection));
        instance.name = objectName;
        if (instance.transform.parent != parent)
            instance.transform.SetParent(parent, false);
        else
        {
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
        }

        WireConnection wire = instance.GetComponent<WireConnection>();
        if (wire == null)
            wire = instance.AddComponent<WireConnection>();
        if (instance.GetComponent<MeshFilter>() == null)
            instance.AddComponent<MeshFilter>();
        if (instance.GetComponent<MeshRenderer>() == null)
            instance.AddComponent<MeshRenderer>();

        return wire;
    }

    private Transform EnsureWireRoot()
    {
        Transform root = activeBoard.TileGrid.Find(WireRootName);
        if (root == null)
        {
            root = new GameObject(WireRootName).transform;
            root.SetParent(activeBoard.TileGrid, false);
        }

        return root;
    }

    private Material EnsureMaterial()
    {
        if (wireMaterial != null)
            return wireMaterial;
        if (runtimeMaterial != null)
            return runtimeMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        if (shader != null)
            runtimeMaterial = new Material(shader) { name = "Runtime Circuit Wire" };
        return runtimeMaterial;
    }

    private float GetScaledWireWidth()
    {
        if (activeBoard == null || activeBoard.GridWidth <= 0)
            return wireWidth;

        // Preserve the wire-to-cell proportion when the logical board resolution changes.
        return wireWidth * ReferenceGridSize / activeBoard.GridWidth;
    }

    private void RegisterExistingWires()
    {
        Transform root = activeBoard.TileGrid.Find(WireRootName);
        if (root == null)
            return;

        List<WireConnection> wires = wiresByCircuit[activeBoard.Circuit];
        foreach (WireConnection wire in root.GetComponentsInChildren<WireConnection>(true))
        {
            if (wire == null || wire.StartNode == null || wire.EndNode == null || wires.Contains(wire))
                continue;
            wires.Add(wire);
            RegisterCells(activeBoard.Circuit, wire.RoutedCells);
        }
    }

    private void RegisterCells(SpecialPrefabInteractable circuit, IReadOnlyList<Vector2Int> cells)
    {
        Dictionary<Vector2Int, int> occupied = wireCellsByCircuit[circuit];
        foreach (Vector2Int cell in cells)
            occupied[cell] = occupied.TryGetValue(cell, out int count) ? count + 1 : 1;
    }

    private void UnregisterCells(SpecialPrefabInteractable circuit, IReadOnlyList<Vector2Int> cells)
    {
        if (!wireCellsByCircuit.TryGetValue(circuit, out Dictionary<Vector2Int, int> occupied))
            return;

        foreach (Vector2Int cell in cells)
        {
            if (!occupied.TryGetValue(cell, out int count))
                continue;
            if (count <= 1)
                occupied.Remove(cell);
            else
                occupied[cell] = count - 1;
        }
    }

    private static void RemoveDuplicatePoints(List<Vector3> points)
    {
        for (int i = points.Count - 1; i > 0; i--)
        {
            if ((points[i] - points[i - 1]).sqrMagnitude < 0.000001f)
                points.RemoveAt(i);
        }
    }

    private static Vector2Int ToVector2(Vector3Int value)
        => new(value.x, value.y);

    private readonly struct EscapeCandidate
    {
        public Vector2Int Cell { get; }
        public float Distance { get; }

        public EscapeCandidate(Vector2Int cell, float distance)
        {
            Cell = cell;
            Distance = distance;
        }
    }

    private CircuitNode CreateOpenNode(Transform parent, string nodeName, Vector3 boardLocalPosition)
    {
        GameObject nodeObject = openNodePrefab != null
            ? Instantiate(openNodePrefab, parent)
            : GameObject.CreatePrimitive(PrimitiveType.Cube);
        nodeObject.name = $"Open Node {nodeName}";
        nodeObject.transform.SetParent(parent, false);
        nodeObject.transform.position = activeBoard.TileGrid.TransformPoint(boardLocalPosition);
        float scale = Mathf.Min(activeBoard.CellWorldWidth, activeBoard.CellWorldHeight) * 0.16f;
        Vector3 parentScale = parent.lossyScale;
        nodeObject.transform.localScale = new Vector3(
            scale / Mathf.Max(Mathf.Abs(parentScale.x), 0.0001f),
            scale / Mathf.Max(Mathf.Abs(parentScale.y), 0.0001f),
            scale / Mathf.Max(Mathf.Abs(parentScale.z), 0.0001f));
        if (nodeObject.TryGetComponent(out BoxCollider hitbox))
            hitbox.size = Vector3.one * 2.5f;
        foreach (Renderer renderer in nodeObject.GetComponentsInChildren<Renderer>(true))
            renderer.sharedMaterial = EnsureMaterial();

        CircuitNode node = nodeObject.GetComponent<CircuitNode>() ??
            nodeObject.AddComponent<CircuitNode>();
        node.SetTerminalId(nodeName);
        return node;
    }

    private static void RebindConnections(CircuitNode oldNode, CircuitNode newNode)
    {
        foreach (WireConnection wire in new List<WireConnection>(oldNode.Connections))
            wire.ReplaceEndpoint(oldNode, newNode);
    }

    private static float DistanceSquared(CircuitNode first, CircuitNode second)
        => first == null || second == null
            ? float.PositiveInfinity
            : (first.ConnectionPoint.position - second.ConnectionPoint.position).sqrMagnitude;

    private static void HideOpenNode(CircuitNode node)
    {
        if (node?.SlotOwner == null)
            return;
        foreach (Renderer renderer in node.GetComponentsInChildren<Renderer>(true))
            renderer.enabled = false;
        foreach (Collider collider in node.GetComponentsInChildren<Collider>(true))
            collider.enabled = false;
    }

    private static bool IsSameHost(CircuitNode first, CircuitNode second)
    {
        if (first.Owner != null && first.Owner == second.Owner)
            return first.Owner.Element == null ||
                first.Owner.Element.Kind != CircuitElementKind.Battery;
        return false;
    }
}
