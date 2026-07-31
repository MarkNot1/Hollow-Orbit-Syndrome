using System.Collections.Generic;
using UnityEngine;

public class BiomeGenerator : MonoBehaviour
{

    public NoiseSettings biomeNoiseSettings;

    public DomainWarping domainWarping;
    public bool UseDomainWarping = true;

    public VoxelLayerHandler startLayerHandler;

    public List<VoxelLayerHandler> aditionalLayerHandlers;

    private bool useBaseLayerTypes;
    private VoxelType surfaceVoxelType;
    private VoxelType undergroundVoxelType;

    private void Awake()
    {
        RefreshLayerReferences();
    }

    private void OnValidate()
    {
        RefreshLayerReferences();
    }

    private void RefreshLayerReferences()
    {
        if (startLayerHandler == null)
            startLayerHandler = GetComponentInChildren<AirLayerHandler>();

        if (aditionalLayerHandlers == null)
            aditionalLayerHandlers = new List<VoxelLayerHandler>();
        aditionalLayerHandlers.RemoveAll(layer => layer == null);

        SurfaceLayerHandler surfaceLayer = GetComponentInChildren<SurfaceLayerHandler>();
        UndergroundLayerHandler undergroundLayer = GetComponentInChildren<UndergroundLayerHandler>();
        if (surfaceLayer != null && undergroundLayer != null)
        {
            surfaceVoxelType = surfaceLayer.surfaceVoxelType;
            undergroundVoxelType = undergroundLayer.undergroundVoxelType;
            useBaseLayerTypes = true;
        }
        else
        {
            useBaseLayerTypes = false;
        }
    }

    public TerrainGenerationSnapshot CreateSnapshot()
    {
        RefreshLayerReferences();
        var metals = new List<MetalColumnRuleSnapshot>();
        if (aditionalLayerHandlers != null)
        {
            foreach (VoxelLayerHandler layer in aditionalLayerHandlers)
            {
                if (layer is MetalLayerHandler metal)
                    metals.Add(new MetalColumnRuleSnapshot(metal));
            }
        }

        return new TerrainGenerationSnapshot(
            new NoiseSettingsSnapshot(biomeNoiseSettings),
            new DomainWarpSnapshot(domainWarping),
            UseDomainWarping,
            surfaceVoxelType,
            undergroundVoxelType,
            metals.ToArray());
    }

    public ChunkData ProcessChunckColumn(ChunkData data, int x, int z, Vector2Int mapSeedOffset)
    {
        int groundPosition = GetSurfaceHeightNoise(
            data.worldPosition.x + x,
            data.worldPosition.z + z,
            data.chunkHeight,
            mapSeedOffset);

        if (useBaseLayerTypes)
        {
            for (int localY = 0; localY < data.chunkHeight; localY++)
                HandleBaseLayer(data, x, localY, z, groundPosition);
        }
        else if (startLayerHandler != null)
        {
            for (int localY = 0; localY < data.chunkHeight; localY++)
                startLayerHandler.Handle(data, x, localY, z, groundPosition, mapSeedOffset);
        }

        if (aditionalLayerHandlers == null)
            return data;

        foreach (var layer in aditionalLayerHandlers)
        {
            if (layer == null)
                continue;

            for (int localY = 0; localY < data.chunkHeight; localY++)
                layer.Handle(data, x, localY, z, groundPosition, mapSeedOffset);
        }

        return data;
    }

    private void HandleBaseLayer(ChunkData data, int x, int y, int z, int surfaceHeightNoise)
    {
        int worldY = data.worldPosition.y + y;
        Vector3Int pos = new Vector3Int(x, y, z);

        if (worldY > surfaceHeightNoise)
            Chunk.SetVoxel(data, pos, VoxelType.Air);
        else if (worldY == surfaceHeightNoise)
            Chunk.SetVoxel(data, pos, surfaceVoxelType);
        else
            Chunk.SetVoxel(data, pos, undergroundVoxelType);
    }

    public int GetSurfaceHeightNoise(int x, int z, int chunkHeight)
    {
        return GetSurfaceHeightNoise(x, z, chunkHeight, biomeNoiseSettings.worldOffset);
    }

    public int GetSurfaceHeightNoise(
        int x,
        int z,
        int chunkHeight,
        Vector2Int worldOffset)
    {
        float terrainHeight;
        if (UseDomainWarping == false)
        {
            terrainHeight = MyNoise.OctavePerlin(x, z, biomeNoiseSettings, worldOffset);
        }
        else
        {
            terrainHeight = domainWarping.GenerateDomainNoise(
                x,
                z,
                biomeNoiseSettings,
                worldOffset);
        }    
        terrainHeight = MyNoise.Redistribution(terrainHeight, biomeNoiseSettings);
        int surfaceHeight = (int)MyNoise.RemapValue01ToInt(terrainHeight, 0, chunkHeight);
        return surfaceHeight;
    }
}
