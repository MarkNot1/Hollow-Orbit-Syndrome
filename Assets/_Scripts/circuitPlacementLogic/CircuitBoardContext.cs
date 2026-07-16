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
}
