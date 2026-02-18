using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using System;


[CreateAssetMenu(fileName = "VoxelData", menuName = "Data/VoxelData")]
public class VoxelDataSO : ScriptableObject
{
    public float textureSizeX, textureSizeY;
    public List<TextureData> textureDataList;

}

[Serializable]

public class TextureData
{
    public VoxelType voxelType;
    public Vector2Int up, down, side;
    public bool isSolid = true;
    public bool generatesCollider = true;
}
