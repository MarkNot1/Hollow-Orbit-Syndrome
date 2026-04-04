using UnityEngine;

public class SurfaceLayerHandler : VoxelLayerHandler
{
    public VoxelType surfaceVoxelType;

    protected override bool tryHandling(ChunkData data, int x, int y, int z, int surfaceHeightNoise, Vector2Int mapSeedOffset)
    {
        int worldY = data.worldPosition.y + y;
        if (worldY == surfaceHeightNoise)
        {
            Vector3Int pos = new Vector3Int(x, y, z);
            Chunk.SetVoxel(data, pos, surfaceVoxelType);
            return true;
        }
        return false;
    }
}
