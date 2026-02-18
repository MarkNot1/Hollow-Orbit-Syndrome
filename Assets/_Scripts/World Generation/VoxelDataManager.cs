using System.Collections.Generic;
using UnityEngine;

public class VoxelDataManager : MonoBehaviour
{

    public static float textureOffset = 0.001f;
    public static float tileSizeX, tileSizeY;
    public static Dictionary<VoxelType, TextureData> voxelTextureDataDictionary = new Dictionary<VoxelType, TextureData>();
    public VoxelDataSO textureData;


    private void Awake()
    {
        foreach (var item in textureData.textureDataList)
        {
            if (voxelTextureDataDictionary.ContainsKey(item.voxelType) == false)
            {
                voxelTextureDataDictionary.Add(item.voxelType, item);

            };
        }
        tileSizeX = textureData.textureSizeX;
        tileSizeY = textureData.textureSizeY;
    }
}
