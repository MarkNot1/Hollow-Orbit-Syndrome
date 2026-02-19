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
    public Vector3Int currentPlayerChunkPosition;
    private Vector3Int currentChunkCenter = Vector3Int.zero;

    public World world;

    [Tooltip("If true, SpawnPlayer() is called automatically when World invokes OnWorldCreated (e.g. after GenerateWorld()).")]
    [SerializeField]
    private bool spawnPlayerWhenWorldCreated = true;

    public float detectionTime = 1;
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
        Vector3 rayStart = new Vector3(halfChunk, 100f, halfChunk);
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
        StopAllCoroutines();
        StartCoroutine(CheckIfShouldLoadNextPosition());
    }

    IEnumerator CheckIfShouldLoadNextPosition()
    {
        if (player == null || world == null) yield break;

        yield return new WaitForSeconds(detectionTime);

        if (player == null || world == null) yield break;

        if (
            Mathf.Abs(currentChunkCenter.x - player.transform.position.x) > world.chunkSize ||
            Mathf.Abs(currentChunkCenter.z - player.transform.position.z) > world.chunkSize ||
            Mathf.Abs(currentPlayerChunkPosition.y - player.transform.position.y) > world.chunkHeight
            )
        {
            world.LoadAdditionalChunksRequest(player);
        }

        StartCoroutine(CheckIfShouldLoadNextPosition());
    }

    private void SetCurrentChunkCoordinates()
    {
        if (player == null || world == null) return;
        currentPlayerChunkPosition = WorldDataHelper.ChunkPositionFromVoxelCoords(world, Vector3Int.RoundToInt(player.transform.position));
        currentChunkCenter.x = currentPlayerChunkPosition.x + world.chunkSize / 2;
        currentChunkCenter.z = currentPlayerChunkPosition.z + world.chunkSize / 2;
    }
}
