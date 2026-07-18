using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class CircuitWireSystem : MonoBehaviour
{
    private const string WireRootName = "Circuit Wires";

    [SerializeField]
    private GameObject wirePrefab;

    [SerializeField]
    private Material wireMaterial;

    [SerializeField, Min(0.002f)]
    private float wireWidth = 0.025f;

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
    private Vector2Int? lastTargetCell;
    private bool currentTailValid;
    private Material runtimeMaterial;

    public event Action<WireConnection> ConnectionCreated;
    public event Action<WireConnection> ConnectionRemoved;

    public bool IsWiring => sourceNode != null;

    private void OnDestroy()
    {
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
        activeBoard = null;
        activeGrid = null;
    }

    public bool TryGetNode(placementInputManager inputManager, out CircuitNode node)
    {
        node = null;
        if (activeBoard == null || inputManager == null || inputManager.IsPointerOverUI() ||
            !inputManager.TryGetRaycastComponent(nodeLayerMask, out CircuitNode candidate))
            return false;

        if (candidate.Owner == null)
            candidate.Initialize(candidate.GetComponentInParent<PlacedCircuitComponent>());

        if (candidate.Owner == null || !candidate.transform.IsChildOf(activeBoard.Circuit.transform))
            return false;

        node = candidate;
        return true;
    }

    public bool BeginWiring(CircuitNode node)
    {
        CancelWiring();
        if (activeBoard == null || activeGrid == null || node == null || node.Owner == null)
            return false;

        Vector3Int sourceCell = node.GetGridCell();
        if (!activeBoard.IsInside(sourceCell))
            return false;

        sourceNode = node;
        committedPath.Add(ToVector2(sourceCell));
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

        RemoveWire(wire);
        return true;
    }

    public void RemoveConnections(PlacedCircuitComponent component)
    {
        if (component == null)
            return;

        HashSet<WireConnection> attached = new();
        foreach (CircuitNode node in component.GetComponentsInChildren<CircuitNode>(true))
        {
            foreach (WireConnection connection in node.Connections)
            {
                if (connection != null)
                    attached.Add(connection);
            }
        }

        foreach (WireConnection connection in attached)
            RemoveWire(connection);
    }

    public void RemoveWire(WireConnection wire)
    {
        if (wire == null)
            return;

        SpecialPrefabInteractable circuit = wire.GetComponentInParent<SpecialPrefabInteractable>();
        if (circuit != null)
        {
            if (wiresByCircuit.TryGetValue(circuit, out List<WireConnection> wires))
                wires.Remove(wire);
            UnregisterCells(circuit, wire.RoutedCells);
        }

        wire.Disconnect();
        ConnectionRemoved?.Invoke(wire);
        Destroy(wire.gameObject);
    }

    private void RebuildTail(Vector2Int targetCell, CircuitNode destination)
    {
        currentTail.Clear();
        Vector2Int start = committedPath[^1];
        Vector2Int? destinationCell = destination != null ? ToVector2(destination.GetGridCell()) : null;
        HashSet<Vector2Int> committed = new(committedPath);
        committed.Remove(start);

        currentTailValid = CircuitWireRouter.TryFindPath(
            activeBoard.GridWidth,
            activeBoard.GridHeight,
            start,
            targetCell,
            cell => committed.Contains(cell) || IsComponentBlocked(cell, destinationCell),
            IsCommittedMoveBlocked,
            out List<Vector2Int> route);

        currentTail.AddRange(currentTailValid
            ? route
            : CircuitWireRouter.BuildDirectPath(start, targetCell));
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

    private bool IsComponentBlocked(Vector2Int cell, Vector2Int? destinationCell)
    {
        Vector2Int sourceCell = ToVector2(sourceNode.GetGridCell());
        if (cell == sourceCell || destinationCell.HasValue && cell == destinationCell.Value)
            return false;

        return activeGrid.IsOccupied(new Vector3Int(cell.x, cell.y, 0));
    }

    private CircuitNode GetValidDestination(CircuitNode candidate)
    {
        if (candidate == null || candidate == sourceNode || candidate.Owner == null ||
            candidate.Owner == sourceNode.Owner || sourceNode.IsConnectedTo(candidate))
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
        previewWire.UpdatePreview(points, wireWidth, EnsureMaterial(), currentTailValid ? wireColor : invalidColor);
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
        wire.Initialize(sourceNode, destination, route, points, wireWidth, EnsureMaterial(), wireColor, true);

        List<WireConnection> wires = wiresByCircuit[activeBoard.Circuit];
        wires.Add(wire);
        RegisterCells(activeBoard.Circuit, route);
        ConnectionCreated?.Invoke(wire);

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
            : new GameObject(objectName, typeof(LineRenderer), typeof(WireConnection));
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
        if (instance.GetComponent<LineRenderer>() == null)
            instance.AddComponent<LineRenderer>();
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
}
