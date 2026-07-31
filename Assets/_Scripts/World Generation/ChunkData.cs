using System.Threading;
using UnityEngine;

public readonly struct FarSurfaceColumn
{
    private readonly short encodedTopY;
    public readonly VoxelType material;
    public int topY => encodedTopY;

    public FarSurfaceColumn(int topY, VoxelType material)
    {
        encodedTopY = (short)Mathf.Clamp(topY, short.MinValue, short.MaxValue);
        this.material = material;
    }

    public bool HasSurface => topY >= 0;
}

public class ChunkData
{
    public VoxelType[] voxels;
    public int chunkSize = 16;
    public int chunkHeight = 100;
    public World worldReference;
    public Vector3Int worldPosition;
    private readonly FarSurfaceColumn[] farSurfaceColumns;
    private int minimumSolidY;
    private int maximumSolidY;
    public bool IsColumnTerrain { get; private set; }

    public bool modifiedByThePlayer = false;
    private int contentVersion;

    public int ContentVersion => Volatile.Read(ref contentVersion);
    public int MinimumSolidY => minimumSolidY;
    public int MaximumSolidY => maximumSolidY;

    public ChunkData(int chunkSize, int chunkHeight, World world, Vector3Int worldPosition)
    {
        this.chunkHeight = chunkHeight;
        this.chunkSize = chunkSize;
        this.worldReference = world;
        this.worldPosition = worldPosition;
        voxels = new VoxelType[chunkSize * chunkHeight * chunkSize];
        farSurfaceColumns = new FarSurfaceColumn[chunkSize * chunkSize];
        minimumSolidY = chunkHeight;
        maximumSolidY = -1;
    }

    /// <summary>
    /// Marks freshly generated voxel data as ready without allocating a duplicate
    /// full-resolution array or a coarse mip chain.
    /// </summary>
    public void MarkGenerated()
    {
        IsColumnTerrain = false;
        RebuildSurfaceData();
        Interlocked.Increment(ref contentVersion);
    }

    /// <summary>
    /// Fast generation path for terrain producers that already know each
    /// column's surface while filling the voxel array.
    /// </summary>
    public void SetGeneratedSurfaceColumn(int x, int z, int topY, VoxelType material)
    {
        farSurfaceColumns[x + chunkSize * z] = new FarSurfaceColumn(topY, material);
        if (topY < 0)
            return;
        minimumSolidY = 0;
        maximumSolidY = Mathf.Max(maximumSolidY, topY);
    }

    public void MarkGeneratedFromSurfaceColumns()
    {
        IsColumnTerrain = true;
        Interlocked.Increment(ref contentVersion);
    }

    public void SetVoxel(int x, int y, int z, VoxelType value)
    {
        int index = x + chunkSize * y + chunkSize * chunkHeight * z;
        voxels[index] = value;
        IsColumnTerrain = false;
        RebuildSurfaceColumn(x, z);
        if (VoxelRuntimeData.IsSolid(value))
        {
            minimumSolidY = Mathf.Min(minimumSolidY, y);
            maximumSolidY = Mathf.Max(maximumSolidY, y);
        }
        else if (y == minimumSolidY || y == maximumSolidY)
        {
            RebuildSolidBounds();
        }
        Interlocked.Increment(ref contentVersion);
    }

    public FarSurfaceColumn GetFarSurfaceColumn(int x, int z)
    {
        if (x < 0 || x >= chunkSize || z < 0 || z >= chunkSize)
            return new FarSurfaceColumn(-1, VoxelType.Air);
        return farSurfaceColumns[x + chunkSize * z];
    }

    private void RebuildSurfaceData()
    {
        minimumSolidY = chunkHeight;
        maximumSolidY = -1;
        for (int z = 0; z < chunkSize; z++)
        {
            for (int x = 0; x < chunkSize; x++)
            {
                RebuildSurfaceColumn(x, z);
                FarSurfaceColumn column = farSurfaceColumns[x + chunkSize * z];
                if (!column.HasSurface)
                    continue;
                maximumSolidY = Mathf.Max(maximumSolidY, column.topY);
            }
        }

        // Terrain can contain caves, so the lowest solid cannot be inferred from
        // the surface-only cache.
        for (int y = 0; y <= maximumSolidY; y++)
        {
            bool found = false;
            for (int z = 0; z < chunkSize && !found; z++)
            {
                int row = chunkSize * y + chunkSize * chunkHeight * z;
                for (int x = 0; x < chunkSize; x++)
                {
                    if (!VoxelRuntimeData.IsSolid(voxels[row + x]))
                        continue;
                    minimumSolidY = y;
                    found = true;
                    break;
                }
            }
            if (found)
                break;
        }
    }

    private void RebuildSurfaceColumn(int x, int z)
    {
        int columnOffset = x + chunkSize * chunkHeight * z;
        for (int y = chunkHeight - 1; y >= 0; y--)
        {
            VoxelType type = voxels[columnOffset + chunkSize * y];
            if (!VoxelRuntimeData.IsSolid(type))
                continue;
            farSurfaceColumns[x + chunkSize * z] = new FarSurfaceColumn(y, type);
            return;
        }
        farSurfaceColumns[x + chunkSize * z] = new FarSurfaceColumn(-1, VoxelType.Air);
    }

    private void RebuildSolidBounds()
    {
        minimumSolidY = chunkHeight;
        maximumSolidY = -1;
        for (int z = 0; z < chunkSize; z++)
        {
            for (int y = 0; y < chunkHeight; y++)
            {
                int row = chunkSize * y + chunkSize * chunkHeight * z;
                for (int x = 0; x < chunkSize; x++)
                {
                    if (!VoxelRuntimeData.IsSolid(voxels[row + x]))
                        continue;
                    minimumSolidY = Mathf.Min(minimumSolidY, y);
                    maximumSolidY = Mathf.Max(maximumSolidY, y);
                }
            }
        }
    }
}
