using UnityEngine;

public static class VoxelHelper
{
 
    private static Direction[] directions =
    {
        Direction.backwards,
        Direction.down,
        Direction.forward,
        Direction.left,
        Direction.right,
        Direction.up
    };

    public static MeshData GetMeshData
        (ChunkData chunk, int x, int y, int z, MeshData meshData, VoxelType voxelType)
    {
        if (voxelType == VoxelType.Air || voxelType == VoxelType.Nothing)
            return meshData;

        foreach (Direction direction in directions)
        {
            var neighborVoxelCoordinates = new Vector3Int(x, y, z) + direction.GetVector();
            var neighborVoxelType = Chunk.GetVoxelFromChunkCoordinates(chunk, neighborVoxelCoordinates);

            if (neighborVoxelType != VoxelType.Nothing && VoxelDataManager.voxelTextureDataDictionary[neighborVoxelType].isSolid == false)
            {

                if (voxelType == VoxelType.Water)
                {
                    if (neighborVoxelType == VoxelType.Air)
                        meshData.waterMesh = GetFaceDataIn(direction, chunk, x, y, z, meshData.waterMesh, voxelType);
                }
                else
                {
                    meshData = GetFaceDataIn(direction, chunk, x, y, z, meshData, voxelType);
                }

            }
        }

        return meshData;
    }

    public static MeshData GetFaceDataIn(Direction direction, ChunkData chunk, int x, int y, int z, MeshData meshData, VoxelType voxelType)
    {
        GetFaceVertices(direction, x, y, z, meshData, voxelType);
        meshData.AddQuadTriangles(VoxelDataManager.voxelTextureDataDictionary[voxelType].generatesCollider);
        meshData.uvs.AddRange(FaceUVs(direction, voxelType));


        return meshData;
    }

    public static void GetFaceVertices(Direction direction, int x, int y, int z, MeshData meshData, VoxelType voxelType)
    {
        var generatesCollider = VoxelDataManager.voxelTextureDataDictionary[voxelType].generatesCollider;
        //order of vertices matters for the normals and how we render the mesh
        switch (direction)
        {
            case Direction.backwards:
                meshData.AddVertex(new Vector3(x - 0.5f, y - 0.5f, z - 0.5f), generatesCollider);
                meshData.AddVertex(new Vector3(x - 0.5f, y + 0.5f, z - 0.5f), generatesCollider);
                meshData.AddVertex(new Vector3(x + 0.5f, y + 0.5f, z - 0.5f), generatesCollider);
                meshData.AddVertex(new Vector3(x + 0.5f, y - 0.5f, z - 0.5f), generatesCollider);
                break;
            case Direction.forward:
                meshData.AddVertex(new Vector3(x + 0.5f, y - 0.5f, z + 0.5f), generatesCollider);
                meshData.AddVertex(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f), generatesCollider);
                meshData.AddVertex(new Vector3(x - 0.5f, y + 0.5f, z + 0.5f), generatesCollider);
                meshData.AddVertex(new Vector3(x - 0.5f, y - 0.5f, z + 0.5f), generatesCollider);
                break;
            case Direction.left:
                meshData.AddVertex(new Vector3(x - 0.5f, y - 0.5f, z + 0.5f), generatesCollider);
                meshData.AddVertex(new Vector3(x - 0.5f, y + 0.5f, z + 0.5f), generatesCollider);
                meshData.AddVertex(new Vector3(x - 0.5f, y + 0.5f, z - 0.5f), generatesCollider);
                meshData.AddVertex(new Vector3(x - 0.5f, y - 0.5f, z - 0.5f), generatesCollider);
                break;

            case Direction.right:
                meshData.AddVertex(new Vector3(x + 0.5f, y - 0.5f, z - 0.5f), generatesCollider);
                meshData.AddVertex(new Vector3(x + 0.5f, y + 0.5f, z - 0.5f), generatesCollider);
                meshData.AddVertex(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f), generatesCollider);
                meshData.AddVertex(new Vector3(x + 0.5f, y - 0.5f, z + 0.5f), generatesCollider);
                break;
            case Direction.down:
                meshData.AddVertex(new Vector3(x - 0.5f, y - 0.5f, z - 0.5f), generatesCollider);
                meshData.AddVertex(new Vector3(x + 0.5f, y - 0.5f, z - 0.5f), generatesCollider);
                meshData.AddVertex(new Vector3(x + 0.5f, y - 0.5f, z + 0.5f), generatesCollider);
                meshData.AddVertex(new Vector3(x - 0.5f, y - 0.5f, z + 0.5f), generatesCollider);
                break;
            case Direction.up:
                meshData.AddVertex(new Vector3(x - 0.5f, y + 0.5f, z + 0.5f), generatesCollider);
                meshData.AddVertex(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f), generatesCollider);
                meshData.AddVertex(new Vector3(x + 0.5f, y + 0.5f, z - 0.5f), generatesCollider);
                meshData.AddVertex(new Vector3(x - 0.5f, y + 0.5f, z - 0.5f), generatesCollider);
                break;
            default:
                break;
        }
    }

    public static Vector2[] FaceUVs(Direction direction, VoxelType voxelType)
    {
        Vector2[] UVs = new Vector2[4];
        var tilePos = TexturePosition(direction, voxelType);

        UVs[0] = new Vector2(VoxelDataManager.tileSizeX * tilePos.x + VoxelDataManager.tileSizeX - VoxelDataManager.textureOffset,
            VoxelDataManager.tileSizeY * tilePos.y + VoxelDataManager.textureOffset);

        UVs[1] = new Vector2(VoxelDataManager.tileSizeX * tilePos.x + VoxelDataManager.tileSizeX - VoxelDataManager.textureOffset,
            VoxelDataManager.tileSizeY * tilePos.y + VoxelDataManager.tileSizeY - VoxelDataManager.textureOffset);

        UVs[2] = new Vector2(VoxelDataManager.tileSizeX * tilePos.x + VoxelDataManager.textureOffset,
            VoxelDataManager.tileSizeY * tilePos.y + VoxelDataManager.tileSizeY - VoxelDataManager.textureOffset);

        UVs[3] = new Vector2(VoxelDataManager.tileSizeX * tilePos.x + VoxelDataManager.textureOffset,
            VoxelDataManager.tileSizeY * tilePos.y + VoxelDataManager.textureOffset);

        return UVs;
    }

    public static Vector2Int TexturePosition(Direction direction, VoxelType voxelType)
    {
        return direction switch
        {
            Direction.up => VoxelDataManager.voxelTextureDataDictionary[voxelType].up,
            Direction.down => VoxelDataManager.voxelTextureDataDictionary[voxelType].down,
            _ => VoxelDataManager.voxelTextureDataDictionary[voxelType].side
        };
    }
}
