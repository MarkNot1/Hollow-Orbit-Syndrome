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

    private readonly struct AxisNeighbours
    {
        private readonly VoxelType left;
        private readonly VoxelType right;
        private readonly VoxelType down;
        private readonly VoxelType up;
        private readonly VoxelType back;
        private readonly VoxelType forward;

        public AxisNeighbours(ChunkMeshContext context, int x, int y, int z)
        {
            left = context.GetVoxel(x - 1, y, z);
            right = context.GetVoxel(x + 1, y, z);
            down = context.GetVoxel(x, y - 1, z);
            up = context.GetVoxel(x, y + 1, z);
            back = context.GetVoxel(x, y, z - 1);
            forward = context.GetVoxel(x, y, z + 1);
        }

        public VoxelType Get(Direction direction)
        {
            return direction switch
            {
                Direction.left => left,
                Direction.right => right,
                Direction.down => down,
                Direction.up => up,
                Direction.backwards => back,
                _ => forward
            };
        }

        public VoxelType GetAxis(int dx, int dy, int dz)
        {
            if (dx < 0) return left;
            if (dx > 0) return right;
            if (dy < 0) return down;
            if (dy > 0) return up;
            return dz < 0 ? back : forward;
        }
    }

    public static MeshData GetMeshData
        (ChunkData chunk, int x, int y, int z, MeshData meshData, VoxelType voxelType)
    {
        return GetMeshData(new ChunkMeshContext(chunk), x, y, z, meshData, voxelType);
    }

    public static MeshData GetMeshData
        (ChunkMeshContext context, int x, int y, int z, MeshData meshData, VoxelType voxelType, bool solidColor = false)
    {
        if (voxelType == VoxelType.Air || voxelType == VoxelType.Nothing)
            return meshData;

        if (!VoxelRuntimeData.TryGet(voxelType, out VoxelRenderInfo voxelInfo))
            return meshData;
        bool isSmooth = voxelInfo.isSmoothable;
        bool forceCube = false;
        VoxelType renderType = voxelType;
        var neighbours = new AxisNeighbours(context, x, y, z);

        if (isSmooth)
        {
            bool hasAnyNeighbor = false;
            foreach (Direction dir in directions) {
                VoxelType nvt = neighbours.Get(dir);
                if (nvt != VoxelType.Nothing && nvt != VoxelType.Air) {
                    hasAnyNeighbor = true;
                    break;
                }
            }
            if (!hasAnyNeighbor) forceCube = true;
        }
        else
        {
            foreach (Direction dir in directions) {
                VoxelType nvt = neighbours.Get(dir);
                if (nvt != VoxelType.Nothing && nvt != VoxelType.Air) {
                    if (VoxelRuntimeData.TryGet(nvt, out VoxelRenderInfo neighbourInfo) && neighbourInfo.isSmoothable) {
                        renderType = nvt;
                        break;
                    }
                }
            }
        }

        foreach (Direction direction in directions)
        {
            VoxelType neighborVoxelType = neighbours.Get(direction);

            if (neighborVoxelType != VoxelType.Nothing &&
                VoxelRuntimeData.TryGet(neighborVoxelType, out VoxelRenderInfo neighbourInfo))
            {
                bool neighborSolid = neighbourInfo.isSolid;
                bool neighborSmoothable = neighbourInfo.isSmoothable;

                if (!neighborSolid || (neighborSmoothable && !isSmooth))
                {
                    meshData = GetFaceDataIn(direction, context, x, y, z, meshData, voxelType, renderType, forceCube, neighbours, solidColor);
                }
            }
            else if (neighborVoxelType == VoxelType.Air)
            {
                meshData = GetFaceDataIn(direction, context, x, y, z, meshData, voxelType, renderType, forceCube, neighbours, solidColor);
            }
        }

        return meshData;
    }

    private static bool IsSolid(ChunkMeshContext context, int x, int y, int z)
    {
        return VoxelRuntimeData.IsSolid(context.GetVoxel(x, y, z));
    }

    private static Vector3 GetSmoothedCorner(ChunkMeshContext context, int x, int y, int z, int sx, int sy, int sz)
    {
        int curX = sx, curY = sy, curZ = sz;
        bool flippedX = false, flippedY = false, flippedZ = false;

        for (int i = 0; i < 3; i++)
        {
            bool airX = !flippedX && ((curX == 1) ? !IsSolid(context, x + 1, y, z) : !IsSolid(context, x - 1, y, z));
            bool airY = !flippedY && ((curY == 1) ? !IsSolid(context, x, y + 1, z) : !IsSolid(context, x, y - 1, z));
            bool airZ = !flippedZ && ((curZ == 1) ? !IsSolid(context, x, y, z + 1) : !IsSolid(context, x, y, z - 1));

            int numExposed = (airX ? 1 : 0) + (airY ? 1 : 0) + (airZ ? 1 : 0);
            
            if (numExposed >= 2)
            {
                if (airY) { curY = -curY; flippedY = true; }
                else if (airX) { curX = -curX; flippedX = true; }
                else if (airZ) { curZ = -curZ; flippedZ = true; }
            }
            else if (numExposed == 1)
            {
                if (airY && !IsSolid(context, x + curX, y, z + curZ)) { curY = -curY; flippedY = true; }
                else if (airX && !IsSolid(context, x, y + curY, z + curZ)) { curX = -curX; flippedX = true; }
                else if (airZ && !IsSolid(context, x + curX, y + curY, z)) { curZ = -curZ; flippedZ = true; }
                else break;
            }
            else break;
        }

        return new Vector3(x + curX * 0.5f, y + curY * 0.5f, z + curZ * 0.5f) * VoxelMetrics.Size;
    }

    private static Vector3 GetStickyCorner(AxisNeighbours neighbours, int x, int y, int z, int dx, int dy, int dz)
    {
        bool hasSolid = false;
        int smoothCount = 1; // Current block is smoothable
        
        AccumulateSticky(neighbours.GetAxis(dx, 0, 0), ref hasSolid, ref smoothCount);
        AccumulateSticky(neighbours.GetAxis(0, dy, 0), ref hasSolid, ref smoothCount);
        AccumulateSticky(neighbours.GetAxis(0, 0, dz), ref hasSolid, ref smoothCount);

        Vector3 basePos = new Vector3(dx * 0.5f, dy * 0.5f, dz * 0.5f);

        if (hasSolid || smoothCount > 1) {
            return new Vector3(x + basePos.x, y + basePos.y, z + basePos.z) * VoxelMetrics.Size;
        }

        return new Vector3(x, y, z) * VoxelMetrics.Size;
    }

    private static void AccumulateSticky(VoxelType type, ref bool hasSolid, ref int smoothCount)
    {
        if (type == VoxelType.Air || type == VoxelType.Nothing ||
            !VoxelRuntimeData.TryGet(type, out VoxelRenderInfo info))
            return;
        if (info.isSmoothable)
            smoothCount++;
        else if (info.isSolid)
            hasSolid = true;
    }

    public static MeshData GetFaceDataIn(Direction direction, ChunkData chunk, int x, int y, int z, MeshData meshData, VoxelType voxelType, VoxelType renderType, bool forceCube = false)
    {
        return GetFaceDataIn(direction, new ChunkMeshContext(chunk), x, y, z, meshData, voxelType, renderType, forceCube);
    }

    public static MeshData GetFaceDataIn(Direction direction, ChunkMeshContext context, int x, int y, int z, MeshData meshData, VoxelType voxelType, VoxelType renderType, bool forceCube = false)
    {
        return GetFaceDataIn(
            direction, context, x, y, z, meshData, voxelType, renderType,
            forceCube, new AxisNeighbours(context, x, y, z), false);
    }

    private static MeshData GetFaceDataIn(Direction direction, ChunkMeshContext context, int x, int y, int z, MeshData meshData, VoxelType voxelType, VoxelType renderType, bool forceCube, AxisNeighbours neighbours, bool solidColor)
    {
        int initialVerts = meshData.vertices.Count;
        bool splitAlternative = GetFaceVerticesCached(direction, context, x, y, z, meshData, voxelType, forceCube, neighbours);

        if (meshData.vertices.Count == initialVerts)
            return meshData;

        bool generatesCollider = VoxelRuntimeData.TryGet(voxelType, out VoxelRenderInfo info) && info.generatesCollider;
        meshData.AddQuadTriangles(generatesCollider, splitAlternative);
        meshData.AddQuadNormals(splitAlternative);
        AddFaceUvs(meshData, direction, renderType);
        Vector2 mode = solidColor ? new Vector2(-2f, -2f) : new Vector2(-1f, -1f);
        Color32 color = solidColor ? FarFaceColor(renderType, direction) : new Color32(255, 255, 255, 255);
        for (int i = 0; i < 4; i++)
        {
            meshData.tiledUvs.Add(mode);
            meshData.colors.Add(color);
        }

        return meshData;
    }

    public static bool GetFaceVertices(Direction direction, ChunkData chunk, int x, int y, int z, MeshData meshData, VoxelType voxelType, bool forceCube = false)
    {
        return GetFaceVertices(direction, new ChunkMeshContext(chunk), x, y, z, meshData, voxelType, forceCube);
    }

    public static bool GetFaceVertices(Direction direction, ChunkMeshContext context, int x, int y, int z, MeshData meshData, VoxelType voxelType, bool forceCube = false)
    {
        return GetFaceVerticesCached(
            direction, context, x, y, z, meshData, voxelType, forceCube,
            new AxisNeighbours(context, x, y, z));
    }

    private static bool GetFaceVerticesCached(Direction direction, ChunkMeshContext context, int x, int y, int z, MeshData meshData, VoxelType voxelType, bool forceCube, AxisNeighbours neighbours)
    {
        if (!VoxelRuntimeData.TryGet(voxelType, out VoxelRenderInfo info))
            return false;
        var generatesCollider = info.generatesCollider;
        bool isSmoothable = info.isSmoothable;
        float s = VoxelMetrics.Size;
        bool splitAlternative = false;

        if (isSmoothable && !forceCube)
        {
            Vector3 v0 = Vector3.zero, v1 = Vector3.zero, v2 = Vector3.zero, v3 = Vector3.zero;

            switch (direction)
            {
                case Direction.backwards:
                    v0 = GetStickyCorner(neighbours, x, y, z, -1, -1, -1);
                    v1 = GetStickyCorner(neighbours, x, y, z, -1, 1, -1);
                    v2 = GetStickyCorner(neighbours, x, y, z, 1, 1, -1);
                    v3 = GetStickyCorner(neighbours, x, y, z, 1, -1, -1);
                    break;
                case Direction.forward:
                    v0 = GetStickyCorner(neighbours, x, y, z, 1, -1, 1);
                    v1 = GetStickyCorner(neighbours, x, y, z, 1, 1, 1);
                    v2 = GetStickyCorner(neighbours, x, y, z, -1, 1, 1);
                    v3 = GetStickyCorner(neighbours, x, y, z, -1, -1, 1);
                    break;
                case Direction.left:
                    v0 = GetStickyCorner(neighbours, x, y, z, -1, -1, 1);
                    v1 = GetStickyCorner(neighbours, x, y, z, -1, 1, 1);
                    v2 = GetStickyCorner(neighbours, x, y, z, -1, 1, -1);
                    v3 = GetStickyCorner(neighbours, x, y, z, -1, -1, -1);
                    break;
                case Direction.right:
                    v0 = GetStickyCorner(neighbours, x, y, z, 1, -1, -1);
                    v1 = GetStickyCorner(neighbours, x, y, z, 1, 1, -1);
                    v2 = GetStickyCorner(neighbours, x, y, z, 1, 1, 1);
                    v3 = GetStickyCorner(neighbours, x, y, z, 1, -1, 1);
                    break;
                case Direction.down:
                    v0 = GetStickyCorner(neighbours, x, y, z, -1, -1, -1);
                    v1 = GetStickyCorner(neighbours, x, y, z, 1, -1, -1);
                    v2 = GetStickyCorner(neighbours, x, y, z, 1, -1, 1);
                    v3 = GetStickyCorner(neighbours, x, y, z, -1, -1, 1);
                    break;
                case Direction.up:
                    v0 = GetStickyCorner(neighbours, x, y, z, -1, 1, 1);
                    v1 = GetStickyCorner(neighbours, x, y, z, 1, 1, 1);
                    v2 = GetStickyCorner(neighbours, x, y, z, 1, 1, -1);
                    v3 = GetStickyCorner(neighbours, x, y, z, -1, 1, -1);
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

    private static void AddFaceUvs(MeshData meshData, Direction direction, VoxelType voxelType)
    {
        Vector2Int tilePos = TexturePosition(direction, voxelType);
        float minX = VoxelDataManager.tileSizeX * tilePos.x + VoxelDataManager.textureOffset;
        float maxX = VoxelDataManager.tileSizeX * tilePos.x + VoxelDataManager.tileSizeX - VoxelDataManager.textureOffset;
        float minY = VoxelDataManager.tileSizeY * tilePos.y + VoxelDataManager.textureOffset;
        float maxY = VoxelDataManager.tileSizeY * tilePos.y + VoxelDataManager.tileSizeY - VoxelDataManager.textureOffset;
        meshData.uvs.Add(new Vector2(maxX, minY));
        meshData.uvs.Add(new Vector2(maxX, maxY));
        meshData.uvs.Add(new Vector2(minX, maxY));
        meshData.uvs.Add(new Vector2(minX, minY));
    }

    private static Color32 FarFaceColor(VoxelType voxelType, Direction direction)
    {
        Color32 color = VoxelRuntimeData.FarColor(voxelType);
        float brightness = direction == Direction.up ? 1f : 0.72f;
        return new Color32(
            (byte)Mathf.RoundToInt(color.r * brightness),
            (byte)Mathf.RoundToInt(color.g * brightness),
            (byte)Mathf.RoundToInt(color.b * brightness),
            color.a);
    }

    public static Vector2Int TexturePosition(Direction direction, VoxelType voxelType)
    {
        return VoxelRuntimeData.TexturePosition(direction, voxelType);
    }
}
