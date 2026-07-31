using System;
using UnityEngine;

// Numeric values are compatibility-sensitive; legacy names remain serialized.
public enum ChunkLodLevel
{
    Full = 0,
    Merge2 = 1,
    Merge4 = 2,
    Merge16 = 3
}

[Serializable]
public sealed class DistanceSimplifierSettings
{
    public bool enabled = true;
    public bool uniformSurfaceRendering;
    [Tooltip("First zero-based chunk ring using the textured surface tier (4 keeps human-counted rings 1-4 full).")]
    [Min(1f)] public float lod1StartChunks = 4f;
    [Tooltip("First zero-based chunk ring using solid color (6 starts on the seventh human-counted ring).")]
    [Min(1f)] public float lod2StartChunks = 6f;
    [HideInInspector, Min(1f)] public float lod3StartChunks = 6f;
}

/// <summary>
/// Chooses LOD from whole chunk coordinates. All tiers share one surface mesh,
/// so changing texture cost cannot open a seam or alter the terrain silhouette.
/// </summary>
public static class DistanceSimplifier
{
    public static ChunkLodLevel SelectLod(
        Vector3Int playerVoxel,
        Vector3Int chunkPosition,
        int chunkSize,
        ChunkLodLevel current,
        bool hasCurrent,
        DistanceSimplifierSettings settings)
    {
        if (settings == null || !settings.enabled)
            return ChunkLodLevel.Full;
        if (settings.uniformSurfaceRendering)
            return ChunkLodLevel.Merge16;

        int size = Mathf.Max(1, chunkSize);
        int playerChunkX = Mathf.FloorToInt(playerVoxel.x / (float)size);
        int playerChunkZ = Mathf.FloorToInt(playerVoxel.z / (float)size);
        int chunkX = Mathf.FloorToInt(chunkPosition.x / (float)size);
        int chunkZ = Mathf.FloorToInt(chunkPosition.z / (float)size);
        int ring = Mathf.Max(Mathf.Abs(chunkX - playerChunkX), Mathf.Abs(chunkZ - playerChunkZ));
        int texturedRing = Mathf.Max(1, Mathf.CeilToInt(settings.lod1StartChunks));
        int solidRing = Mathf.Max(texturedRing + 1, Mathf.CeilToInt(settings.lod2StartChunks));

        return ring < texturedRing
            ? ChunkLodLevel.Full
            : ring < solidRing ? ChunkLodLevel.Merge4 : ChunkLodLevel.Merge16;
    }

    public static MeshData BuildMesh(ChunkData data, ChunkLodLevel lod)
    {
        return BuildMesh(new ChunkMeshContext(data), lod, lod == ChunkLodLevel.Full);
    }

    public static MeshData BuildMesh(ChunkMeshContext context, ChunkLodLevel lod, bool collectCollider)
    {
        return Chunk.GetChunkMeshData(context, collectCollider, lod == ChunkLodLevel.Merge16);
    }
}
