using System.Collections.Generic;
using UnityEngine;

public class GridData
{
    private readonly Dictionary<Vector3Int, PlacedCircuitComponent> placedComponents = new();
    private readonly int width;
    private readonly int height;

    public IReadOnlyDictionary<Vector3Int, PlacedCircuitComponent> PlacedComponents => placedComponents;
    public int Width => width;
    public int Height => height;

    public GridData(int width, int height)
    {
        this.width = Mathf.Max(1, width);
        this.height = Mathf.Max(1, height);
    }

    public bool CanPlace(Vector3Int gridPosition, Vector2Int size)
    {
        if (!IsFootprintInsideGrid(gridPosition, size))
            return false;

        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                Vector3Int cell = gridPosition + new Vector3Int(x, y, 0);
                if (placedComponents.TryGetValue(cell, out PlacedCircuitComponent component) && component != null)
                    return false;
            }
        }

        return true;
    }

    public bool TryRegister(PlacedCircuitComponent component)
    {
        if (component == null || !IsFootprintInsideGrid(component.GridPosition, component.FootprintSize))
            return false;

        for (int x = 0; x < component.FootprintSize.x; x++)
        {
            for (int y = 0; y < component.FootprintSize.y; y++)
            {
                Vector3Int cell = component.GridPosition + new Vector3Int(x, y, 0);
                if (placedComponents.TryGetValue(cell, out PlacedCircuitComponent existing) &&
                    existing != null && existing != component)
                    return false;
            }
        }

        for (int x = 0; x < component.FootprintSize.x; x++)
        {
            for (int y = 0; y < component.FootprintSize.y; y++)
            {
                Vector3Int cell = component.GridPosition + new Vector3Int(x, y, 0);
                placedComponents[cell] = component;
            }
        }

        return true;
    }

    public bool TryGetPlacement(Vector3Int gridPosition, out PlacedCircuitComponent component)
    {
        if (placedComponents.TryGetValue(gridPosition, out component) && component != null)
            return true;

        placedComponents.Remove(gridPosition);
        component = null;
        return false;
    }

    public bool IsOccupied(Vector3Int gridPosition)
        => TryGetPlacement(gridPosition, out _);

    public bool TryRemove(Vector3Int gridPosition, out PlacedCircuitComponent component)
    {
        if (!TryGetPlacement(gridPosition, out component))
            return false;

        List<Vector3Int> cellsToRemove = new();
        foreach (KeyValuePair<Vector3Int, PlacedCircuitComponent> entry in placedComponents)
        {
            if (entry.Value == null || entry.Value == component)
                cellsToRemove.Add(entry.Key);
        }

        for (int i = 0; i < cellsToRemove.Count; i++)
            placedComponents.Remove(cellsToRemove[i]);

        return true;
    }

    private bool IsFootprintInsideGrid(Vector3Int gridPosition, Vector2Int size)
    {
        return size.x > 0 && size.y > 0 &&
            gridPosition.x >= 0 && gridPosition.y >= 0 &&
            gridPosition.x + size.x <= width &&
            gridPosition.y + size.y <= height;
    }
}
