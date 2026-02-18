using UnityEngine;

public class AirLayerHandler : VoxelLayerHandler
{
    protected override bool tryHandling(ChunkData data, int x, int y, int z, int surfaceHeightNoise, Vector2Int mapSeedOffset)
    {
        if (y > surfaceHeightNoise)
        {
            Vector3Int pos = new Vector3Int(x, y, z);
            Chunk.SetVoxel(data, pos, VoxelType.Air);
            return true;
        }
        return false;
    }
}
