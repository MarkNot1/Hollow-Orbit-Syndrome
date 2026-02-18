using UnityEngine;

public class WaterLayerHandler : VoxelLayerHandler
{
    public int waterLevel = 1;

    protected override bool tryHandling(ChunkData data, int x, int y, int z, int surfaceHeightNoise, Vector2Int mapSeedOffset)
    {
        if (y <= waterLevel && y > surfaceHeightNoise)
        {
            Vector3Int pos = new Vector3Int(x, y, z);
            Chunk.SetVoxel(data, pos, VoxelType.Water);

            if(y == surfaceHeightNoise + 1)
            {
                pos.y = surfaceHeightNoise;
                Chunk.SetVoxel(data, pos, VoxelType.Ship_Metal);
            }


            return true;
        }
        return false;
    }
}
