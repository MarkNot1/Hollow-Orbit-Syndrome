using System;
using UnityEngine;

/// <summary>
/// Value-only copies of terrain authoring settings. Worker threads never read
/// MonoBehaviours or ScriptableObjects through this path.
/// </summary>
public readonly struct NoiseSettingsSnapshot
{
    public readonly float zoom;
    public readonly int octaves;
    public readonly float persistence;
    public readonly Vector2Int offset;
    public readonly Vector2Int worldOffset;
    public readonly float redistribution;
    public readonly float exponent;

    public NoiseSettingsSnapshot(NoiseSettings settings)
    {
        zoom = settings != null ? settings.noiseZoom : 0.01f;
        octaves = settings != null ? Mathf.Max(1, settings.octaves) : 1;
        persistence = settings != null ? settings.persistence : 0.5f;
        offset = settings != null ? settings.offset : Vector2Int.zero;
        worldOffset = settings != null ? settings.worldOffset : Vector2Int.zero;
        redistribution = settings != null ? settings.redistributionModifier : 1f;
        exponent = settings != null ? settings.exponent : 1f;
    }

    public float Sample(float x, float z, Vector2Int seedOffset)
    {
        x = x * zoom + zoom;
        z = z * zoom + zoom;
        float total = 0f;
        float frequency = 1f;
        float amplitude = 1f;
        float amplitudeSum = 0f;
        for (int octave = 0; octave < octaves; octave++)
        {
            total += Mathf.PerlinNoise(
                (offset.x + seedOffset.x + x) * frequency,
                (offset.y + seedOffset.y + z) * frequency) * amplitude;
            amplitudeSum += amplitude;
            amplitude *= persistence;
            frequency *= 2f;
        }

        return amplitudeSum > 0f ? total / amplitudeSum : 0f;
    }
}

public readonly struct DomainWarpSnapshot
{
    public readonly NoiseSettingsSnapshot domainX;
    public readonly NoiseSettingsSnapshot domainY;
    public readonly int amplitudeX;
    public readonly int amplitudeY;

    public DomainWarpSnapshot(DomainWarping source)
    {
        domainX = new NoiseSettingsSnapshot(source != null ? source.noiseDomainX : null);
        domainY = new NoiseSettingsSnapshot(source != null ? source.noiseDomainY : null);
        amplitudeX = source != null ? source.amplitudeX : 0;
        amplitudeY = source != null ? source.amplitudeY : 0;
    }

    public float Sample(float x, float z, NoiseSettingsSnapshot target, Vector2Int seedOffset)
    {
        float dx = domainX.Sample(x, z, domainX.worldOffset) * amplitudeX;
        float dz = domainY.Sample(x, z, domainY.worldOffset) * amplitudeY;
        return target.Sample(x + dx, z + dz, seedOffset);
    }
}

public readonly struct MetalColumnRuleSnapshot
{
    public readonly float threshold;
    public readonly NoiseSettingsSnapshot noise;
    public readonly DomainWarpSnapshot warp;

    public MetalColumnRuleSnapshot(MetalLayerHandler source)
    {
        threshold = source != null ? source.metalThreshold : 1f;
        noise = new NoiseSettingsSnapshot(source != null ? source.MetalNoiseSettings : null);
        warp = new DomainWarpSnapshot(source != null ? source.domainWarping : null);
    }

    public bool Applies(int worldX, int worldZ, Vector2Int seedOffset)
    {
        return warp.Sample(worldX, worldZ, noise, seedOffset) > threshold;
    }
}

public sealed class TerrainGenerationSnapshot
{
    private readonly NoiseSettingsSnapshot biomeNoise;
    private readonly DomainWarpSnapshot biomeWarp;
    private readonly bool useDomainWarping;
    private readonly VoxelType surfaceType;
    private readonly VoxelType undergroundType;
    private readonly MetalColumnRuleSnapshot[] metalRules;

    public TerrainGenerationSnapshot(
        NoiseSettingsSnapshot biomeNoise,
        DomainWarpSnapshot biomeWarp,
        bool useDomainWarping,
        VoxelType surfaceType,
        VoxelType undergroundType,
        MetalColumnRuleSnapshot[] metalRules)
    {
        this.biomeNoise = biomeNoise;
        this.biomeWarp = biomeWarp;
        this.useDomainWarping = useDomainWarping;
        this.surfaceType = surfaceType;
        this.undergroundType = undergroundType;
        this.metalRules = metalRules ?? Array.Empty<MetalColumnRuleSnapshot>();
    }

    public ChunkData Generate(ChunkData data, Vector2Int seedOffset)
    {
        int size = data.chunkSize;
        int height = data.chunkHeight;
        for (int z = 0; z < size; z++)
        {
            int worldZ = data.worldPosition.z + z;
            for (int x = 0; x < size; x++)
            {
                int worldX = data.worldPosition.x + x;
                float terrain = useDomainWarping
                    ? biomeWarp.Sample(worldX, worldZ, biomeNoise, seedOffset)
                    : biomeNoise.Sample(worldX, worldZ, seedOffset);
                terrain = Mathf.Clamp01(Mathf.Pow(
                    terrain * biomeNoise.redistribution,
                    biomeNoise.exponent));
                int surfaceHeight = (int)(terrain * height);

                bool useMetal = false;
                for (int rule = 0; rule < metalRules.Length; rule++)
                {
                    if (metalRules[rule].Applies(worldX, worldZ, seedOffset))
                    {
                        useMetal = true;
                        break;
                    }
                }

                int columnBase = x + size * height * z;
                int topSolidY = -1;
                VoxelType topMaterial = VoxelType.Air;
                for (int localY = 0; localY < height; localY++)
                {
                    int worldY = data.worldPosition.y + localY;
                    VoxelType voxel;
                    if (worldY > surfaceHeight)
                        voxel = VoxelType.Air;
                    else if (useMetal)
                        voxel = VoxelType.Metal;
                    else if (worldY == surfaceHeight)
                        voxel = surfaceType;
                    else
                        voxel = undergroundType;
                    data.voxels[columnBase + size * localY] = voxel;
                    if (voxel != VoxelType.Air && voxel != VoxelType.Nothing)
                    {
                        topSolidY = localY;
                        topMaterial = voxel;
                    }
                }
                data.SetGeneratedSurfaceColumn(x, z, topSolidY, topMaterial);
            }
        }

        data.MarkGeneratedFromSurfaceColumns();
        return data;
    }
}
