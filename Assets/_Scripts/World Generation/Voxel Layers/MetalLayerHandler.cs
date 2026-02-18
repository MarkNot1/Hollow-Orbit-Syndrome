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
        if (chunckData.worldPosition.y > surfaceHeightNoise)
        {
            return false;
        }

        metalNoiseSettings.worldOffset = mapSeedOffset;
        //float metalNoiseValue = MyNoise.OctavePerlin(chunckData.worldPosition.x + x, chunckData.worldPosition.z + z, metalNoiseSettings);

        float metalNoiseValue = domainWarping.GenerateDomainNoise(chunckData.worldPosition.x + x, chunckData.worldPosition.z + z, metalNoiseSettings);

        int endPosition = surfaceHeightNoise;
        if (chunckData.worldPosition.y < 0)
        {
            endPosition = chunckData.worldPosition.y + chunckData.chunkHeight;
        }
    
        if (metalNoiseValue > metalThreshold)
        {
            for (int i = chunckData.worldPosition.y; i < endPosition; i++)
            {
                Vector3Int pos = new Vector3Int(x, i, z);
                Chunk.SetVoxel(chunckData,pos , VoxelType.Metal);
            }
            return true;
        }
        return false;
    }
}
