using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

public class World : MonoBehaviour
{
    public int mapSizeInChunks = 6;
    public int chunkSize = 32, chunkHeight = 100;
    public int chunkDrawingRange = 8;

    public GameObject chunkPrefab;
    public WorldRenderer worldRenderer;

    public TerrainGenerator terrainGenerator;
    public Vector2Int mapSeedOffset;

    CancellationTokenSource taskTokenSource = new CancellationTokenSource();

    //public Dictionary<Vector3Int, ChunkData> chunkDataDictionary = new Dictionary<Vector3Int, ChunkData>();
    //public Dictionary<Vector3Int, ChunkRenderer> chunkDictionary = new Dictionary<Vector3Int, ChunkRenderer>();

    public UnityEvent OnWorldCreated, OnNewChunksGenerated;
    public event Action<Vector3Int, ChunkRenderer> OnChunkRendered;

    public WorldData worldData { get; private set; }
    private bool IsWorldCreated = false;

    private void Awake()
    {
        worldData = new WorldData
        {
            chunkHeight = this.chunkHeight,
            chunkSize = this.chunkSize,
            chunkDataDictionary = new Dictionary<Vector3Int, ChunkData>(),
            chunkDictionary = new Dictionary<Vector3Int, ChunkRenderer>()
        };
    }

    public async void GenerateWorld()
    {
        await GenerateWorld(Vector3Int.zero);
        //AxyncTest();
    }

    /*private async void AxyncTest()
    {
        Debug.Log("Doing Async work");   
        StartCoroutine(AsyncCoroutine());
        int value = await testTask();
        Debug.Log("Task returned: " + value);
        StopAllCoroutines();
        Debug.Log("Finished generation");
    }

    IEnumerator AsyncCoroutine()
    {
        yield return new WaitForSeconds(0.5f);
        Debug.Log("Playing game" + Time.time);
        StartCoroutine(AsyncCoroutine());
    }

    private async Task<int> testTask()
    {
        await Task.Delay(1000);
        return await Task.Run(() => {
            return 10;
        });
    }*/


    private async Task GenerateWorld(Vector3Int position)
    {

        WorldGenerationData worldGenerationData = await Task.Run(() => GetPositionsThatPlayerSees(position), taskTokenSource.Token);

        foreach (Vector3Int pos in worldGenerationData.chunkPositionsToRemove)
        {
            WorldDataHelper.RemoveChunk(this, pos);
        }

        foreach (Vector3Int pos in worldGenerationData.chunkDataToRemove)
        {
            WorldDataHelper.RemoveChunkData(this, pos);
        }

        ConcurrentDictionary<Vector3Int, ChunkData> dataDictionary = null;

        try
        {
            dataDictionary = await CalculateWorldChunkData(worldGenerationData.chunkDataPositionsToCreate);
        }
        catch (Exception)
        {
            Debug.Log("Task cancelled");
            return;
        }

        foreach (var calculatedData in dataDictionary)
        {
            worldData.chunkDataDictionary.Add(calculatedData.Key, calculatedData.Value);
        }
        
        ConcurrentDictionary<Vector3Int, MeshData> meshDataDictionary = new ConcurrentDictionary<Vector3Int, MeshData>();
        List<ChunkData> dataToRender = worldData.chunkDataDictionary
            .Where(keyValuePair => worldGenerationData.chunkPositionsToCreate.Contains(keyValuePair.Key))
            .Select(keyValuePair => keyValuePair.Value)
            .ToList();

        
        try
        {
            meshDataDictionary = await CreateMeshDataAsync(dataToRender);
        }
        catch (Exception)
        {
            Debug.Log("Task cancelled");
            return;
        }
        StartCoroutine(chunckCreationCoroutine(meshDataDictionary));
    }

    private Task<ConcurrentDictionary<Vector3Int, ChunkData>> CalculateWorldChunkData(List<Vector3Int> chunkDataPositionsToCreate)
    {
        ConcurrentDictionary<Vector3Int, ChunkData> chunkDataDictionary = new ConcurrentDictionary<Vector3Int, ChunkData>();

        return Task.Run(() => {
            foreach (Vector3Int pos in chunkDataPositionsToCreate)
            {
                if (taskTokenSource.Token.IsCancellationRequested)
                    taskTokenSource.Token.ThrowIfCancellationRequested();
                ChunkData data = new ChunkData(chunkSize, chunkHeight, this, pos);
                ChunkData newData = terrainGenerator.GenerateChunkData(data, mapSeedOffset);
                chunkDataDictionary.TryAdd(pos, newData);
            }
            return chunkDataDictionary;
        },
        taskTokenSource.Token
        );

    }

    private Task<ConcurrentDictionary<Vector3Int, MeshData>> CreateMeshDataAsync(List<ChunkData> dataToRender)
    {
        ConcurrentDictionary<Vector3Int, MeshData> meshDataDictionary = new ConcurrentDictionary<Vector3Int, MeshData>();

        return Task.Run(() => {
            foreach (ChunkData data in dataToRender)
            {
                if (taskTokenSource.Token.IsCancellationRequested)
                    taskTokenSource.Token.ThrowIfCancellationRequested();
                MeshData meshData = Chunk.GetChunkMeshData(data);
                meshDataDictionary.TryAdd(data.worldPosition, meshData);
            }
            return meshDataDictionary;
        },
        taskTokenSource.Token
        );

    }

    IEnumerator chunckCreationCoroutine(ConcurrentDictionary<Vector3Int, MeshData> meshDataDictionary)
    {
        foreach (var item in meshDataDictionary)
        {
            CreateChunk(worldData, item.Key, item.Value);
            yield return new WaitForEndOfFrame();
        }

        if (IsWorldCreated == false)
        {
            IsWorldCreated = true;
            OnWorldCreated?.Invoke();
        }
            
    }

    private void CreateChunk(WorldData worldData, Vector3Int position, MeshData meshData)
    {
        ChunkRenderer chunkRenderer = worldRenderer.RenderChunk(worldData, position, meshData);
        worldData.chunkDictionary.Add(position, chunkRenderer);
        OnChunkRendered?.Invoke(position, chunkRenderer);
    }
        

    internal bool SetVoxel(RaycastHit hit, VoxelType voxelType)
    {
        ChunkRenderer chunk = hit.collider.GetComponent<ChunkRenderer>();
        if (chunk == null)
            return false;

        Vector3Int pos = GetVoxelPos(hit);

        WorldDataHelper.SetVoxel(chunk.ChunkData.worldReference, pos, voxelType);
        chunk.ModifiedByThePlayer = true;

        if (Chunk.IsOnEdge(chunk.ChunkData, pos))
        {
            List<ChunkData> neighbourDataList = Chunk.GetEdgeNeighbourChunk(chunk.ChunkData, pos);
            foreach (ChunkData neighbourData in neighbourDataList)
            {
                //neighbourData.modifiedByThePlayer = true;
                ChunkRenderer chunkToUpdate = WorldDataHelper.GetChunk(neighbourData.worldReference, neighbourData.worldPosition);
                if (chunkToUpdate != null)
                    chunkToUpdate.UpdateChunk();
            }

        }

        chunk.UpdateChunk();
        return true;
    }

    private static Vector3Int GetVoxelPos(RaycastHit hit)
    {
        return VoxelMetrics.WorldHitToTargetVoxelIndex(hit);
    }


    private WorldGenerationData GetPositionsThatPlayerSees(Vector3Int playerPosition)
    {
        List<Vector3Int> allChunkPositionsNeeded = WorldDataHelper.GetChunkPositionsAroundPlayer(this, playerPosition);
        List<Vector3Int> allChunkDataPositionsNeeded = WorldDataHelper.GetDataPositionsAroundPlayer(this, playerPosition);

        List<Vector3Int> chunkPositionsToCreate = WorldDataHelper.SelectPositonsToCreate(worldData, allChunkPositionsNeeded, playerPosition);
        List<Vector3Int> chunkDataPositionsToCreate = WorldDataHelper.SelectDataPositonsToCreate(worldData, allChunkDataPositionsNeeded, playerPosition);

        List<Vector3Int> chunkPositionsToRemove = WorldDataHelper.GetUnnededChunks(worldData, allChunkPositionsNeeded);
        List<Vector3Int> chunkDataToRemove = WorldDataHelper.GetUnnededData(worldData, allChunkDataPositionsNeeded);

        WorldGenerationData data = new WorldGenerationData
        {
            chunkPositionsToCreate = chunkPositionsToCreate,
            chunkDataPositionsToCreate = chunkDataPositionsToCreate,
            chunkPositionsToRemove = chunkPositionsToRemove,
            chunkDataToRemove = chunkDataToRemove,
            chunkPositionsToUpdate = new List<Vector3Int>()
        };
        return data;

    }

    internal async void LoadAdditionalChunksRequest(GameObject player)
    {
        Debug.Log("Load more chunks");
        await GenerateWorld(VoxelMetrics.WorldToVoxelCoord(player.transform.position));
        OnNewChunksGenerated?.Invoke();
    }

    internal VoxelType GetVoxelFromChunkCoordinates(ChunkData chunkData, int x, int y, int z)
    {
        Vector3Int pos = Chunk.ChunkPositionFromVoxelCoords(this, x, y, z);
        ChunkData containerChunk = null;

        worldData.chunkDataDictionary.TryGetValue(pos, out containerChunk);

        if (containerChunk == null)
            return VoxelType.Nothing;
        Vector3Int voxelInChunkCoordinates = Chunk.GetVoxelInChunkCoordinates(containerChunk, new Vector3Int(x, y, z));
        return Chunk.GetVoxelFromChunkCoordinates(containerChunk, voxelInChunkCoordinates);
    }

    public void OnDisable()
    {
        taskTokenSource.Cancel();
    }

    public struct WorldGenerationData
    {
        public List<Vector3Int> chunkPositionsToCreate;
        public List<Vector3Int> chunkDataPositionsToCreate;
        public List<Vector3Int> chunkPositionsToRemove;
        public List<Vector3Int> chunkDataToRemove;
        public List<Vector3Int> chunkPositionsToUpdate;
    }


}
public struct WorldData
{
    public Dictionary<Vector3Int, ChunkData> chunkDataDictionary;
    public Dictionary<Vector3Int, ChunkRenderer> chunkDictionary;
    public int chunkSize;
    public int chunkHeight;
}