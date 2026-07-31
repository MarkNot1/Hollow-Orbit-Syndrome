using System;
using System.Collections.Generic;
using UnityEngine;

public static class Chunk
{

    public static void LoopThroughTheVoxels(ChunkData chunkData, Action<int, int, int> actionToPerform)
    {
        for (int index = 0; index < chunkData.voxels.Length; index++)
        {
            var position = GetPostitionFromIndex(chunkData, index);
            actionToPerform(position.x, position.y, position.z);
        }
    }

    private static Vector3Int GetPostitionFromIndex(ChunkData chunkData, int index)
    {
        int x = index % chunkData.chunkSize;
        int y = (index / chunkData.chunkSize) % chunkData.chunkHeight;
        int z = index / (chunkData.chunkSize * chunkData.chunkHeight);
        return new Vector3Int(x, y, z);
    }

    //in chunk coordinate system
    private static bool InRange(ChunkData chunkData, int axisCoordinate)
    {
        if (axisCoordinate < 0 || axisCoordinate >= chunkData.chunkSize)
            return false;

        return true;
    }

    //in chunk coordinate system
    private static bool InRangeHeight(ChunkData chunkData, int ycoordinate)
    {
        if (ycoordinate < 0 || ycoordinate >= chunkData.chunkHeight)
            return false;

        return true;
    }

    public static VoxelType GetVoxelFromChunkCoordinates(ChunkData chunkData, Vector3Int chunkCoordinates)
    {
        return GetVoxelFromChunkCoordinates(chunkData, chunkCoordinates.x, chunkCoordinates.y, chunkCoordinates.z);
    }

    public static VoxelType GetVoxelFromChunkCoordinates(ChunkData chunkData, int x, int y, int z)
    {
        if (InRange(chunkData, x) && InRangeHeight(chunkData, y) && InRange(chunkData, z))
        {
            int index = GetIndexFromPosition(chunkData, x, y, z);
            return chunkData.voxels[index];
        }

        return chunkData.worldReference.GetVoxelFromChunkCoordinates(chunkData, chunkData.worldPosition.x + x, chunkData.worldPosition.y + y, chunkData.worldPosition.z + z);
    }

    public static void SetVoxel(ChunkData chunkData, Vector3Int localPosition, VoxelType voxel)
    {
        if (InRange(chunkData, localPosition.x) && InRangeHeight(chunkData, localPosition.y) && InRange(chunkData, localPosition.z))
        {
            int index = GetIndexFromPosition(chunkData, localPosition.x, localPosition.y, localPosition.z);
            chunkData.SetVoxel(localPosition.x, localPosition.y, localPosition.z, voxel);
        }
        else
        {
            WorldDataHelper.SetVoxel(chunkData.worldReference, localPosition, voxel);
        }
    }

    private static int GetIndexFromPosition(ChunkData chunkData, int x, int y, int z)
    {
        return x + chunkData.chunkSize * y + chunkData.chunkSize * chunkData.chunkHeight * z;
    }

    public static Vector3Int GetVoxelInChunkCoordinates(ChunkData chunkData, Vector3Int pos)
    {
        return new Vector3Int
        {
            x = pos.x - chunkData.worldPosition.x,
            y = pos.y - chunkData.worldPosition.y,
            z = pos.z - chunkData.worldPosition.z
        };
    }

    public static MeshData GetChunkMeshData(ChunkData chunkData)
    {
        return GetChunkMeshData(new ChunkMeshContext(chunkData), true);
    }

    public static MeshData GetChunkMeshData(
        ChunkMeshContext context,
        bool collectCollider,
        bool solidColor = false)
    {
        ChunkData chunkData = context.Center;
        if (context.CanUseColumnTerrain)
            return GetColumnTerrainMeshData(context, collectCollider, solidColor);

        MeshData meshData = new MeshData(collectCollider);
        int minimumY = Mathf.Max(0, chunkData.MinimumSolidY);
        int maximumY = Mathf.Min(chunkData.chunkHeight - 1, chunkData.MaximumSolidY);
        for (int z = 0; z < chunkData.chunkSize; z++)
        {
            for (int y = minimumY; y <= maximumY; y++)
            {
                for (int x = 0; x < chunkData.chunkSize; x++)
                {
                    VoxelType type = chunkData.voxels[GetIndexFromPosition(chunkData, x, y, z)];
                    meshData = VoxelHelper.GetMeshData(context, x, y, z, meshData, type, solidColor);
                }
            }
        }
        return meshData;
    }

    private static MeshData GetColumnTerrainMeshData(
        ChunkMeshContext context,
        bool collectCollider,
        bool solidColor)
    {
        ChunkData data = context.Center;
        int size = data.chunkSize;
        int height = data.chunkHeight;
        var meshData = new MeshData(collectCollider, size * size * 2);
        for (int z = 0; z < size; z++)
        {
            for (int x = 0; x < size; x++)
            {
                FarSurfaceColumn column = context.GetFarSurfaceColumn(x, z);
                if (!column.HasSurface)
                    continue;

                int lowestHorizontalTop = Mathf.Min(
                    Mathf.Min(
                        context.GetFarSurfaceColumn(x - 1, z).topY,
                        context.GetFarSurfaceColumn(x + 1, z).topY),
                    Mathf.Min(
                        context.GetFarSurfaceColumn(x, z - 1).topY,
                        context.GetFarSurfaceColumn(x, z + 1).topY));
                int firstVisibleY = Mathf.Min(
                    column.topY,
                    Mathf.Max(0, lowestHorizontalTop + 1));

                bool bottomVisible = !VoxelRuntimeData.IsSolid(context.GetVoxel(x, -1, z));
                if (bottomVisible && firstVisibleY > 0)
                {
                    VoxelType bottomType = data.voxels[x + size * height * z];
                    meshData = VoxelHelper.GetMeshData(context, x, 0, z, meshData, bottomType, solidColor);
                }

                for (int y = firstVisibleY; y <= column.topY; y++)
                {
                    VoxelType type = data.voxels[x + size * y + size * height * z];
                    meshData = VoxelHelper.GetMeshData(context, x, y, z, meshData, type, solidColor);
                }
            }
        }
        return meshData;
    }

    internal static Vector3Int ChunkPositionFromVoxelCoords(World world, int x, int y, int z)
    {
        Vector3Int pos = new Vector3Int
        {
            x = Mathf.FloorToInt(x / (float)world.chunkSize) * world.chunkSize,
            y = Mathf.FloorToInt(y / (float)world.chunkHeight) * world.chunkHeight,
            z = Mathf.FloorToInt(z / (float)world.chunkSize) * world.chunkSize
        };
        return pos;
    }

    internal static List<ChunkData> GetEdgeNeighbourChunk(ChunkData chunkData, Vector3Int worldPosition)
    {
        Vector3Int chunkPosition = GetVoxelInChunkCoordinates(chunkData, worldPosition);
        List<ChunkData> neighboursToUpdate = new List<ChunkData>();
        if (chunkPosition.x == 0)
        {
            neighboursToUpdate.Add(WorldDataHelper.GetChunkData(chunkData.worldReference, worldPosition - Vector3Int.right));
        }
        if (chunkPosition.x == chunkData.chunkSize - 1)
        {
            neighboursToUpdate.Add(WorldDataHelper.GetChunkData(chunkData.worldReference, worldPosition + Vector3Int.right));
        }
        if (chunkPosition.y == 0)
        {
            neighboursToUpdate.Add(WorldDataHelper.GetChunkData(chunkData.worldReference, worldPosition - Vector3Int.up));
        }
        if (chunkPosition.y == chunkData.chunkHeight - 1)
        {
            neighboursToUpdate.Add(WorldDataHelper.GetChunkData(chunkData.worldReference, worldPosition + Vector3Int.up));
        }
        if (chunkPosition.z == 0)
        {
            neighboursToUpdate.Add(WorldDataHelper.GetChunkData(chunkData.worldReference, worldPosition - Vector3Int.forward));
        }
        if (chunkPosition.z == chunkData.chunkSize - 1)
        {
            neighboursToUpdate.Add(WorldDataHelper.GetChunkData(chunkData.worldReference, worldPosition + Vector3Int.forward));
        }
        return neighboursToUpdate;
    }

    internal static bool IsOnEdge(ChunkData chunkData, Vector3Int worldPosition)
    {
        Vector3Int chunkPosition = GetVoxelInChunkCoordinates(chunkData, worldPosition);
        if (
            chunkPosition.x == 0 || chunkPosition.x == chunkData.chunkSize - 1 ||
            chunkPosition.y == 0 || chunkPosition.y == chunkData.chunkHeight - 1 ||
            chunkPosition.z == 0 || chunkPosition.z == chunkData.chunkSize - 1
            )
            return true;
        return false;
    }
}
