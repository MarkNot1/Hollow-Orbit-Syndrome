using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public partial class World : MonoBehaviour
{
    public int mapSizeInChunks = 6;
    public int chunkSize = 32, chunkHeight = 100;
    public int chunkDrawingRange = 8;

    [Header("Distance Simplification")]
    [SerializeField]
    private DistanceSimplifierSettings distanceSimplification = new DistanceSimplifierSettings();
    [Tooltip("Maximum background chunk/mesh workers. Zero uses up to three; nearby collider cooking is bounded on the main thread.")]
    [SerializeField, Min(0)]
    private int generationWorkerLimit;
    [Tooltip("Maximum completed render meshes applied in one frame.")]
    [SerializeField, Min(1)]
    private int meshUploadsPerFrame = 4;
    [Tooltip("Main-thread mesh upload budget after the playable center is ready.")]
    [SerializeField, Min(0.25f)]
    private float meshUploadBudgetMs = 2f;
    public GameObject chunkPrefab;
    public WorldRenderer worldRenderer;
    [SerializeField]
    private GameObject circuitVoxelPrefab;
    [SerializeField]
    private SpecialPrefabDatabaseSO specialPrefabDatabase;

    public TerrainGenerator terrainGenerator;
    public Vector2Int mapSeedOffset;

    public UnityEvent OnWorldCreated, OnNewChunksGenerated;
    public event Action<Vector3Int, ChunkRenderer> OnChunkRendered;

    public WorldData worldData { get; private set; }
    public SpecialVoxelManager SpecialVoxels { get; private set; }
    private bool IsWorldCreated;

    private void Awake()
    {
        distanceSimplification ??= new DistanceSimplifierSettings();
        worldData = new WorldData
        {
            chunkHeight = chunkHeight,
            chunkSize = chunkSize,
            chunkDataDictionary = new Dictionary<Vector3Int, ChunkData>(),
            chunkDictionary = new Dictionary<Vector3Int, ChunkRenderer>()
        };
        InitializeStreaming();
    }

    public void GenerateWorld()
    {
        BeginStreaming(Vector3Int.zero);
    }

    private int ResolveGenerationWorkerCount()
    {
        // Unity's main, render and physics threads need headroom while streaming.
        // Three general workers saturate this small-column workload while leaving
        // headroom for the render, physics and bounded collider-cook work.
        int automatic = Math.Max(1, Math.Min(Environment.ProcessorCount - 2, 3));
        return generationWorkerLimit <= 0
            ? automatic
            : Math.Max(1, Math.Min(generationWorkerLimit, automatic));
    }

    internal bool SetVoxel(RaycastHit hit, VoxelType voxelType)
    {
        ChunkRenderer chunk = hit.collider != null ? hit.collider.GetComponent<ChunkRenderer>() : null;
        if (chunk == null || chunk.ChunkData == null || chunk.ChunkData.worldReference != this)
            return false;

        Vector3Int pos = GetVoxelPos(hit);
        ChunkData data = WorldDataHelper.GetChunkData(this, pos);
        if (data == null) return false;
        VoxelType previous = Chunk.GetVoxelFromChunkCoordinates(data, Chunk.GetVoxelInChunkCoordinates(data, pos));
        if (previous == VoxelType.Air || previous == VoxelType.Nothing || previous == voxelType) return false;
        WorldDataHelper.SetVoxel(chunk.ChunkData.worldReference, pos, voxelType);
        if (SpecialVoxels != null &&
            (voxelType == VoxelType.Air || voxelType == VoxelType.Nothing))
            SpecialVoxels.RemoveSpecialPrefabsAttachedTo(pos);
        chunk.ModifiedByThePlayer = true;
        RequestChunkRemesh(chunk.ChunkData, Chunk.IsOnEdge(chunk.ChunkData, pos));
        return true;
    }

    /// <summary>
    /// Places a voxel in the empty cell adjacent to the hit face.
    /// </summary>
    internal bool PlaceVoxel(RaycastHit hit, VoxelType voxelType)
    {
        if (!VoxelRuntimeData.IsSolid(voxelType) || !TryGetVoxelPlacementCell(hit, out Vector3Int pos))
            return false;

        WorldDataHelper.SetVoxel(this, pos, voxelType);

        Vector3Int[] faceNeighbours =
        {
            pos + Vector3Int.up,
            pos + Vector3Int.down,
            pos + Vector3Int.left,
            pos + Vector3Int.right,
            pos + Vector3Int.forward,
            pos + Vector3Int.back
        };
        foreach (Vector3Int neighbourPos in faceNeighbours)
        {
            ChunkData neighbourData =
                WorldDataHelper.GetChunkData(this, neighbourPos);
            if (neighbourData == null)
                continue;

            Vector3Int localPosition =
                Chunk.GetVoxelInChunkCoordinates(neighbourData, neighbourPos);
            if (Chunk.GetVoxelFromChunkCoordinates(neighbourData, localPosition) ==
                VoxelType.Nothing)
            {
                WorldDataHelper.SetVoxel(
                    this,
                    neighbourPos,
                    VoxelType.Air);
            }
        }

        ChunkData targetChunkData = WorldDataHelper.GetChunkData(this, pos);
        targetChunkData.modifiedByThePlayer = true;
        RequestChunkRemesh(targetChunkData, Chunk.IsOnEdge(targetChunkData, pos));

        return true;
    }

    private readonly Collider[] placementOverlaps = new Collider[32];

    internal bool TryGetVoxelPlacementCell(RaycastHit hit, out Vector3Int position)
    {
        position = default;
        ChunkRenderer source = hit.collider != null ? hit.collider.GetComponent<ChunkRenderer>() : null;
        if (source == null || source.ChunkData == null || source.ChunkData.worldReference != this ||
            VoxelMetrics.FaceNormal(hit.normal) == Vector3Int.zero) return false;
        Vector3Int surface = VoxelMetrics.WorldHitToTargetVoxelIndex(hit);
        ChunkData surfaceData = WorldDataHelper.GetChunkData(this, surface);
        if (surfaceData == null || !VoxelRuntimeData.IsSolid(Chunk.GetVoxelFromChunkCoordinates(
                surfaceData, Chunk.GetVoxelInChunkCoordinates(surfaceData, surface)))) return false;
        position = VoxelMetrics.WorldHitToAdjacentVoxelIndex(hit);
        ChunkData destination = WorldDataHelper.GetChunkData(this, position);
        if (destination == null) return false;
        VoxelType existing = Chunk.GetVoxelFromChunkCoordinates(destination, Chunk.GetVoxelInChunkCoordinates(destination, position));
        if (existing != VoxelType.Air && existing != VoxelType.Nothing) return false;
        int count = Physics.OverlapBoxNonAlloc((Vector3)position * VoxelMetrics.Size,
            Vector3.one * (VoxelMetrics.Size * 0.5f - 0.005f), placementOverlaps,
            Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        if (count == placementOverlaps.Length) return false;
        // Chunk occupancy above is authoritative while their colliders are being rebuilt.
        for (int i = 0; i < count; i++)
            if (placementOverlaps[i].GetComponent<ChunkRenderer>() == null) return false;
        return true;
    }

    internal bool PlaceSpecialVoxel(RaycastHit hit)
    {
        EnsureSpecialVoxelManager();
        return SpecialVoxels != null && SpecialVoxels.PlaceCircuit(hit);
    }

    internal bool PlaceSpecialPrefab(RaycastHit hit, SpecialPrefabData prefabData)
    {
        EnsureSpecialVoxelManager();
        return SpecialVoxels != null &&
               SpecialVoxels.PlaceSpecialPrefab(hit, prefabData);
    }

    internal bool PlaceSpecialPrefab(RaycastHit hit, string itemId)
    {
        if (specialPrefabDatabase == null ||
            !specialPrefabDatabase.TryGetPrefab(itemId, out SpecialPrefabData prefabData))
            return false;

        return PlaceSpecialPrefab(hit, prefabData);
    }

    private void EnsureSpecialVoxelManager()
    {
        if (SpecialVoxels != null)
            return;

        SpecialVoxels = GetComponent<SpecialVoxelManager>();
        if (SpecialVoxels == null)
            SpecialVoxels = gameObject.AddComponent<SpecialVoxelManager>();
        SpecialVoxels.Configure(this, circuitVoxelPrefab, specialPrefabDatabase);
    }

    private static Vector3Int GetVoxelPos(RaycastHit hit)
    {
        return VoxelMetrics.WorldHitToTargetVoxelIndex(hit);
    }

    internal void LoadAdditionalChunksRequest(GameObject player)
    {
        if (player != null)
            SetStreamingTarget(player.transform);
    }

    internal VoxelType GetVoxelFromChunkCoordinates(
        ChunkData chunkData,
        int x,
        int y,
        int z)
    {
        Vector3Int pos = Chunk.ChunkPositionFromVoxelCoords(this, x, y, z);
        worldData.chunkDataDictionary.TryGetValue(pos, out ChunkData containerChunk);

        if (containerChunk == null)
            return VoxelType.Nothing;

        Vector3Int voxelInChunkCoordinates = Chunk.GetVoxelInChunkCoordinates(
            containerChunk,
            new Vector3Int(x, y, z));
        return Chunk.GetVoxelFromChunkCoordinates(
            containerChunk,
            voxelInChunkCoordinates);
    }

    public void OnDisable()
    {
        ShutdownStreaming();
    }
}

public struct WorldData
{
    public Dictionary<Vector3Int, ChunkData> chunkDataDictionary;
    public Dictionary<Vector3Int, ChunkRenderer> chunkDictionary;
    public int chunkSize;
    public int chunkHeight;
}
