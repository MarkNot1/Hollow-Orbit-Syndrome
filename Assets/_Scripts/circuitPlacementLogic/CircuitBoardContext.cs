using UnityEngine;

public sealed class CircuitBoardContext
{
    public SpecialPrefabInteractable Circuit { get; }
    public Transform TileGrid { get; }
    public Bounds TileGridBounds { get; }
    public Plane PlacementPlane { get; }
    public Vector3 GridRight { get; }
    public Vector3 GridForward { get; }
    public Vector3 GridNormal { get; }
    public float CellWorldWidth { get; }
    public float CellWorldHeight { get; }
    public int GridWidth { get; }
    public int GridHeight { get; }
    public float SurfaceOffset { get; }

    public CircuitBoardContext(
        SpecialPrefabInteractable circuit,
        Transform tileGrid,
        Bounds tileGridBounds,
        Plane placementPlane,
        Vector3 gridRight,
        Vector3 gridForward,
        Vector3 gridNormal,
        float cellWorldWidth,
        float cellWorldHeight,
        int gridWidth,
        int gridHeight,
        float surfaceOffset)
    {
        Circuit = circuit;
        TileGrid = tileGrid;
        TileGridBounds = tileGridBounds;
        PlacementPlane = placementPlane;
        GridRight = gridRight;
        GridForward = gridForward;
        GridNormal = gridNormal;
        CellWorldWidth = cellWorldWidth;
        CellWorldHeight = cellWorldHeight;
        GridWidth = gridWidth;
        GridHeight = gridHeight;
        SurfaceOffset = surfaceOffset;
    }

    public bool TryWorldToCell(Vector3 worldPosition, out Vector3Int cell)
    {
        cell = default;
        Vector3 local = TileGrid.InverseTransformPoint(worldPosition);
        if (local.x < TileGridBounds.min.x || local.x > TileGridBounds.max.x ||
            local.z < TileGridBounds.min.z || local.z > TileGridBounds.max.z)
            return false;

        cell = new Vector3Int(
            Mathf.Min(Mathf.FloorToInt((local.x - TileGridBounds.min.x) /
                (TileGridBounds.size.x / GridWidth)), GridWidth - 1),
            Mathf.Min(Mathf.FloorToInt((local.z - TileGridBounds.min.z) /
                (TileGridBounds.size.z / GridHeight)), GridHeight - 1),
            0);
        return IsInside(cell);
    }

    public bool TryGetPointerCell(
        placementInputManager inputManager,
        out Vector3Int cell,
        out Vector3 worldPosition)
    {
        cell = default;
        worldPosition = default;
        return inputManager != null &&
            inputManager.TryGetSelectedPositionOnPlane(PlacementPlane, out worldPosition) &&
            TryWorldToCell(worldPosition, out cell);
    }

    public bool IsInside(Vector3Int cell)
        => cell.x >= 0 && cell.y >= 0 && cell.x < GridWidth && cell.y < GridHeight;

    public Vector3 GetCellCenter(Vector3Int cell, float additionalOffset = 0f)
    {
        Vector3 localCenter = new(
            TileGridBounds.min.x + (cell.x + 0.5f) * TileGridBounds.size.x / GridWidth,
            TileGridBounds.center.y,
            TileGridBounds.min.z + (cell.y + 0.5f) * TileGridBounds.size.z / GridHeight);
        return TileGrid.TransformPoint(localCenter) + GridNormal * (SurfaceOffset + additionalOffset);
    }

    public Vector3 GetFootprintCenter(Vector3Int cell, Vector2Int size)
    {
        Vector3 localCenter = new(
            TileGridBounds.min.x + (cell.x + size.x * 0.5f) * TileGridBounds.size.x / GridWidth,
            TileGridBounds.center.y,
            TileGridBounds.min.z + (cell.y + size.y * 0.5f) * TileGridBounds.size.z / GridHeight);
        return TileGrid.TransformPoint(localCenter) + GridNormal * SurfaceOffset;
    }
}
