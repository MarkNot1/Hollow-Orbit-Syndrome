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
