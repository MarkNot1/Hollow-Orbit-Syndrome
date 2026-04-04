using UnityEngine;

public class WaterLayerHandler : VoxelLayerHandler
{
    public int waterLevel = 1;

    protected override bool tryHandling(ChunkData data, int x, int y, int z, int surfaceHeightNoise, Vector2Int mapSeedOffset)
    {
        int worldY = data.worldPosition.y + y;
        if (worldY <= waterLevel && worldY > surfaceHeightNoise)
        {
            Vector3Int pos = new Vector3Int(x, y, z);
            Chunk.SetVoxel(data, pos, VoxelType.Water);

            if (worldY == surfaceHeightNoise + 1)
            {
                int surfaceLocalY = surfaceHeightNoise - data.worldPosition.y;
                if (surfaceLocalY >= 0 && surfaceLocalY < data.chunkHeight)
                {
                    pos.y = surfaceLocalY;
                    Chunk.SetVoxel(data, pos, VoxelType.Ship_Metal);
                }
            }

            return true;
        }
        return false;
    }
}
