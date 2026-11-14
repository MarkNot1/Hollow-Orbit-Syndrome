using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class StructureData
{
    public int version = 1;
    public string id;
    public string name = "Structure";
    public bool hasMasterFrame;
    public Vector3 position;
    public Quaternion rotation = Quaternion.identity;
    public List<StructureVoxelData> voxels = new();
    public List<StructurePrefabData> prefabs = new();

    public StructureData ConnectedCopy(out HashSet<Vector3Int> connected)
    {
        StructureData copy = new() { id = id, name = name, hasMasterFrame = hasMasterFrame, position = position, rotation = rotation };
        Dictionary<Vector3Int, VoxelType> cells = new();
        foreach (var voxel in voxels) cells.Add(voxel.position, voxel.type);
        connected = new();
        if (hasMasterFrame && cells.TryGetValue(Vector3Int.zero, out VoxelType root))
        {
            connected.Add(Vector3Int.zero);
            copy.voxels.Add(new StructureVoxelData(Vector3Int.zero, root));
        }
        for (int i = 0; i < copy.voxels.Count; i++)
            foreach (Vector3Int normal in VoxelStructure.FaceNormals)
            {
                Vector3Int cell = copy.voxels[i].position + normal;
                if (cells.TryGetValue(cell, out VoxelType type) && connected.Add(cell))
                    copy.voxels.Add(new StructureVoxelData(cell, type));
            }
        foreach (var prefab in prefabs)
            if (connected.Contains(prefab.surfaceVoxel)) copy.prefabs.Add(prefab);
        copy.voxels.Sort((a, b) => VoxelStructure.ComparePosition(a.position, b.position));
        return copy;
    }

    public Bounds WorldBounds()
    {
        Bounds bounds = new(position, Vector3.zero);
        foreach (var voxel in voxels)
            bounds.Encapsulate(position + rotation * ((Vector3)voxel.position * VoxelMetrics.Size));
        bounds.Expand(Vector3.one * (VoxelMetrics.Size * 1.733f));
        return bounds;
    }
}

[Serializable]
public struct StructureVoxelData
{
    public Vector3Int position;
    public VoxelType type;

    public StructureVoxelData(Vector3Int position, VoxelType type)
    {
        this.position = position;
        this.type = type;
    }
}

[Serializable]
public sealed class StructurePrefabData
{
    public string id;
    public string prefabId;
    public Vector3Int surfaceVoxel;
    public Vector3Int normal;
    public Vector3 position;
    public Quaternion rotation = Quaternion.identity;
    public Vector3 scale = Vector3.one;
    public string state;
}

// A prefab owns its state format; the structure stores it without scene references.
public interface IStructurePrefabState
{
    string CaptureState();
    void RestoreState(string json);
}

[Serializable]
public sealed class StructureSaveData
{
    public int version = 1;
    public List<StructureData> structures = new();
}
