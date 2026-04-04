using UnityEngine;

public class MetalLayerHandler : VoxelLayerHandler
{
    [Range (0,1)]
    public float metalThreshold = 0.5f;

    [SerializeField]
    private NoiseSettings metalNoiseSettings;

    public DomainWarping domainWarping;

    protected override bool tryHandling(ChunkData chunckData, int x, int y, int z, int surfaceHeightNoise, Vector2Int mapSeedOffset)
    {
        int worldY = chunckData.worldPosition.y + y;
        if (worldY > surfaceHeightNoise)
            return false;

        metalNoiseSettings.worldOffset = mapSeedOffset;
        float metalNoiseValue = domainWarping.GenerateDomainNoise(chunckData.worldPosition.x + x, chunckData.worldPosition.z + z, metalNoiseSettings);

        if (metalNoiseValue > metalThreshold)
        {
            Chunk.SetVoxel(chunckData, new Vector3Int(x, y, z), VoxelType.Metal);
            return true;
        }
        return false;
    }
}
