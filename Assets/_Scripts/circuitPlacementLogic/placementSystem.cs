using System.Collections.Generic;
using UnityEngine;

public class placementSystem : MonoBehaviour
{
    private const int CircuitGridSize = 16;
    private const string PlacementRootName = "Placed Circuit Components";
    private const string TileGridName = "TileGrid";
    private const float GridSurfaceOffset = 0.002f;
    private static readonly int GridSizeProperty = Shader.PropertyToID("_Size");

    [SerializeField]
    private placementInputManager inputManager;

    [SerializeField]
    private componentDatabaseSO componentDatabase;

    [SerializeField, Range(0.1f, 1f)]
    private float componentCellPadding = 0.95f;

    private readonly Dictionary<SpecialPrefabInteractable, GridData> circuitGrids = new();
    private int selectedComponentIndex = -1;
    private int placementStartedFrame = -1;

    private SpecialPrefabInteractable activeCircuit;
    private Transform activeTileGrid;
    private Bounds activeTileGridBounds;
    private Transform circuitPlacementRoot;
    private Plane circuitPlacementPlane;
    private Vector3 gridRight;
    private Vector3 gridForward;
    private Vector3 gridNormal;
    private float cellWorldWidth;
    private float cellWorldHeight;
    private MaterialPropertyBlock tileGridPropertyBlock;

    private void OnDisable()
    {
        StopPlacement();
    }

    public void SetActiveCircuit(SpecialPrefabInteractable circuit)
    {
        StopPlacement();
        activeCircuit = circuit;
        activeTileGrid = null;
        circuitPlacementRoot = null;

        if (activeCircuit != null && !circuitGrids.ContainsKey(activeCircuit))
            circuitGrids.Add(activeCircuit, new GridData());

        ConfigureCircuitGrid();
    }

    public void ClearActiveCircuit()
    {
        StopPlacement();
        activeCircuit = null;
        activeTileGrid = null;
        circuitPlacementRoot = null;
    }

    public void StartPlacement(int ID)
    {
        StopPlacement();

        if (activeCircuit == null)
        {
            Debug.LogWarning("Cannot place circuit components without inspecting a circuit.", this);
            return;
        }

        if (inputManager == null)
        {
            Debug.LogError("Placement input manager is not assigned.", this);
            return;
        }

        if (componentDatabase == null || componentDatabase.componentDataList == null)
        {
            Debug.LogError("Component database is not assigned.", this);
            return;
        }

        selectedComponentIndex = componentDatabase.componentDataList.FindIndex(data => data.ID == ID);
        if (selectedComponentIndex < 0)
        {
            Debug.LogError($"Component with ID {ID} not found in database.", this);
            return;
        }

        componentData selectedComponent = componentDatabase.componentDataList[selectedComponentIndex];
        if (selectedComponent.Prefab == null)
        {
            Debug.LogError($"Component with ID {ID} has no prefab.", this);
            selectedComponentIndex = -1;
            return;
        }

        if (selectedComponent.Size.x <= 0 || selectedComponent.Size.y <= 0)
        {
            Debug.LogError($"Component with ID {ID} has an invalid grid size.", this);
            selectedComponentIndex = -1;
            return;
        }

        if (!ConfigureCircuitGrid())
        {
            selectedComponentIndex = -1;
            return;
        }

        placementStartedFrame = Time.frameCount;
        inputManager.OnClicked += PlaceComponent;
        inputManager.OnExit += StopPlacement;
    }

    private void PlaceComponent()
    {
        // Ignore the UI click that selected the component.
        if (Time.frameCount == placementStartedFrame || selectedComponentIndex < 0 || activeCircuit == null)
            return;

        componentData selectedComponent = componentDatabase.componentDataList[selectedComponentIndex];
        if (!TryGetCircuitCell(selectedComponent.Size, out Vector3Int gridPosition, out Vector3 footprintCenter))
            return;

        GridData gridData = circuitGrids[activeCircuit];
        if (!gridData.CanPlace(gridPosition, selectedComponent.Size))
            return;

        GameObject instance = Instantiate(
            selectedComponent.Prefab,
            footprintCenter,
            Quaternion.LookRotation(gridForward, gridNormal),
            EnsureCircuitPlacementRoot());

        FitToFootprint(instance, selectedComponent.Size, footprintCenter);

        gridData.TryAdd(gridPosition, selectedComponent.Size);
        StopPlacement();
    }

    private void StopPlacement()
    {
        selectedComponentIndex = -1;
        placementStartedFrame = -1;

        if (inputManager == null)
            return;

        inputManager.OnClicked -= PlaceComponent;
        inputManager.OnExit -= StopPlacement;
    }

    private bool ConfigureCircuitGrid()
    {
        if (activeCircuit == null)
            return false;

        activeTileGrid = activeCircuit.transform.Find(TileGridName);
        if (activeTileGrid == null)
        {
            Debug.LogWarning("Inspected circuit has no direct TileGrid child. Cannot place circuit components.", activeCircuit);
            return false;
        }

        MeshFilter tileGridMesh = activeTileGrid.GetComponent<MeshFilter>();
        if (tileGridMesh == null || tileGridMesh.sharedMesh == null)
        {
            Debug.LogWarning("TileGrid has no mesh. Cannot place circuit components.", activeTileGrid);
            return false;
        }

        activeTileGridBounds = tileGridMesh.sharedMesh.bounds;
        if (activeTileGridBounds.size.x <= 0.0001f || activeTileGridBounds.size.z <= 0.0001f)
        {
            Debug.LogWarning("TileGrid mesh has invalid dimensions.", activeTileGrid);
            return false;
        }

        gridRight = activeTileGrid.right.normalized;
        gridForward = activeTileGrid.forward.normalized;
        gridNormal = activeTileGrid.up.normalized;

        float localCellWidth = activeTileGridBounds.size.x / CircuitGridSize;
        float localCellHeight = activeTileGridBounds.size.z / CircuitGridSize;
        cellWorldWidth = activeTileGrid.TransformVector(Vector3.right * localCellWidth).magnitude;
        cellWorldHeight = activeTileGrid.TransformVector(Vector3.forward * localCellHeight).magnitude;

        Vector3 preferredNormal = activeCircuit.GetPreferredViewDirection();
        if (preferredNormal.sqrMagnitude > 0.01f && Vector3.Dot(gridNormal, preferredNormal.normalized) < 0f)
            gridNormal = -gridNormal;

        circuitPlacementPlane = new Plane(gridNormal, activeTileGrid.TransformPoint(activeTileGridBounds.center));

        Renderer tileGridRenderer = activeTileGrid.GetComponent<Renderer>();
        if (tileGridRenderer != null)
        {
            tileGridPropertyBlock ??= new MaterialPropertyBlock();
            tileGridRenderer.GetPropertyBlock(tileGridPropertyBlock);
            tileGridPropertyBlock.SetVector(GridSizeProperty, new Vector4(CircuitGridSize, CircuitGridSize, 0f, 0f));
            tileGridRenderer.SetPropertyBlock(tileGridPropertyBlock);
        }

        return true;
    }

    private void FitToFootprint(GameObject instance, Vector2Int footprintSize, Vector3 footprintCenter)
    {
        if (!TryGetProjectedBounds(instance, out Vector3 min, out Vector3 max))
            return;

        float width = Mathf.Max(max.x - min.x, 0.0001f);
        float height = Mathf.Max(max.y - min.y, 0.0001f);
        float scale = Mathf.Min(
            cellWorldWidth * footprintSize.x / width,
            cellWorldHeight * footprintSize.y / height) * componentCellPadding;

        if (!float.IsFinite(scale) || scale <= 0f)
            return;

        instance.transform.localScale *= scale;
        if (!TryGetProjectedBounds(instance, out min, out max))
            return;

        Vector3 currentCenter = new((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f, min.z);
        Vector3 targetCenter = new(
            Vector3.Dot(footprintCenter, gridRight),
            Vector3.Dot(footprintCenter, gridForward),
            Vector3.Dot(footprintCenter, gridNormal));

        instance.transform.position += gridRight * (targetCenter.x - currentCenter.x)
            + gridForward * (targetCenter.y - currentCenter.y)
            + gridNormal * (targetCenter.z - currentCenter.z);
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
                Vector3.Dot(bounds.center, gridRight),
                Vector3.Dot(bounds.center, gridForward),
                Vector3.Dot(bounds.center, gridNormal));
            Vector3 radius = new(
                Vector3.Dot(Abs(gridRight), bounds.extents),
                Vector3.Dot(Abs(gridForward), bounds.extents),
                Vector3.Dot(Abs(gridNormal), bounds.extents));

            min = Vector3.Min(min, center - radius);
            max = Vector3.Max(max, center + radius);
        }

        return sources.Length > 0;
    }

    private static Vector3 Abs(Vector3 value)
        => new(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));

    private Transform EnsureCircuitPlacementRoot()
    {
        if (circuitPlacementRoot != null)
            return circuitPlacementRoot;

        circuitPlacementRoot = activeCircuit.transform.Find(PlacementRootName);
        if (circuitPlacementRoot != null)
            return circuitPlacementRoot;

        circuitPlacementRoot = new GameObject(PlacementRootName).transform;
        circuitPlacementRoot.SetParent(activeCircuit.transform, false);
        return circuitPlacementRoot;
    }

    private bool TryGetCircuitCell(Vector2Int footprintSize, out Vector3Int gridPosition, out Vector3 footprintCenter)
    {
        gridPosition = default;
        footprintCenter = default;

        if (activeTileGrid == null || inputManager == null ||
            !inputManager.TryGetSelectedPositionOnPlane(circuitPlacementPlane, out Vector3 pointerPosition))
            return false;

        Vector3 localPosition = activeTileGrid.InverseTransformPoint(pointerPosition);
        if (localPosition.x < activeTileGridBounds.min.x || localPosition.x > activeTileGridBounds.max.x ||
            localPosition.z < activeTileGridBounds.min.z || localPosition.z > activeTileGridBounds.max.z)
            return false;

        float cellWidth = activeTileGridBounds.size.x / CircuitGridSize;
        float cellHeight = activeTileGridBounds.size.z / CircuitGridSize;
        gridPosition = new Vector3Int(
            Mathf.Min(Mathf.FloorToInt((localPosition.x - activeTileGridBounds.min.x) / cellWidth), CircuitGridSize - 1),
            Mathf.Min(Mathf.FloorToInt((localPosition.z - activeTileGridBounds.min.z) / cellHeight), CircuitGridSize - 1),
            0);

        if (gridPosition.x + footprintSize.x > CircuitGridSize ||
            gridPosition.y + footprintSize.y > CircuitGridSize)
            return false;

        Vector3 localFootprintCenter = new(
            activeTileGridBounds.min.x + (gridPosition.x + footprintSize.x * 0.5f) * cellWidth,
            activeTileGridBounds.center.y,
            activeTileGridBounds.min.z + (gridPosition.y + footprintSize.y * 0.5f) * cellHeight);

        footprintCenter = activeTileGrid.TransformPoint(localFootprintCenter) + gridNormal * GridSurfaceOffset;
        return true;
    }
}
