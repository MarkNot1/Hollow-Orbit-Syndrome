using System.Collections.Generic;
using UnityEngine;

public class GridData
{
    private readonly HashSet<Vector3Int> occupiedCells = new();

    public bool CanPlace(Vector3Int gridPosition, Vector2Int size)
    {
        if (size.x <= 0 || size.y <= 0)
            return false;

        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                if (occupiedCells.Contains(gridPosition + new Vector3Int(x, y, 0)))
                    return false;
            }
        }

        return true;
    }

    public bool TryAdd(Vector3Int gridPosition, Vector2Int size)
    {
        if (!CanPlace(gridPosition, size))
            return false;

        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
                occupiedCells.Add(gridPosition + new Vector3Int(x, y, 0));
        }

        return true;
    }
}
