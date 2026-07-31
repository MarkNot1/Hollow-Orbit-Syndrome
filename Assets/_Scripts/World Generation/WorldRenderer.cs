using System;
using System.Collections.Generic;
using UnityEngine;

public class WorldRenderer : MonoBehaviour
{
    public GameObject chunkPrefab;
    public Queue<ChunkRenderer> chunkPool = new Queue<ChunkRenderer>();
    [SerializeField, Min(8)] private int maxPooledChunks = 64;

    public void Clear(WorldData worldData)
    {
        foreach (var item in worldData.chunkDictionary.Values)
        {
            Destroy(item.gameObject);
        }
        foreach (var item in chunkPool)
        {
            Destroy(item.gameObject);
        }
        chunkPool.Clear();
    }

    internal ChunkRenderer RenderChunk(
        WorldData worldData,
        Vector3Int position)
    {
        ChunkRenderer newChunk = null;

        if (chunkPool.Count > 0)        {
            newChunk = chunkPool.Dequeue();
            newChunk.transform.position = VoxelMetrics.ChunkKeyToWorldOrigin(position);
        }
        else
        {
            GameObject chunkObject = Instantiate(chunkPrefab, VoxelMetrics.ChunkKeyToWorldOrigin(position), Quaternion.identity);
            newChunk = chunkObject.GetComponent<ChunkRenderer>();
        }

        newChunk.gameObject.SetActive(true);

        newChunk.InitializeChunk(worldData.chunkDataDictionary[position]);

        return newChunk;
    }

    public void RemoveChunk(ChunkRenderer chunk)
    {
        chunk.ResetForPool();
        chunk.gameObject.SetActive(false);
        if (chunkPool.Count >= maxPooledChunks)
        {
            // A streaming fault must not turn the renderer pool into permanent
            // mesh memory. Normal movement reuses far fewer than this cap.
            Destroy(chunk.gameObject);
            return;
        }
        chunkPool.Enqueue(chunk);
    }
}
