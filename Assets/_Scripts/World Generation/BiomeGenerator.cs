using System.Collections.Generic;
using UnityEngine;

public class BiomeGenerator : MonoBehaviour
{

    public int waterThreshold = 50;

    public NoiseSettings biomeNoiseSettings;

    public DomainWarping domainWarping;
    public bool UseDomainWarping = true;

    public VoxelLayerHandler startLayerHandler;

    public List<VoxelLayerHandler> aditionalLayerHandlers;

    public ChunkData ProcessChunckColumn(ChunkData data, int x, int z, Vector2Int mapSeedOffset)
    {
        biomeNoiseSettings.worldOffset = mapSeedOffset;
        int groundPosition = GetSurfaceHeightNoise(data.worldPosition.x + x, data.worldPosition.z + z, data.chunkHeight);

        // Handlers use local Y (0..chunkHeight-1); compare to surface using world Y inside each handler.
        for (int localY = 0; localY < data.chunkHeight; localY++)
            startLayerHandler.Handle(data, x, localY, z, groundPosition, mapSeedOffset);

        foreach (var layer in aditionalLayerHandlers)
        {
            for (int localY = 0; localY < data.chunkHeight; localY++)
                layer.Handle(data, x, localY, z, groundPosition, mapSeedOffset);
        }

        return data;
    }

    public int GetSurfaceHeightNoise(int x, int z, int chunkHeight)
    {
        float terrainHeight;
        if (UseDomainWarping == false)
        {
            terrainHeight = MyNoise.OctavePerlin(x, z, biomeNoiseSettings);
        }
        else
        {
            terrainHeight = domainWarping.GenerateDomainNoise(x, z, biomeNoiseSettings);
        }    
        terrainHeight = MyNoise.Redistribution(terrainHeight, biomeNoiseSettings);
        int surfaceHeight = (int)MyNoise.RemapValue01ToInt(terrainHeight, 0, chunkHeight);
        return surfaceHeight;
    }
}
