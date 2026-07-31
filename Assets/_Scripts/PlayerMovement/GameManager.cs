using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public GameObject playerPrefab;
    private GameObject player;

    public GameObject PlayerObject => player;
    public Vector3Int currentPlayerChunkPosition;
    public World world;

    [Tooltip("If true, SpawnPlayer() is called automatically when World invokes OnWorldCreated.")]
    [SerializeField]
    private bool spawnPlayerWhenWorldCreated = true;

    public float detectionTime = 1f;
    public CinemachineCamera camera_VM;

    [Header("Fallback spawn (used if raycast hits nothing)")]
    [SerializeField]
    private Vector3 fallbackSpawnPosition = new Vector3(8f, 25f, 8f);

    private void Start()
    {
        if (spawnPlayerWhenWorldCreated && world != null)
            world.OnWorldCreated.AddListener(SpawnPlayer);
    }

    private void OnDestroy()
    {
        if (world != null)
            world.OnWorldCreated.RemoveListener(SpawnPlayer);
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
                Debug.LogWarning("GameManager.SpawnPlayer: No World found.");
                return;
            }
        }
        if (playerPrefab == null)
        {
            Debug.LogError("GameManager.SpawnPlayer: Player Prefab is not assigned.");
            return;
        }

        int halfChunk = world.chunkSize / 2;
        Vector3 rayStart = new Vector3(
            halfChunk * VoxelMetrics.Size,
            100f,
            halfChunk * VoxelMetrics.Size);
        Vector3 spawnPosition;
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 120f))
        {
            spawnPosition = hit.point + Vector3.up;
        }
        else
        {
            Debug.LogWarning(
                "GameManager.SpawnPlayer: the ready center collider was not hit from " +
                rayStart + ". Using fallback spawn position.");
            spawnPosition = fallbackSpawnPosition;
        }

        player = Instantiate(playerPrefab, spawnPosition, Quaternion.identity);
        world.SetStreamingTarget(player.transform);

        if (camera_VM != null && player.transform.childCount > 0)
            camera_VM.Follow = player.transform.GetChild(0);
        else if (camera_VM != null)
            camera_VM.Follow = player.transform;

        PlayerCamera playerCamera = player.GetComponentInChildren<PlayerCamera>();
        if (playerCamera != null)
            playerCamera.CaptureCursorForGameplay();
        StartCheckingTheMap();
    }

    public void StartCheckingTheMap()
    {
        if (player == null || world == null)
            return;
        SetCurrentChunkCoordinates();
        world.SetStreamingTarget(player.transform);
        StopAllCoroutines();
    }

    private void Update()
    {
        if (player == null || world == null)
            return;
        Vector3Int playerChunk = WorldDataHelper.ChunkPositionFromVoxelCoords(
            world,
            VoxelMetrics.WorldToVoxelCoord(player.transform.position));
        if (playerChunk != currentPlayerChunkPosition)
            SetCurrentChunkCoordinates();
    }

    private void SetCurrentChunkCoordinates()
    {
        if (player == null || world == null)
            return;
        currentPlayerChunkPosition = WorldDataHelper.ChunkPositionFromVoxelCoords(
            world,
            VoxelMetrics.WorldToVoxelCoord(player.transform.position));
    }

    public void CopyActiveColliderChunkKeysTo(List<Vector3Int> destination)
    {
        if (world != null)
            world.CopyActiveCollisionChunkKeys(destination);
        else
            destination.Clear();
    }
}
