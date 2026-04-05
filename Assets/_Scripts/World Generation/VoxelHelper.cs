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

        bool isSmooth = VoxelDataManager.voxelTextureDataDictionary[voxelType].isSmoothable;
        bool forceCube = false;
        VoxelType renderType = voxelType;

        if (isSmooth)
        {
            bool hasAnyNeighbor = false;
            foreach (Direction dir in directions) {
                var nvt = Chunk.GetVoxelFromChunkCoordinates(chunk, new Vector3Int(x, y, z) + dir.GetVector());
                if (nvt != VoxelType.Nothing && nvt != VoxelType.Air && nvt != VoxelType.Water) {
                    hasAnyNeighbor = true;
                    break;
                }
            }
            if (!hasAnyNeighbor) forceCube = true;
        }
        else
        {
            foreach (Direction dir in directions) {
                var nvt = Chunk.GetVoxelFromChunkCoordinates(chunk, new Vector3Int(x, y, z) + dir.GetVector());
                if (nvt != VoxelType.Nothing && nvt != VoxelType.Air && nvt != VoxelType.Water) {
                    if (VoxelDataManager.voxelTextureDataDictionary.ContainsKey(nvt) && VoxelDataManager.voxelTextureDataDictionary[nvt].isSmoothable) {
                        renderType = nvt;
                        break;
                    }
                }
            }
        }

        foreach (Direction direction in directions)
        {
            var neighborVoxelCoordinates = new Vector3Int(x, y, z) + direction.GetVector();
            var neighborVoxelType = Chunk.GetVoxelFromChunkCoordinates(chunk, neighborVoxelCoordinates);

            if (neighborVoxelType != VoxelType.Nothing)
            {
                bool neighborSolid = VoxelDataManager.voxelTextureDataDictionary[neighborVoxelType].isSolid;
                bool neighborSmoothable = VoxelDataManager.voxelTextureDataDictionary[neighborVoxelType].isSmoothable;

                if (!neighborSolid || (neighborSmoothable && !isSmooth))
                {
                    if (voxelType == VoxelType.Water)
                    {
                        if (neighborVoxelType == VoxelType.Air)
                            meshData.waterMesh = GetFaceDataIn(direction, chunk, x, y, z, meshData.waterMesh, voxelType, renderType, forceCube);
                    }
                    else
                    {
                        meshData = GetFaceDataIn(direction, chunk, x, y, z, meshData, voxelType, renderType, forceCube);
                    }
                }
            }
        }

        return meshData;
    }

    private static bool IsSolid(ChunkData chunk, int x, int y, int z)
    {
        VoxelType vt = Chunk.GetVoxelFromChunkCoordinates(chunk, x, y, z);
        return vt != VoxelType.Nothing && vt != VoxelType.Air && vt != VoxelType.Water && VoxelDataManager.voxelTextureDataDictionary.ContainsKey(vt) && VoxelDataManager.voxelTextureDataDictionary[vt].isSolid;
    }

    private static Vector3 GetSmoothedCorner(ChunkData chunk, int x, int y, int z, int sx, int sy, int sz)
    {
        int curX = sx, curY = sy, curZ = sz;
        bool flippedX = false, flippedY = false, flippedZ = false;

        for (int i = 0; i < 3; i++)
        {
            bool airX = !flippedX && ((curX == 1) ? !IsSolid(chunk, x + 1, y, z) : !IsSolid(chunk, x - 1, y, z));
            bool airY = !flippedY && ((curY == 1) ? !IsSolid(chunk, x, y + 1, z) : !IsSolid(chunk, x, y - 1, z));
            bool airZ = !flippedZ && ((curZ == 1) ? !IsSolid(chunk, x, y, z + 1) : !IsSolid(chunk, x, y, z - 1));

            int numExposed = (airX ? 1 : 0) + (airY ? 1 : 0) + (airZ ? 1 : 0);
            
            if (numExposed >= 2)
            {
                if (airY) { curY = -curY; flippedY = true; }
                else if (airX) { curX = -curX; flippedX = true; }
                else if (airZ) { curZ = -curZ; flippedZ = true; }
            }
            else if (numExposed == 1)
            {
                if (airY && !IsSolid(chunk, x + curX, y, z + curZ)) { curY = -curY; flippedY = true; }
                else if (airX && !IsSolid(chunk, x, y + curY, z + curZ)) { curX = -curX; flippedX = true; }
                else if (airZ && !IsSolid(chunk, x + curX, y + curY, z)) { curZ = -curZ; flippedZ = true; }
                else break;
            }
            else break;
        }

        return new Vector3(x + curX * 0.5f, y + curY * 0.5f, z + curZ * 0.5f) * VoxelMetrics.Size;
    }

    private static Vector3 GetStickyCorner(ChunkData chunk, int x, int y, int z, int dx, int dy, int dz)
    {
        bool hasSolid = false;
        int smoothCount = 1; // Current block is smoothable
        
        int nx1 = x + dx, ny1 = y, nz1 = z;
        int nx2 = x, ny2 = y + dy, nz2 = z;
        int nx3 = x, ny3 = y, nz3 = z + dz;
        
        VoxelType vt1 = Chunk.GetVoxelFromChunkCoordinates(chunk, nx1, ny1, nz1);
        VoxelType vt2 = Chunk.GetVoxelFromChunkCoordinates(chunk, nx2, ny2, nz2);
        VoxelType vt3 = Chunk.GetVoxelFromChunkCoordinates(chunk, nx3, ny3, nz3);

        VoxelType[] vts = { vt1, vt2, vt3 };
        
        foreach (var vt in vts) {
            if (vt != VoxelType.Air && vt != VoxelType.Nothing && vt != VoxelType.Water) {
                if (VoxelDataManager.voxelTextureDataDictionary.ContainsKey(vt)) {
                    if (VoxelDataManager.voxelTextureDataDictionary[vt].isSmoothable) {
                        smoothCount++;
                    } else if (VoxelDataManager.voxelTextureDataDictionary[vt].isSolid) {
                        hasSolid = true;
                    }
                }
            }
        }

        Vector3 basePos = new Vector3(dx * 0.5f, dy * 0.5f, dz * 0.5f);

        if (hasSolid || smoothCount > 1) {
            return new Vector3(x + basePos.x, y + basePos.y, z + basePos.z) * VoxelMetrics.Size;
        }

        return new Vector3(x, y, z) * VoxelMetrics.Size;
    }

    public static MeshData GetFaceDataIn(Direction direction, ChunkData chunk, int x, int y, int z, MeshData meshData, VoxelType voxelType, VoxelType renderType, bool forceCube = false)
    {
        int initialVerts = meshData.vertices.Count;
        bool splitAlternative = GetFaceVertices(direction, chunk, x, y, z, meshData, voxelType, forceCube);

        if (meshData.vertices.Count == initialVerts)
            return meshData;

        meshData.AddQuadTriangles(VoxelDataManager.voxelTextureDataDictionary[voxelType].generatesCollider, splitAlternative);
        meshData.uvs.AddRange(FaceUVs(direction, renderType));

        return meshData;
    }

    public static bool GetFaceVertices(Direction direction, ChunkData chunk, int x, int y, int z, MeshData meshData, VoxelType voxelType, bool forceCube = false)
    {
        var generatesCollider = VoxelDataManager.voxelTextureDataDictionary[voxelType].generatesCollider;
        bool isSmoothable = VoxelDataManager.voxelTextureDataDictionary[voxelType].isSmoothable;
        float s = VoxelMetrics.Size;
        bool splitAlternative = false;

        if (isSmoothable && !forceCube)
        {
            Vector3 v0 = Vector3.zero, v1 = Vector3.zero, v2 = Vector3.zero, v3 = Vector3.zero;

            switch (direction)
            {
                case Direction.backwards:
                    v0 = GetStickyCorner(chunk, x, y, z, -1, -1, -1);
                    v1 = GetStickyCorner(chunk, x, y, z, -1, 1, -1);
                    v2 = GetStickyCorner(chunk, x, y, z, 1, 1, -1);
                    v3 = GetStickyCorner(chunk, x, y, z, 1, -1, -1);
                    break;
                case Direction.forward:
                    v0 = GetStickyCorner(chunk, x, y, z, 1, -1, 1);
                    v1 = GetStickyCorner(chunk, x, y, z, 1, 1, 1);
                    v2 = GetStickyCorner(chunk, x, y, z, -1, 1, 1);
                    v3 = GetStickyCorner(chunk, x, y, z, -1, -1, 1);
                    break;
                case Direction.left:
                    v0 = GetStickyCorner(chunk, x, y, z, -1, -1, 1);
                    v1 = GetStickyCorner(chunk, x, y, z, -1, 1, 1);
                    v2 = GetStickyCorner(chunk, x, y, z, -1, 1, -1);
                    v3 = GetStickyCorner(chunk, x, y, z, -1, -1, -1);
                    break;
                case Direction.right:
                    v0 = GetStickyCorner(chunk, x, y, z, 1, -1, -1);
                    v1 = GetStickyCorner(chunk, x, y, z, 1, 1, -1);
                    v2 = GetStickyCorner(chunk, x, y, z, 1, 1, 1);
                    v3 = GetStickyCorner(chunk, x, y, z, 1, -1, 1);
                    break;
                case Direction.down:
                    v0 = GetStickyCorner(chunk, x, y, z, -1, -1, -1);
                    v1 = GetStickyCorner(chunk, x, y, z, 1, -1, -1);
                    v2 = GetStickyCorner(chunk, x, y, z, 1, -1, 1);
                    v3 = GetStickyCorner(chunk, x, y, z, -1, -1, 1);
                    break;
                case Direction.up:
                    v0 = GetStickyCorner(chunk, x, y, z, -1, 1, 1);
                    v1 = GetStickyCorner(chunk, x, y, z, 1, 1, 1);
                    v2 = GetStickyCorner(chunk, x, y, z, 1, 1, -1);
                    v3 = GetStickyCorner(chunk, x, y, z, -1, 1, -1);
                    break;
            }


            if (Vector3.Cross(v1 - v0, v2 - v0).sqrMagnitude < 0.0001f && 
                Vector3.Cross(v2 - v0, v3 - v0).sqrMagnitude < 0.0001f)
            {
                return false;
            }

            if (Vector3.SqrMagnitude(v1 - v3) < Vector3.SqrMagnitude(v0 - v2))
                splitAlternative = true;

            meshData.AddVertex(v0, generatesCollider);
            meshData.AddVertex(v1, generatesCollider);
            meshData.AddVertex(v2, generatesCollider);
            meshData.AddVertex(v3, generatesCollider);

            return splitAlternative;
        }

        // Local cell corners in index space (±0.5), scaled to meters for mesh under chunk origin.
        switch (direction)
        {
            case Direction.backwards:
                meshData.AddVertex(new Vector3(x - 0.5f, y - 0.5f, z - 0.5f) * s, generatesCollider);
                meshData.AddVertex(new Vector3(x - 0.5f, y + 0.5f, z - 0.5f) * s, generatesCollider);
                meshData.AddVertex(new Vector3(x + 0.5f, y + 0.5f, z - 0.5f) * s, generatesCollider);
                meshData.AddVertex(new Vector3(x + 0.5f, y - 0.5f, z - 0.5f) * s, generatesCollider);
                break;
            case Direction.forward:
                meshData.AddVertex(new Vector3(x + 0.5f, y - 0.5f, z + 0.5f) * s, generatesCollider);
                meshData.AddVertex(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * s, generatesCollider);
                meshData.AddVertex(new Vector3(x - 0.5f, y + 0.5f, z + 0.5f) * s, generatesCollider);
                meshData.AddVertex(new Vector3(x - 0.5f, y - 0.5f, z + 0.5f) * s, generatesCollider);
                break;
            case Direction.left:
                meshData.AddVertex(new Vector3(x - 0.5f, y - 0.5f, z + 0.5f) * s, generatesCollider);
                meshData.AddVertex(new Vector3(x - 0.5f, y + 0.5f, z + 0.5f) * s, generatesCollider);
                meshData.AddVertex(new Vector3(x - 0.5f, y + 0.5f, z - 0.5f) * s, generatesCollider);
                meshData.AddVertex(new Vector3(x - 0.5f, y - 0.5f, z - 0.5f) * s, generatesCollider);
                break;

            case Direction.right:
                meshData.AddVertex(new Vector3(x + 0.5f, y - 0.5f, z - 0.5f) * s, generatesCollider);
                meshData.AddVertex(new Vector3(x + 0.5f, y + 0.5f, z - 0.5f) * s, generatesCollider);
                meshData.AddVertex(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * s, generatesCollider);
                meshData.AddVertex(new Vector3(x + 0.5f, y - 0.5f, z + 0.5f) * s, generatesCollider);
                break;
            case Direction.down:
                meshData.AddVertex(new Vector3(x - 0.5f, y - 0.5f, z - 0.5f) * s, generatesCollider);
                meshData.AddVertex(new Vector3(x + 0.5f, y - 0.5f, z - 0.5f) * s, generatesCollider);
                meshData.AddVertex(new Vector3(x + 0.5f, y - 0.5f, z + 0.5f) * s, generatesCollider);
                meshData.AddVertex(new Vector3(x - 0.5f, y - 0.5f, z + 0.5f) * s, generatesCollider);
                break;
            case Direction.up:
                meshData.AddVertex(new Vector3(x - 0.5f, y + 0.5f, z + 0.5f) * s, generatesCollider);
                meshData.AddVertex(new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * s, generatesCollider);
                meshData.AddVertex(new Vector3(x + 0.5f, y + 0.5f, z - 0.5f) * s, generatesCollider);
                meshData.AddVertex(new Vector3(x - 0.5f, y + 0.5f, z - 0.5f) * s, generatesCollider);
                break;
            default:
                break;
        }

        return false;
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
