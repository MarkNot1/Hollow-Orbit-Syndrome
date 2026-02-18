using System;
using UnityEngine;

public abstract class VoxelLayerHandler : MonoBehaviour
{
    [SerializeField]
    private VoxelLayerHandler Next;

    public bool Handle(ChunkData data, int x, int y, int z, int surfaceHeightNoise , Vector2Int mapSeedOffset)
    {
       if (tryHandling(data, x, y, z, surfaceHeightNoise, mapSeedOffset))
        {
            return true;
        }
        if (Next != null)
        {
            return Next.Handle(data, x, y, z, surfaceHeightNoise, mapSeedOffset);
        }
        return false;
    }

    protected abstract bool tryHandling(ChunkData data, int x, int y, int z, int surfaceHeightNoise, Vector2Int mapSeedOffset);
}
