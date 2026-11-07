using UnityEngine;

public static class FrameBlueprint
{
    public const int Size = 6;
    private const int Last = Size - 1;

    public static bool ContainsEdge(Vector3Int p)
    {
        if ((uint)p.x >= Size || (uint)p.y >= Size || (uint)p.z >= Size) return false;
        return (p.x is 0 or Last ? 1 : 0) + (p.y is 0 or Last ? 1 : 0) +
            (p.z is 0 or Last ? 1 : 0) >= 2;
    }

    public static VoxelType MasterVoxelType(Vector3Int p)
    {
        bool marker = p.y == 0 &&
            ((p.x is 2 or 3 && p.z is 0 or Last) || (p.z is 2 or 3 && p.x is 0 or Last));
        return marker ? VoxelType.Striped_Ship_Metal : VoxelType.Dark_Ship_Metal;
    }

    public static StructureData Create(bool master)
    {
        StructureData frame = new() { name = master ? "Master Frame" : "Metal Frame", hasMasterFrame = master };
        for (int x = 0; x < Size; x++)
        for (int y = 0; y < Size; y++)
        for (int z = 0; z < Size; z++)
        {
            Vector3Int position = new(x, y, z);
            if (ContainsEdge(position))
                frame.voxels.Add(new StructureVoxelData(position, master ? MasterVoxelType(position) : VoxelType.Dark_Ship_Metal));
        }
        return frame;
    }
}
