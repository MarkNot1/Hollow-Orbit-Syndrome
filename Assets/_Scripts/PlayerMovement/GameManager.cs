//using Cinemachine;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public GameObject playerPrefab;
    private GameObject player;

    /// <summary>Spawned player instance; null before <see cref="SpawnPlayer"/> completes.</summary>
    public GameObject PlayerObject => player;
    public Vector3Int currentPlayerChunkPosition;
    private Vector3Int currentChunkCenter = Vector3Int.zero;

    public World world;

    [Tooltip("If true, SpawnPlayer() is called automatically when World invokes OnWorldCreated (e.g. after GenerateWorld()).")]
    [SerializeField]
    private bool spawnPlayerWhenWorldCreated = true;

    public float detectionTime = 1;
    public CinemachineCamera camera_VM;

    /// <summary>Chunk world keys (3×3×3 neighbourhood) that currently have terrain colliders enabled. Separate from world streaming.</summary>
    private readonly HashSet<Vector3Int> colliderGridChunks = new HashSet<Vector3Int>();

    [Header("Fallback spawn (used if raycast hits nothing)")]
    [SerializeField]
    private Vector3 fallbackSpawnPosition = new Vector3(8f, 25f, 8f);

    private void Start()
    {
        if (spawnPlayerWhenWorldCreated && world != null)
            world.OnWorldCreated.AddListener(SpawnPlayer);
        if (world != null)
            world.OnNewChunksGenerated.AddListener(OnNewChunksGeneratedRefreshColliders);
    }

    private void OnDestroy()
    {
        if (world != null)
            world.OnWorldCreated.RemoveListener(SpawnPlayer);
        if (world != null)
            world.OnNewChunksGenerated.RemoveListener(OnNewChunksGeneratedRefreshColliders);
    }

    private void OnNewChunksGeneratedRefreshColliders()
    {
        if (player != null && world != null)
            UpdatePlayerChunkCollider();
    }

    public void SpawnPlayer()
    {
        if (player != null)
            return;

        if (world == null)
        {
            world = FindFirstObjectByType<World>();
            if (world == null)
            {
                Debug.LogWarning("GameManager.SpawnPlayer: No World found. Assign World in the Inspector or ensure a World exists in the scene.");
                return;
            }
        }

        if (playerPrefab == null)
        {
            Debug.LogError("GameManager.SpawnPlayer: Player Prefab is not assigned in the Inspector.");
            return;
        }

        Vector3 spawnPosition;
        int halfChunk = world.chunkSize / 2;
        Vector3 rayStart = new Vector3(halfChunk * VoxelMetrics.Size, 100f, halfChunk * VoxelMetrics.Size);
        Vector3Int initialChunkPos = WorldDataHelper.ChunkPositionFromVoxelCoords(world, VoxelMetrics.WorldToVoxelCoord(rayStart));
        ApplyColliderGrid3x3Around(initialChunkPos);
        RaycastHit hit;

        if (Physics.Raycast(rayStart, Vector3.down, out hit, 120f))
        {
            spawnPosition = hit.point + Vector3.up;
        }
        else
        {
            Debug.LogWarning("GameManager.SpawnPlayer: Raycast from " + rayStart + " did not hit ground. Using fallback spawn position.");
            spawnPosition = fallbackSpawnPosition;
        }

        player = Instantiate(playerPrefab, spawnPosition, Quaternion.identity);

        if (camera_VM != null && player.transform.childCount > 0)
            camera_VM.Follow = player.transform.GetChild(0);
        else if (camera_VM != null)
            camera_VM.Follow = player.transform;

        StartCheckingTheMap();
    }

    public void StartCheckingTheMap()
    {
        if (player == null || world == null) return;
        SetCurrentChunkCoordinates();
        UpdatePlayerChunkCollider();
        StopAllCoroutines();
        StartCoroutine(CheckIfShouldLoadNextPosition());
    }

    IEnumerator CheckIfShouldLoadNextPosition()
    {
        if (player == null || world == null) yield break;

        yield return new WaitForSeconds(detectionTime);

        if (player == null || world == null) yield break;

        Vector3Int playerVoxel = VoxelMetrics.WorldToVoxelCoord(player.transform.position);
        int centerVx = currentPlayerChunkPosition.x + world.chunkSize / 2;
        int centerVz = currentPlayerChunkPosition.z + world.chunkSize / 2;
        if (
            Mathf.Abs(centerVx - playerVoxel.x) > world.chunkSize ||
            Mathf.Abs(centerVz - playerVoxel.z) > world.chunkSize ||
            Mathf.Abs(currentPlayerChunkPosition.y - playerVoxel.y) > world.chunkHeight
            )
        {
            world.LoadAdditionalChunksRequest(player);
            SetCurrentChunkCoordinates();
        }

        UpdatePlayerChunkCollider();

        StartCoroutine(CheckIfShouldLoadNextPosition());
    }

    private void SetCurrentChunkCoordinates()
    {
        if (player == null || world == null) return;
        currentPlayerChunkPosition = WorldDataHelper.ChunkPositionFromVoxelCoords(world, VoxelMetrics.WorldToVoxelCoord(player.transform.position));
        currentChunkCenter.x = currentPlayerChunkPosition.x + world.chunkSize / 2;
        currentChunkCenter.z = currentPlayerChunkPosition.z + world.chunkSize / 2;
    }

    private void UpdatePlayerChunkCollider()
    {
        if (player == null || world == null) return;

        Vector3Int center = WorldDataHelper.ChunkPositionFromVoxelCoords(world, VoxelMetrics.WorldToVoxelCoord(player.transform.position));
        ApplyColliderGrid3x3Around(center);
    }

    private HashSet<Vector3Int> BuildColliderGrid3x3Around(Vector3Int centerChunkWorldPos)
    {
        var desired = new HashSet<Vector3Int>();
        int cs = world.chunkSize;
        int ch = world.chunkHeight;
        for (int oy = -1; oy <= 1; oy++)
        {
            for (int ox = -1; ox <= 1; ox++)
            {
                for (int oz = -1; oz <= 1; oz++)
                {
                    desired.Add(centerChunkWorldPos + new Vector3Int(ox * cs, oy * ch, oz * cs));
                }
            }
        }

        return desired;
    }

    /// <summary>
    /// Enables MeshColliders for a 3×3×3 neighbourhood (xz and vertical chunk layers) around the player chunk.
    /// Chunk render/streaming is unchanged; this only toggles collision.
    /// </summary>
    private void ApplyColliderGrid3x3Around(Vector3Int centerChunkWorldPos)
    {
        if (world == null) return;

        HashSet<Vector3Int> desired = BuildColliderGrid3x3Around(centerChunkWorldPos);

        foreach (Vector3Int pos in colliderGridChunks)
        {
            if (!desired.Contains(pos))
            {
                ChunkRenderer chunk = WorldDataHelper.GetChunk(world, pos);
                if (chunk != null)
                    chunk.SetCollisionActive(false);
            }
        }

        foreach (Vector3Int pos in desired)
        {
            ChunkRenderer chunk = WorldDataHelper.GetChunk(world, pos);
            if (chunk != null)
                chunk.SetCollisionActive(true);
        }

        colliderGridChunks.Clear();
        foreach (Vector3Int p in desired)
            colliderGridChunks.Add(p);
    }

    /// <summary>For debug overlay: copies chunk world keys that currently have terrain collision enabled.</summary>
    public void CopyActiveColliderChunkKeysTo(List<Vector3Int> destination)
    {
        destination.Clear();
        foreach (Vector3Int p in colliderGridChunks)
            destination.Add(p);
    }
}
