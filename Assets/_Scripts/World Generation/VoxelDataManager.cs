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
        RebuildRuntimeData();
    }

    public void RebuildRuntimeData()
    {
        voxelTextureDataDictionary.Clear();
        foreach (var item in textureData.textureDataList)
        {
            if (voxelTextureDataDictionary.ContainsKey(item.voxelType) == false)
            {
                voxelTextureDataDictionary.Add(item.voxelType, item);

            };
        }
        tileSizeX = textureData.textureSizeX;
        tileSizeY = textureData.textureSizeY;
        VoxelRuntimeData.Capture(textureData);
    }
}

public readonly struct VoxelRenderInfo
{
    public readonly bool isDefined;
    public readonly bool isSolid;
    public readonly bool generatesCollider;
    public readonly bool isSmoothable;
    public readonly Vector2Int up;
    public readonly Vector2Int down;
    public readonly Vector2Int side;
    public readonly Color32 farColor;

    public VoxelRenderInfo(TextureData data)
    {
        isDefined = data != null;
        isSolid = data != null && data.isSolid;
        generatesCollider = data != null && data.generatesCollider;
        isSmoothable = data != null && data.isSmoothable;
        up = data != null ? data.up : Vector2Int.zero;
        down = data != null ? data.down : Vector2Int.zero;
        side = data != null ? data.side : Vector2Int.zero;
        farColor = FarColorFor(data != null ? data.voxelType : VoxelType.Air);
    }

    private static Color32 FarColorFor(VoxelType type)
    {
        return type switch
        {
            // Palette anchors supplied as #RRGGBB values. Ship metal shares the
            // primary metal swatch so distant structural surfaces stay coherent.
            VoxelType.Infected_Growth => new Color32(0x41, 0x25, 0x6E, 255),
            VoxelType.Ship_Metal => new Color32(0x11, 0x35, 0x37, 255),
            VoxelType.Metal => new Color32(0x11, 0x35, 0x37, 255),
            VoxelType.Rock => new Color32(54, 61, 68, 255),
            _ => new Color32(90, 90, 90, 255)
        };
    }
}

/// <summary>Immutable value-only voxel rendering data used by mesh workers.</summary>
public static class VoxelRuntimeData
{
    private static VoxelRenderInfo[] infos = new VoxelRenderInfo[0];
    public static float TileSizeX { get; private set; }
    public static float TileSizeY { get; private set; }

    public static void Capture(VoxelDataSO source)
    {
        int count = System.Enum.GetValues(typeof(VoxelType)).Length;
        var captured = new VoxelRenderInfo[count];
        if (source != null && source.textureDataList != null)
        {
            foreach (TextureData data in source.textureDataList)
            {
                int index = (int)data.voxelType;
                if (index >= 0 && index < captured.Length)
                    captured[index] = new VoxelRenderInfo(data);
            }
            TileSizeX = source.textureSizeX;
            TileSizeY = source.textureSizeY;
        }
        infos = captured;
    }

    public static bool TryGet(VoxelType type, out VoxelRenderInfo info)
    {
        int index = (int)type;
        if (index >= 0 && index < infos.Length && infos[index].isDefined)
        {
            info = infos[index];
            return true;
        }
        info = default;
        return false;
    }

    public static bool IsSolid(VoxelType type)
    {
        return type != VoxelType.Nothing && type != VoxelType.Air &&
               TryGet(type, out VoxelRenderInfo info) && info.isSolid;
    }

    public static Vector2Int TexturePosition(Direction direction, VoxelType type)
    {
        if (!TryGet(type, out VoxelRenderInfo info))
            return Vector2Int.zero;
        return direction == Direction.up ? info.up :
               direction == Direction.down ? info.down : info.side;
    }

    public static Color32 FarColor(VoxelType type)
    {
        return TryGet(type, out VoxelRenderInfo info)
            ? info.farColor
            : new Color32(90, 90, 90, 255);
    }
}
