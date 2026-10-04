using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Immutable neighborhood captured on the main thread for one mesh request.
/// Mesh workers use it instead of touching the live World dictionaries.
/// </summary>
public sealed class ChunkMeshContext
{
    private readonly Dictionary<Vector3Int, ChunkData> chunks;

    public ChunkData Center { get; }
    public int NeighbourVersionHash { get; }
    public bool CanUseColumnTerrain { get; }

    public ChunkMeshContext(
        ChunkData center,
        Dictionary<Vector3Int, ChunkData> neighborhood = null)
    {
        Center = center;
        chunks = neighborhood ?? new Dictionary<Vector3Int, ChunkData>();
        if (!chunks.ContainsKey(center.worldPosition))
            chunks.Add(center.worldPosition, center);
        bool columnTerrain = center.IsColumnTerrain;
        foreach (ChunkData data in chunks.Values)
            columnTerrain &= data.IsColumnTerrain;
        CanUseColumnTerrain = columnTerrain;

        unchecked
        {
            int hash = 17;
            foreach (KeyValuePair<Vector3Int, ChunkData> pair in chunks)
            {
                hash = hash * 31 + pair.Key.GetHashCode();
                hash = hash * 31 + pair.Value.ContentVersion;
            }
            NeighbourVersionHash = hash;
        }
    }

    public VoxelType GetVoxel(int localX, int localY, int localZ)
    {
        int size = Center.chunkSize;
        int height = Center.chunkHeight;
        if ((uint)localX < (uint)size && (uint)localY < (uint)height && (uint)localZ < (uint)size)
            return Center.voxels[localX + size * localY + size * height * localZ];

        // Distant surface chunks omit underground data; keep their underside
        // closed. Nearby chunks use the real vertical neighbor below.
        if (localY < 0 && Center.worldPosition.y <= 0)
        {
            int belowOffsetX = FloorDiv(localX, size);
            int belowOffsetZ = FloorDiv(localZ, size);
            Vector3Int columnKey = Center.worldPosition + new Vector3Int(
                belowOffsetX * size, 0, belowOffsetZ * size);
            Vector3Int belowKey = columnKey + Vector3Int.down * height;
            if (!chunks.ContainsKey(belowKey) && chunks.TryGetValue(columnKey, out ChunkData column))
            {
                int surfaceX = localX - belowOffsetX * size;
                int surfaceZ = localZ - belowOffsetZ * size;
                if ((uint)surfaceX < (uint)column.chunkSize &&
                    (uint)surfaceZ < (uint)column.chunkSize)
                    return column.voxels[surfaceX + column.chunkSize * column.chunkHeight * surfaceZ];
            }
        }

        int offsetX = FloorDiv(localX, size);
        int offsetY = FloorDiv(localY, height);
        int offsetZ = FloorDiv(localZ, size);
        var key = Center.worldPosition + new Vector3Int(
            offsetX * size,
            offsetY * height,
            offsetZ * size);
        if (!chunks.TryGetValue(key, out ChunkData data))
            return VoxelType.Air;

        int x = localX - offsetX * size;
        int y = localY - offsetY * height;
        int z = localZ - offsetZ * size;
        if (x < 0 || x >= data.chunkSize || y < 0 || y >= data.chunkHeight || z < 0 || z >= data.chunkSize)
            return VoxelType.Air;
        return data.voxels[x + data.chunkSize * y + data.chunkSize * data.chunkHeight * z];
    }

    public FarSurfaceColumn GetFarSurfaceColumn(int localX, int localZ)
    {
        int size = Center.chunkSize;
        if ((uint)localX < (uint)size && (uint)localZ < (uint)size)
            return Center.GetFarSurfaceColumn(localX, localZ);

        int offsetX = FloorDiv(localX, size);
        int offsetZ = FloorDiv(localZ, size);
        var key = Center.worldPosition + new Vector3Int(offsetX * size, 0, offsetZ * size);
        if (!chunks.TryGetValue(key, out ChunkData data))
            return new FarSurfaceColumn(-1, VoxelType.Air);

        FarSurfaceColumn column = data.GetFarSurfaceColumn(
            localX - offsetX * size,
            localZ - offsetZ * size);
        if (!column.HasSurface)
            return column;
        return new FarSurfaceColumn(column.topY + data.worldPosition.y - Center.worldPosition.y, column.material);
    }

    private static int FloorDiv(int value, int divisor)
    {
        if (value >= 0)
            return value / divisor;
        return -((-value + divisor - 1) / divisor);
    }
}
