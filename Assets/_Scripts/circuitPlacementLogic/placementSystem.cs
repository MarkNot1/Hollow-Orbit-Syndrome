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
    private int selectedComponentID = -1;

    [SerializeField]
    private float componentCellPadding = 0.95f;

    private SpecialPrefabInteractable activeCircuit;
    private Transform activeTileGrid;
    private MeshFilter activeTileGridMesh;
    private Bounds activeTileGridBounds;
    private Transform circuitPlacementRoot;
    private Plane circuitPlacementPlane;
    private Vector3 gridRight;
    private Vector3 gridForward;
    private Vector3 gridNormal;
    private float activeCellWidth = 1f;
    private float activeCellHeight = 1f;
    private int placementStartedFrame = -1;
    private MaterialPropertyBlock tileGridPropertyBlock;

    private void Start()
    {
        StopPlacement();
    }

    private void OnDisable()
    {
        StopPlacement();
    }

    public void SetActiveCircuit(SpecialPrefabInteractable circuit)
    {
        activeCircuit = circuit;
        activeTileGrid = null;
        activeTileGridMesh = null;
        circuitPlacementRoot = null;
        ConfigureCircuitGrid();
    }

    public void ClearActiveCircuit()
    {
        StopPlacement();
        activeCircuit = null;
        activeTileGrid = null;
        activeTileGridMesh = null;
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

        if (componentDatabase == null || componentDatabase.componentDataList == null)
        {
            Debug.LogError("Component database is not assigned.", this);
            return;
        }

        selectedComponentID = componentDatabase.componentDataList.FindIndex(componentData => componentData.ID == ID);
        if (selectedComponentID < 0)
        {
            Debug.LogError($"Component with ID {ID} not found in database.");
            return;
        }

        if (!ConfigureCircuitGrid())
        {
            selectedComponentID = -1;
            return;
        }

        placementStartedFrame = Time.frameCount;
        inputManager.OnClicked += PlaceComponent;
        inputManager.OnExit += StopPlacement;
    }

    private void PlaceComponent()
    {
        if (Time.frameCount == placementStartedFrame)
            return;

        if (selectedComponentID < 0 || activeCircuit == null)
        {
            return;
        }

        if (!TryGetCircuitCell(out _, out Vector3 cellCenter))
            return;

        GameObject prefab = componentDatabase.componentDataList[selectedComponentID].Prefab;
        if (prefab == null)
            return;

        GameObject instance = Instantiate(prefab, cellCenter, GetComponentPlacementRotation(), EnsureCircuitPlacementRoot());
        FitInstanceToCell(instance, cellCenter);
    }

    private void StopPlacement()
    {
        selectedComponentID = -1;

        inputManager.OnClicked -= PlaceComponent;
        inputManager.OnExit -= StopPlacement;
    }

    private bool ConfigureCircuitGrid()
    {
        if (activeCircuit == null)
            return false;

        activeTileGrid = FindChildByName(activeCircuit.transform, TileGridName);
        if (activeTileGrid == null)
        {
            Debug.LogWarning("Inspected circuit has no TileGrid child. Cannot place circuit components.", activeCircuit);
            return false;
        }

        activeTileGridMesh = activeTileGrid.GetComponent<MeshFilter>();
        if (activeTileGridMesh == null || activeTileGridMesh.sharedMesh == null)
        {
            Debug.LogWarning("TileGrid has no mesh. Cannot place circuit components.", activeTileGrid);
            return false;
        }

        activeTileGridBounds = activeTileGridMesh.sharedMesh.bounds;
        gridRight = activeTileGrid.right.normalized;
        gridForward = activeTileGrid.forward.normalized;
        gridNormal = activeTileGrid.up.normalized;

        Vector3 preferredNormal = activeCircuit.GetPreferredViewDirection();
        if (preferredNormal.sqrMagnitude > 0.01f && Vector3.Dot(gridNormal, preferredNormal.normalized) < 0f)
            gridNormal = -gridNormal;

        float localCellWidth = activeTileGridBounds.size.x / CircuitGridSize;
        float localCellHeight = activeTileGridBounds.size.z / CircuitGridSize;
        activeCellWidth = activeTileGrid.TransformVector(Vector3.right * localCellWidth).magnitude;
        activeCellHeight = activeTileGrid.TransformVector(Vector3.forward * localCellHeight).magnitude;
        circuitPlacementPlane = new Plane(gridNormal, activeTileGrid.TransformPoint(activeTileGridBounds.center));
        ApplyTileGridMaterialProperties();
        return activeCellWidth > 0.0001f && activeCellHeight > 0.0001f;
    }

    private void ApplyTileGridMaterialProperties()
    {
        Renderer tileGridRenderer = activeTileGrid != null ? activeTileGrid.GetComponent<Renderer>() : null;
        if (tileGridRenderer == null)
            return;

        tileGridPropertyBlock ??= new MaterialPropertyBlock();
        tileGridRenderer.GetPropertyBlock(tileGridPropertyBlock);
        tileGridPropertyBlock.SetVector(GridSizeProperty, new Vector4(CircuitGridSize, CircuitGridSize, 0f, 0f));
        tileGridRenderer.SetPropertyBlock(tileGridPropertyBlock);
    }

    private static Transform FindChildByName(Transform parent, string childName)
    {
        if (parent == null)
            return null;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == childName)
                return child;

            Transform nested = FindChildByName(child, childName);
            if (nested != null)
                return nested;
        }

        return null;
    }

    private Transform EnsureCircuitPlacementRoot()
    {
        if (circuitPlacementRoot != null)
            return circuitPlacementRoot;

        Transform existing = activeCircuit.transform.Find(PlacementRootName);
        if (existing != null)
        {
            circuitPlacementRoot = existing;
            return circuitPlacementRoot;
        }

        GameObject root = new GameObject(PlacementRootName);
        circuitPlacementRoot = root.transform;
        circuitPlacementRoot.SetParent(activeCircuit.transform, false);
        return circuitPlacementRoot;
    }

    private bool TryGetCircuitCell(out Vector3Int gridPosition, out Vector3 cellCenter)
    {
        gridPosition = default;
        cellCenter = default;

        if (activeTileGrid == null || activeTileGridMesh == null || inputManager == null)
            return false;

        if (!inputManager.TryGetSelectedPositionOnPlane(circuitPlacementPlane, out Vector3 mousePos))
            return false;

        Vector3 localPosition = activeTileGrid.InverseTransformPoint(mousePos);
        if (localPosition.x < activeTileGridBounds.min.x || localPosition.x > activeTileGridBounds.max.x ||
            localPosition.z < activeTileGridBounds.min.z || localPosition.z > activeTileGridBounds.max.z)
            return false;

        float localCellWidth = activeTileGridBounds.size.x / CircuitGridSize;
        float localCellHeight = activeTileGridBounds.size.z / CircuitGridSize;
        gridPosition = new Vector3Int(
            Mathf.FloorToInt((localPosition.x - activeTileGridBounds.min.x) / localCellWidth),
            Mathf.FloorToInt((localPosition.z - activeTileGridBounds.min.z) / localCellHeight),
            0);
        gridPosition.x = Mathf.Clamp(gridPosition.x, 0, CircuitGridSize - 1);
        gridPosition.y = Mathf.Clamp(gridPosition.y, 0, CircuitGridSize - 1);

        Vector3 localCellCenter = new Vector3(
            activeTileGridBounds.min.x + (gridPosition.x + 0.5f) * localCellWidth,
            activeTileGridBounds.center.y,
            activeTileGridBounds.min.z + (gridPosition.y + 0.5f) * localCellHeight);

        cellCenter = activeTileGrid.TransformPoint(localCellCenter) + gridNormal * GridSurfaceOffset;
        return true;
    }

    private void FitInstanceToCell(GameObject instance, Vector3 cellCenter)
    {
        if (instance == null || activeTileGrid == null)
            return;

        if (!TryGetProjectedBounds(instance, gridRight, gridForward, gridNormal, null, out ProjectedBounds bounds))
            return;

        float width = Mathf.Max(0.0001f, bounds.MaxX - bounds.MinX);
        float height = Mathf.Max(0.0001f, bounds.MaxY - bounds.MinY);
        float scale = Mathf.Min(activeCellWidth / width, activeCellHeight / height) * Mathf.Clamp01(componentCellPadding);
        if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0f)
            return;

        instance.transform.localScale *= scale;

        if (!TryGetProjectedBounds(instance, gridRight, gridForward, gridNormal, null, out bounds))
            return;

        float targetX = Vector3.Dot(cellCenter, gridRight);
        float targetY = Vector3.Dot(cellCenter, gridForward);
        float targetZ = Vector3.Dot(cellCenter, gridNormal);
        float currentX = (bounds.MinX + bounds.MaxX) * 0.5f;
        float currentY = (bounds.MinY + bounds.MaxY) * 0.5f;

        instance.transform.position += gridRight * (targetX - currentX)
            + gridForward * (targetY - currentY)
            + gridNormal * (targetZ - bounds.MinZ);
    }

    private Quaternion GetComponentPlacementRotation()
    {
        return Quaternion.LookRotation(gridForward, gridNormal);
    }

    private static bool TryGetProjectedBounds(
        GameObject target,
        Vector3 axisX,
        Vector3 axisY,
        Vector3 axisZ,
        Transform ignoredParent,
        out ProjectedBounds projectedBounds)
    {
        projectedBounds = default;
        bool hasBounds = false;

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            if (ShouldIgnore(renderers[i].transform, ignoredParent))
                continue;

            EncapsulateProjectedBounds(renderers[i].bounds, axisX, axisY, axisZ, ref projectedBounds, ref hasBounds);
        }

        if (hasBounds)
            return true;

        Collider[] colliders = target.GetComponentsInChildren<Collider>();
        for (int i = 0; i < colliders.Length; i++)
        {
            if (ShouldIgnore(colliders[i].transform, ignoredParent))
                continue;

            EncapsulateProjectedBounds(colliders[i].bounds, axisX, axisY, axisZ, ref projectedBounds, ref hasBounds);
        }

        return hasBounds;
    }

    private static bool ShouldIgnore(Transform transformToCheck, Transform ignoredParent)
    {
        return ignoredParent != null && transformToCheck.IsChildOf(ignoredParent);
    }

    private static void EncapsulateProjectedBounds(
        Bounds bounds,
        Vector3 axisX,
        Vector3 axisY,
        Vector3 axisZ,
        ref ProjectedBounds projectedBounds,
        ref bool hasBounds)
    {
        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;

        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 point = center + Vector3.Scale(extents, new Vector3(x, y, z));
                    projectedBounds.Encapsulate(
                        Vector3.Dot(point, axisX),
                        Vector3.Dot(point, axisY),
                        Vector3.Dot(point, axisZ),
                        ref hasBounds);
                }
            }
        }
    }

    private struct ProjectedBounds
    {
        public float MinX;
        public float MaxX;
        public float MinY;
        public float MaxY;
        public float MinZ;
        public float MaxZ;

        public void Encapsulate(float x, float y, float z, ref bool hasBounds)
        {
            if (!hasBounds)
            {
                MinX = MaxX = x;
                MinY = MaxY = y;
                MinZ = MaxZ = z;
                hasBounds = true;
                return;
            }

            MinX = Mathf.Min(MinX, x);
            MaxX = Mathf.Max(MaxX, x);
            MinY = Mathf.Min(MinY, y);
            MaxY = Mathf.Max(MaxY, y);
            MinZ = Mathf.Min(MinZ, z);
            MaxZ = Mathf.Max(MaxZ, z);
        }
    }
}
