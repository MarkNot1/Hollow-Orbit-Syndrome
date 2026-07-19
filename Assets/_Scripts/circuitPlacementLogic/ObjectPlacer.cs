using System;
using System.Collections.Generic;
using UnityEngine;

public class ObjectPlacer : MonoBehaviour
{
    private const string PlacementRootName = "Placed Circuit Components";

    [SerializeField]
    private componentDatabaseSO componentDatabase;

    [SerializeField]
    private PreviewSystem preview;

    [SerializeField]
    private CircuitWireSystem wireSystem;

    [SerializeField, Range(0.1f, 1f)]
    private float componentCellPadding = 0.95f;

    private readonly Dictionary<SpecialPrefabInteractable, GridData> placedComponentsByCircuit = new();

    private CircuitBoardContext activeBoard;
    private GridData activeGrid;
    private componentData selectedComponent;
    private Vector2Int selectedFootprint;
    private int placementStartedFrame = -1;
    private Vector3Int? lastDetectedPosition;
    private PlacedCircuitComponent hoveredRemovalComponent;

    private bool HasActiveBoard => activeBoard != null && activeGrid != null;
    public GridData ActiveGrid => activeGrid;

    public event Action<PlacedCircuitComponent> ComponentPlaced;
    public event Action<PlacedCircuitComponent> ComponentRemoved;

    public void SetWireSystem(CircuitWireSystem system)
        => wireSystem = system;

    public void SetActiveBoard(CircuitBoardContext board)
    {
        EndPlacement();
        EndRemoval();

        activeBoard = board;
        activeGrid = null;

        if (activeBoard == null || activeBoard.Circuit == null)
            return;

        if (!placedComponentsByCircuit.TryGetValue(activeBoard.Circuit, out activeGrid))
        {
            activeGrid = new GridData(activeBoard.GridWidth, activeBoard.GridHeight);
            placedComponentsByCircuit.Add(activeBoard.Circuit, activeGrid);
        }

        RegisterAuthoredComponents();
    }

    public void ClearActiveBoard()
    {
        EndPlacement();
        EndRemoval();
        activeBoard = null;
        activeGrid = null;
    }

    public bool BeginPlacement(int componentId)
    {
        EndPlacement();
        EndRemoval();

        if (!HasActiveBoard)
        {
            Debug.LogWarning("Cannot place circuit components without an active circuit board.", this);
            return false;
        }

        if (preview == null)
        {
            Debug.LogError("Preview system is not assigned to ObjectPlacer.", this);
            return false;
        }

        if (!TryGetComponentData(componentId, out selectedComponent))
        {
            Debug.LogError($"Component with ID {componentId} was not found in ObjectPlacer's database.", this);
            return false;
        }

        selectedFootprint = GetRequiredFootprint(selectedComponent);

        if (selectedComponent.Prefab == null || selectedFootprint.x <= 0 || selectedFootprint.y <= 0)
        {
            Debug.LogError($"Component with ID {componentId} has an invalid prefab or footprint.", this);
            selectedComponent = null;
            return false;
        }

        GameObject previewInstance = preview.StartShowingPlacementPreview(selectedComponent.Prefab);

        if (previewInstance == null)
        {
            selectedComponent = null;
            return false;
        }

        previewInstance.transform.rotation = GetBoardRotation();
        if (!ScaleToFootprint(previewInstance, selectedFootprint))
        {
            Debug.LogWarning($"Could not fit the preview for component ID {componentId} to the board.", this);
            preview.StopShowingPreview();
            selectedComponent = null;
            return false;
        }

        placementStartedFrame = Time.frameCount;
        lastDetectedPosition = null;
        return true;
    }

    public void UpdatePlacementPreview(placementInputManager inputManager)
    {
        if (selectedComponent == null || inputManager == null || preview == null)
            return;

        if (!TryGetCircuitCell(
                inputManager,
                selectedFootprint,
                out Vector3Int gridPosition,
                out Vector3 footprintCenter))
        {
            preview.SetPreviewVisible(false);
            lastDetectedPosition = null;
            return;
        }

        preview.SetPreviewVisible(true);
        if (lastDetectedPosition.HasValue && lastDetectedPosition.Value == gridPosition)
            return;

        lastDetectedPosition = gridPosition;
        bool placementValidity = CanPlace(gridPosition, selectedFootprint);
        preview.UpdatePosition(footprintCenter, placementValidity);
        AlignToFootprint(preview.PreviewObject, footprintCenter);
    }

    public bool TryPlaceSelectedComponent(placementInputManager inputManager)
    {
        if (Time.frameCount == placementStartedFrame || selectedComponent == null ||
            inputManager == null || inputManager.IsPointerOverUI() || !HasActiveBoard)
            return false;

        if (!TryGetCircuitCell(
                inputManager,
                selectedFootprint,
                out Vector3Int gridPosition,
                out Vector3 footprintCenter) ||
            !CanPlace(gridPosition, selectedFootprint))
            return false;

        GameObject instance = Instantiate(
            selectedComponent.Prefab,
            footprintCenter,
            GetBoardRotation(),
            EnsureCircuitPlacementRoot());

        if (!ScaleToFootprint(instance, selectedFootprint))
        {
            Debug.LogWarning($"Could not fit component ID {selectedComponent.ID} to the board.", this);
            Destroy(instance);
            return false;
        }

        AlignToFootprint(instance, footprintCenter);

        PlacedCircuitComponent metadata = instance.GetComponent<PlacedCircuitComponent>();
        if (metadata == null)
            metadata = instance.AddComponent<PlacedCircuitComponent>();

        metadata.Initialize(selectedComponent.ID, gridPosition, selectedFootprint);
        if (!activeGrid.TryRegister(metadata))
        {
            Debug.LogWarning("The component footprint became occupied before it could be registered.", this);
            Destroy(instance);
            return false;
        }

        ComponentPlaced?.Invoke(metadata);

        return true;
    }

    public void EndPlacement()
    {
        selectedComponent = null;
        selectedFootprint = Vector2Int.zero;
        placementStartedFrame = -1;
        lastDetectedPosition = null;

        if (preview != null)
            preview.StopShowingPreview();
    }

    public bool BeginRemoval()
    {
        EndPlacement();
        EndRemoval();

        if (!HasActiveBoard)
        {
            Debug.LogWarning("Cannot remove circuit components without an active circuit board.", this);
            return false;
        }

        if (preview == null)
        {
            Debug.LogError("Preview system is not assigned to ObjectPlacer.", this);
            return false;
        }

        return true;
    }

    public void UpdateRemovalPreview(placementInputManager inputManager)
    {
        if (!HasActiveBoard || inputManager == null || preview == null)
            return;

        if (!TryGetCircuitCell(
                inputManager,
                Vector2Int.one,
                out Vector3Int gridPosition,
                out _) ||
            !activeGrid.TryGetPlacement(gridPosition, out PlacedCircuitComponent component))
        {
            ClearRemovalPreview();
            return;
        }

        if (hoveredRemovalComponent == component && preview.PreviewObject != null)
            return;

        ClearRemovalPreview();
        hoveredRemovalComponent = component;

        if (!TryGetComponentData(component.ComponentId, out componentData data) || data.Prefab == null)
        {
            Debug.LogWarning(
                $"Cannot show a removal preview for component ID {component.ComponentId}; its database entry is missing.",
                component);
            return;
        }

        GameObject previewInstance = preview.StartShowingRemovalPreview(data.Prefab);
        previewInstance.transform.rotation = GetBoardRotation();
        if (!ScaleToFootprint(previewInstance, component.FootprintSize))
        {
            preview.StopShowingPreview();
            return;
        }

        Vector3 footprintCenter = GetFootprintCenter(component.GridPosition, component.FootprintSize);
        preview.UpdatePosition(footprintCenter, false);
        AlignToFootprint(previewInstance, footprintCenter);
    }

    public bool TryRemoveSelectedComponent(placementInputManager inputManager)
    {
        if (!HasActiveBoard || inputManager == null || inputManager.IsPointerOverUI())
            return false;

        if (!TryGetCircuitCell(
                inputManager,
                Vector2Int.one,
                out Vector3Int gridPosition,
                out _) ||
            !activeGrid.TryRemove(gridPosition, out PlacedCircuitComponent component))
            return false;

        ClearRemovalPreview();
        if (component != null)
        {
            wireSystem?.RemoveConnections(component);
            ComponentRemoved?.Invoke(component);
            Destroy(component.gameObject);
        }

        return true;
    }

    public void EndRemoval()
    {
        ClearRemovalPreview();
    }

    private void RegisterAuthoredComponents()
    {
        Transform placementRoot = activeBoard.Circuit.transform.Find(PlacementRootName);
        if (placementRoot == null)
            return;

        PlacedCircuitComponent[] authoredComponents =
            placementRoot.GetComponentsInChildren<PlacedCircuitComponent>(true);

        for (int i = 0; i < authoredComponents.Length; i++)
        {
            PlacedCircuitComponent component = authoredComponents[i];
            if (!TryGetComponentData(component.ComponentId, out componentData data))
            {
                Debug.LogWarning(
                    $"Skipping authored component '{component.name}': component ID {component.ComponentId} is not in the database.",
                    component);
                continue;
            }

            Vector2Int requiredFootprint = GetRequiredFootprint(data);
            if (component.FootprintSize != requiredFootprint)
            {
                if (component.ComponentId == 4)
                {
                    component.Initialize(component.ComponentId, component.GridPosition, requiredFootprint);
                }
                else
                {
                    Debug.LogWarning(
                        $"Skipping authored component '{component.name}': its footprint does not match component ID {component.ComponentId}.",
                        component);
                    continue;
                }
            }

            if (!activeGrid.TryRegister(component))
            {
                Debug.LogWarning(
                    $"Skipping authored component '{component.name}': its footprint is invalid, outside the board, or overlapping.",
                    component);
            }
            else
            {
                component.ConfigureNodes();
            }
        }
    }

    private bool TryGetComponentData(int componentId, out componentData data)
    {
        data = null;
        if (componentDatabase == null || componentDatabase.componentDataList == null)
            return false;

        data = componentDatabase.componentDataList.Find(candidate => candidate.ID == componentId);
        return data != null;
    }

    private static Vector2Int GetRequiredFootprint(componentData data)
        // Grid x is horizontal and grid y is vertical on the circuit board.
        // The battery always occupies one column and two vertical cells.
        => data != null && data.ID == 4 ? new Vector2Int(1, 2) : data?.Size ?? Vector2Int.zero;

    private bool TryGetCircuitCell(
        placementInputManager inputManager,
        Vector2Int footprintSize,
        out Vector3Int gridPosition,
        out Vector3 footprintCenter)
    {
        gridPosition = default;
        footprintCenter = default;

        if (!HasActiveBoard ||
            !activeBoard.TryGetPointerCell(inputManager, out gridPosition, out _))
            return false;

        if (gridPosition.x + footprintSize.x > activeBoard.GridWidth ||
            gridPosition.y + footprintSize.y > activeBoard.GridHeight)
            return false;

        footprintCenter = GetFootprintCenter(gridPosition, footprintSize);
        return true;
    }

    private Vector3 GetFootprintCenter(Vector3Int gridPosition, Vector2Int footprintSize)
        => activeBoard.GetFootprintCenter(gridPosition, footprintSize);

    private bool CanPlace(Vector3Int gridPosition, Vector2Int footprintSize)
        => activeGrid.CanPlace(gridPosition, footprintSize) &&
            (wireSystem == null || wireSystem.IsFootprintClear(gridPosition, footprintSize));

    private bool ScaleToFootprint(GameObject instance, Vector2Int footprintSize)
    {
        if (!TryGetProjectedBounds(instance, out Vector3 min, out Vector3 max))
            return false;

        float width = Mathf.Max(max.x - min.x, 0.0001f);
        float height = Mathf.Max(max.y - min.y, 0.0001f);
        float scale = Mathf.Min(
            activeBoard.CellWorldWidth * footprintSize.x / width,
            activeBoard.CellWorldHeight * footprintSize.y / height) * componentCellPadding;

        if (!float.IsFinite(scale) || scale <= 0f)
            return false;

        instance.transform.localScale *= scale;
        return true;
    }

    private void AlignToFootprint(GameObject instance, Vector3 footprintCenter)
    {
        if (instance == null || !TryGetProjectedBounds(instance, out Vector3 min, out Vector3 max))
            return;

        Vector3 currentCenter = new((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f, min.z);
        Vector3 targetCenter = new(
            Vector3.Dot(footprintCenter, activeBoard.GridRight),
            Vector3.Dot(footprintCenter, activeBoard.GridForward),
            Vector3.Dot(footprintCenter, activeBoard.GridNormal));

        instance.transform.position += activeBoard.GridRight * (targetCenter.x - currentCenter.x)
            + activeBoard.GridForward * (targetCenter.y - currentCenter.y)
            + activeBoard.GridNormal * (targetCenter.z - currentCenter.z);
    }

    private bool TryGetProjectedBounds(GameObject instance, out Vector3 min, out Vector3 max)
    {
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
            return TryProjectBounds(renderers, renderer => renderer.bounds, out min, out max);

        Collider[] colliders = instance.GetComponentsInChildren<Collider>();
        return TryProjectBounds(colliders, collider => collider.bounds, out min, out max);
    }

    private bool TryProjectBounds<T>(T[] sources, System.Func<T, Bounds> getBounds, out Vector3 min, out Vector3 max)
    {
        min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

        foreach (T source in sources)
        {
            Bounds bounds = getBounds(source);
            Vector3 center = new(
                Vector3.Dot(bounds.center, activeBoard.GridRight),
                Vector3.Dot(bounds.center, activeBoard.GridForward),
                Vector3.Dot(bounds.center, activeBoard.GridNormal));
            Vector3 radius = new(
                Vector3.Dot(Abs(activeBoard.GridRight), bounds.extents),
                Vector3.Dot(Abs(activeBoard.GridForward), bounds.extents),
                Vector3.Dot(Abs(activeBoard.GridNormal), bounds.extents));

            min = Vector3.Min(min, center - radius);
            max = Vector3.Max(max, center + radius);
        }

        return sources.Length > 0;
    }

    private Transform EnsureCircuitPlacementRoot()
    {
        Transform placementRoot = activeBoard.Circuit.transform.Find(PlacementRootName);
        if (placementRoot != null)
            return placementRoot;

        placementRoot = new GameObject(PlacementRootName).transform;
        placementRoot.SetParent(activeBoard.Circuit.transform, false);
        return placementRoot;
    }

    private Quaternion GetBoardRotation()
        => Quaternion.LookRotation(activeBoard.GridForward, activeBoard.GridNormal);

    private void ClearRemovalPreview()
    {
        hoveredRemovalComponent = null;
        if (preview != null)
            preview.StopShowingPreview();
    }

    private static Vector3 Abs(Vector3 value)
        => new(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
}
