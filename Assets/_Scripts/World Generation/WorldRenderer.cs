using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WorldRenderer : MonoBehaviour
{
    public GameObject chunkPrefab;
    public Queue<ChunkRenderer> chunkPool = new Queue<ChunkRenderer>();

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

    internal ChunkRenderer RenderChunk(WorldData worldData, Vector3Int position, MeshData meshData)
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
        newChunk.UpdateChunk(meshData);

        return newChunk;
    }

    public void RemoveChunk(ChunkRenderer chunk)
    {
        chunk.gameObject.SetActive(false);
        chunkPool.Enqueue(chunk);
    }
}
