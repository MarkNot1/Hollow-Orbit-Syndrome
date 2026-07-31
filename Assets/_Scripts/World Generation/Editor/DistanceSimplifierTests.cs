using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class DistanceSimplifierTests
{
    private VoxelDataSO voxelData;

    [SetUp]
    public void SetUp()
    {
        voxelData = ScriptableObject.CreateInstance<VoxelDataSO>();
        voxelData.textureSizeX = 1f / 16f;
        voxelData.textureSizeY = 1f / 16f;
        voxelData.textureDataList = new List<TextureData>
        {
            Info(VoxelType.Air, false, false, false, 0),
            Info(VoxelType.Rock, true, true, true, 1),
            Info(VoxelType.Infected_Growth, true, true, true, 2),
            Info(VoxelType.Ship_Metal, true, false, false, 3),
            Info(VoxelType.Metal, true, true, false, 4)
        };
        VoxelRuntimeData.Capture(voxelData);
        VoxelDataManager.tileSizeX = voxelData.textureSizeX;
        VoxelDataManager.tileSizeY = voxelData.textureSizeY;
        VoxelDataManager.voxelTextureDataDictionary.Clear();
        foreach (TextureData data in voxelData.textureDataList)
            VoxelDataManager.voxelTextureDataDictionary[data.voxelType] = data;
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(voxelData);
        VoxelDataManager.voxelTextureDataDictionary.Clear();
    }

    [Test]
    public void SelectLod_UsesExactChunkRingsWithoutHistory()
    {
        var settings = new DistanceSimplifierSettings
        {
            uniformSurfaceRendering = false,
            lod1StartChunks = 4f,
            lod2StartChunks = 6f,
            lod3StartChunks = 6f
        };

        Assert.That(SelectAtRing(0, ChunkLodLevel.Full, false, settings), Is.EqualTo(ChunkLodLevel.Full));
        Assert.That(SelectAtRing(1, ChunkLodLevel.Full, false, settings), Is.EqualTo(ChunkLodLevel.Full));
        Assert.That(SelectAtRing(2, ChunkLodLevel.Full, false, settings), Is.EqualTo(ChunkLodLevel.Full));
        Assert.That(SelectAtRing(3, ChunkLodLevel.Full, false, settings), Is.EqualTo(ChunkLodLevel.Full));
        Assert.That(SelectAtRing(4, ChunkLodLevel.Full, false, settings), Is.EqualTo(ChunkLodLevel.Merge4));
        Assert.That(SelectAtRing(5, ChunkLodLevel.Full, false, settings), Is.EqualTo(ChunkLodLevel.Merge4));
        Assert.That(SelectAtRing(6, ChunkLodLevel.Full, false, settings), Is.EqualTo(ChunkLodLevel.Merge16));
        Assert.That(SelectAtRing(7, ChunkLodLevel.Full, false, settings), Is.EqualTo(ChunkLodLevel.Merge16));
        Assert.That(SelectAtRing(4, ChunkLodLevel.Full, true, settings), Is.EqualTo(ChunkLodLevel.Merge4));
        Assert.That(SelectAtRing(5, ChunkLodLevel.Full, true, settings), Is.EqualTo(ChunkLodLevel.Merge4));
        Assert.That(SelectAtRing(6, ChunkLodLevel.Merge4, true, settings), Is.EqualTo(ChunkLodLevel.Merge16));
        Assert.That(SelectAtRing(7, ChunkLodLevel.Merge4, true, settings), Is.EqualTo(ChunkLodLevel.Merge16));
    }

    [Test]
    public void SelectLod_ChangesOnlyWhenPlayerEntersAnotherChunk()
    {
        var settings = new DistanceSimplifierSettings();
        Vector3Int target = new Vector3Int(4 * 16, 0, 0);

        ChunkLodLevel atNearEdge = DistanceSimplifier.SelectLod(
            new Vector3Int(0, 0, 0), target, 16, ChunkLodLevel.Full, false, settings);
        ChunkLodLevel atFarEdge = DistanceSimplifier.SelectLod(
            new Vector3Int(15, 0, 15), target, 16, ChunkLodLevel.Full, true, settings);
        ChunkLodLevel afterCrossing = DistanceSimplifier.SelectLod(
            new Vector3Int(16, 0, 0), target, 16, ChunkLodLevel.Merge4, true, settings);

        Assert.That(atNearEdge, Is.EqualTo(ChunkLodLevel.Merge4));
        Assert.That(atFarEdge, Is.EqualTo(atNearEdge));
        Assert.That(afterCrossing, Is.EqualTo(ChunkLodLevel.Full));
    }

    [Test]
    public void UniformSurfaceRendering_RemovesDistanceTransition()
    {
        var settings = new DistanceSimplifierSettings
        {
            enabled = true,
            uniformSurfaceRendering = true
        };

        Assert.That(SelectAtRing(0, ChunkLodLevel.Full, false, settings), Is.EqualTo(ChunkLodLevel.Merge16));
        Assert.That(SelectAtRing(8, ChunkLodLevel.Full, false, settings), Is.EqualTo(ChunkLodLevel.Merge16));
    }

    [Test]
    public void AllLods_ShareIdenticalGeometryAndBounds()
    {
        ChunkData data = SolidChunk(16, 16, Vector3Int.zero);
        MeshData full = DistanceSimplifier.BuildMesh(
            new ChunkMeshContext(data), ChunkLodLevel.Full, false);
        MeshData merge2 = DistanceSimplifier.BuildMesh(data, ChunkLodLevel.Merge2);
        MeshData merge4 = DistanceSimplifier.BuildMesh(data, ChunkLodLevel.Merge4);
        MeshData merge16 = DistanceSimplifier.BuildMesh(data, ChunkLodLevel.Merge16);

        CollectionAssert.AreEqual(full.vertices, merge2.vertices);
        CollectionAssert.AreEqual(full.vertices, merge4.vertices);
        CollectionAssert.AreEqual(full.vertices, merge16.vertices);
        CollectionAssert.AreEqual(full.triangles, merge16.triangles);
        CollectionAssert.AreEqual(full.normals, merge16.normals);
        AssertBounds(full, 16, 16);
        AssertBounds(merge2, 16, 16);
        AssertBounds(merge4, 16, 16);
        AssertBounds(merge16, 16, 16);
    }

    [Test]
    public void FarSurface_PreservesOriginalColumnHeightAndChunkExtents()
    {
        ChunkData data = SolidChunk(8, 8, Vector3Int.zero);
        data.SetVoxel(3, 7, 3, VoxelType.Air);
        MeshData mesh = DistanceSimplifier.BuildMesh(data, ChunkLodLevel.Merge16);

        float expectedLowerTop = 6.5f * VoxelMetrics.Size;
        Assert.That(mesh.vertices.Exists(vertex => Mathf.Abs(vertex.y - expectedLowerTop) < 0.0001f), Is.True);
        AssertBounds(mesh, 8, 8);
    }

    [Test]
    public void SurfaceMesh_HasNoHiddenBottomOrLowerChunkDependency()
    {
        ChunkData data = SolidChunk(8, 8, Vector3Int.zero);
        MeshData mesh = DistanceSimplifier.BuildMesh(
            new ChunkMeshContext(data), ChunkLodLevel.Full, true);

        Assert.That(mesh.normals.Exists(normal => normal == Vector3.down), Is.False);
    }

    [Test]
    public void FarSurface_ProvidesWorkerNormalsAndSolidMaterialColors()
    {
        MeshData mesh = DistanceSimplifier.BuildMesh(
            SolidChunk(16, 8, Vector3Int.zero),
            ChunkLodLevel.Merge16);

        Assert.That(mesh.normals.Count, Is.EqualTo(mesh.vertices.Count));
        Assert.That(mesh.tiledUvs.Count, Is.EqualTo(mesh.vertices.Count));
        Assert.That(mesh.colors.Count, Is.EqualTo(mesh.vertices.Count));
        Assert.That(mesh.colors.Exists(color => color.a == 255), Is.True);
        Assert.That(mesh.normals.TrueForAll(normal => Mathf.Abs(normal.sqrMagnitude - 1f) < 0.001f), Is.True);
    }

    [Test]
    public void TexturedTier_UsesAtlasWithoutColorBlendOrGeometryChange()
    {
        ChunkData data = SolidChunk(8, 8, Vector3Int.zero);
        MeshData transition = DistanceSimplifier.BuildMesh(data, ChunkLodLevel.Merge4);
        MeshData lightTransition = DistanceSimplifier.BuildMesh(data, ChunkLodLevel.Merge2);
        MeshData solid = DistanceSimplifier.BuildMesh(data, ChunkLodLevel.Merge16);

        Assert.That(transition.triangles.Count, Is.EqualTo(solid.triangles.Count));
        Assert.That(transition.vertices, Is.EquivalentTo(solid.vertices));
        Assert.That(transition.tiledUvs.TrueForAll(uv => uv.x > -1.5f && uv.y > -1.5f), Is.True);
        Assert.That(lightTransition.colors.TrueForAll(color => color.Equals(new Color32(255, 255, 255, 255))), Is.True);
        Assert.That(transition.colors.TrueForAll(color => color.Equals(new Color32(255, 255, 255, 255))), Is.True);
        Assert.That(solid.tiledUvs.TrueForAll(uv => uv.x < -1.5f && uv.y < -1.5f), Is.True);
    }

    [Test]
    public void LodFaces_NeverGrowBeyondOneVoxel()
    {
        MeshData mesh = DistanceSimplifier.BuildMesh(
            SolidChunk(8, 8, Vector3Int.zero),
            ChunkLodLevel.Merge16);

        float maximumExtent = VoxelMetrics.Size + 0.0001f;
        for (int i = 0; i < mesh.vertices.Count; i += 4)
        {
            Bounds face = new Bounds(mesh.vertices[i], Vector3.zero);
            face.Encapsulate(mesh.vertices[i + 1]);
            face.Encapsulate(mesh.vertices[i + 2]);
            face.Encapsulate(mesh.vertices[i + 3]);
            Assert.That(face.size.x, Is.LessThanOrEqualTo(maximumExtent));
            Assert.That(face.size.y, Is.LessThanOrEqualTo(maximumExtent));
            Assert.That(face.size.z, Is.LessThanOrEqualTo(maximumExtent));
        }
    }

    [Test]
    public void FarSurface_UsesRequestedMetalAndGrowthPalette()
    {
        Assert.That(VoxelRuntimeData.FarColor(VoxelType.Metal), Is.EqualTo(new Color32(0x11, 0x35, 0x37, 255)));
        Assert.That(VoxelRuntimeData.FarColor(VoxelType.Ship_Metal), Is.EqualTo(new Color32(0x11, 0x35, 0x37, 255)));
        Assert.That(VoxelRuntimeData.FarColor(VoxelType.Infected_Growth), Is.EqualTo(new Color32(0x41, 0x25, 0x6E, 255)));
    }

    [Test]
    public void FarSurface_OnlyProvidesCollisionWhenRequested()
    {
        ChunkData data = SolidChunk(4, 4, Vector3Int.zero);
        MeshData renderOnly = DistanceSimplifier.BuildMesh(data, ChunkLodLevel.Merge16);
        MeshData collision = DistanceSimplifier.BuildMesh(
            new ChunkMeshContext(data),
            ChunkLodLevel.Merge16,
            true);

        Assert.That(renderOnly.colliderVertices, Is.Empty);
        Assert.That(renderOnly.colliderTriangles, Is.Empty);
        Assert.That(collision.colliderVertices, Is.Not.Empty);
        Assert.That(collision.colliderTriangles, Is.Not.Empty);
    }

    [Test]
    public void AdjacentChunks_CullTheirSharedBoundary()
    {
        ChunkData left = SolidChunk(4, 4, Vector3Int.zero);
        ChunkData right = SolidChunk(4, 4, new Vector3Int(4, 0, 0));
        var neighborhood = new Dictionary<Vector3Int, ChunkData>
        {
            [left.worldPosition] = left,
            [right.worldPosition] = right
        };

        MeshData isolated = DistanceSimplifier.BuildMesh(left, ChunkLodLevel.Merge16);
        MeshData joined = DistanceSimplifier.BuildMesh(
            new ChunkMeshContext(left, neighborhood),
            ChunkLodLevel.Merge16,
            false);

        Assert.That(joined.triangles.Count, Is.LessThan(isolated.triangles.Count));
        float boundary = 3.5f * VoxelMetrics.Size;
        int boundaryQuads = 0;
        for (int i = 0; i < joined.vertices.Count; i += 4)
        {
            bool allOnBoundary = true;
            for (int vertex = 0; vertex < 4; vertex++)
                allOnBoundary &= Mathf.Abs(joined.vertices[i + vertex].x - boundary) < 0.0001f;
            if (allOnBoundary)
                boundaryQuads++;
        }
        Assert.That(boundaryQuads, Is.EqualTo(0));
    }

    [Test]
    public void MixedDetailBoundary_AddsNoSkirtOrGeometryChange()
    {
        ChunkData left = SolidChunk(4, 4, Vector3Int.zero);
        ChunkData right = SolidChunk(4, 4, new Vector3Int(4, 0, 0));
        var neighborhood = new Dictionary<Vector3Int, ChunkData>
        {
            [left.worldPosition] = left,
            [right.worldPosition] = right
        };
        var context = new ChunkMeshContext(left, neighborhood);
        MeshData full = DistanceSimplifier.BuildMesh(
            context,
            ChunkLodLevel.Full,
            false);
        MeshData simplified = DistanceSimplifier.BuildMesh(
            context,
            ChunkLodLevel.Merge4,
            false);

        CollectionAssert.AreEqual(full.vertices, simplified.vertices);
        CollectionAssert.AreEqual(full.triangles, simplified.triangles);
        CollectionAssert.AreEqual(full.normals, simplified.normals);
    }

    [Test]
    public void Edit_IncrementsVersionAndInvalidatesNeighbourhoodHash()
    {
        ChunkData data = SolidChunk(4, 4, Vector3Int.zero);
        var neighborhood = new Dictionary<Vector3Int, ChunkData> { [Vector3Int.zero] = data };
        int beforeVersion = data.ContentVersion;
        int beforeHash = new ChunkMeshContext(data, neighborhood).NeighbourVersionHash;

        data.SetVoxel(0, 0, 0, VoxelType.Air);

        Assert.That(data.ContentVersion, Is.GreaterThan(beforeVersion));
        Assert.That(new ChunkMeshContext(data, neighborhood).NeighbourVersionHash, Is.Not.EqualTo(beforeHash));
    }

    [Test]
    public void Edit_IncrementallyUpdatesFarSurfaceColumn()
    {
        ChunkData data = SolidChunk(4, 4, Vector3Int.zero);
        Assert.That(data.GetFarSurfaceColumn(1, 1).topY, Is.EqualTo(3));

        data.SetVoxel(1, 3, 1, VoxelType.Air);

        Assert.That(data.GetFarSurfaceColumn(1, 1).topY, Is.EqualTo(2));
        Assert.That(data.GetFarSurfaceColumn(1, 1).material, Is.EqualTo(VoxelType.Rock));
    }

    [Test]
    public void ColumnTerrainFullMesh_MatchesGeneralSmoothMesh()
    {
        const int size = 4;
        const int height = 6;
        var optimizedNeighborhood = new Dictionary<Vector3Int, ChunkData>();
        for (int z = -1; z <= 1; z++)
        {
            for (int x = -1; x <= 1; x++)
            {
                Vector3Int position = new Vector3Int(x * size, 0, z * size);
                optimizedNeighborhood[position] = ColumnChunk(size, height, position, 3);
            }
        }

        ChunkData optimizedCenter = optimizedNeighborhood[Vector3Int.zero];
        MeshData optimized = DistanceSimplifier.BuildMesh(
            new ChunkMeshContext(optimizedCenter, optimizedNeighborhood),
            ChunkLodLevel.Full,
            true);

        ChunkData generalCenter = SolidToHeightChunk(size, height, Vector3Int.zero, 3);
        var generalNeighborhood = new Dictionary<Vector3Int, ChunkData>(optimizedNeighborhood)
        {
            [Vector3Int.zero] = generalCenter
        };
        MeshData general = DistanceSimplifier.BuildMesh(
            new ChunkMeshContext(generalCenter, generalNeighborhood),
            ChunkLodLevel.Full,
            true);

        Assert.That(optimized.vertices, Is.EquivalentTo(general.vertices));
        Assert.That(optimized.triangles.Count, Is.EqualTo(general.triangles.Count));
        Assert.That(optimized.colliderVertices, Is.EquivalentTo(general.colliderVertices));
        Assert.That(optimized.colliderTriangles.Count, Is.EqualTo(general.colliderTriangles.Count));
    }

    [Test]
    public void SnapshotGenerator_MatchesExistingBaseTerrainFormula()
    {
        NoiseSettings settings = ScriptableObject.CreateInstance<NoiseSettings>();
        settings.noiseZoom = 0.03f;
        settings.octaves = 3;
        settings.persistence = 0.55f;
        settings.offset = new Vector2Int(7, -4);
        settings.redistributionModifier = 1.1f;
        settings.exponent = 1.2f;
        Vector2Int seed = new Vector2Int(91, -33);
        var snapshot = new TerrainGenerationSnapshot(
            new NoiseSettingsSnapshot(settings),
            new DomainWarpSnapshot(null),
            false,
            VoxelType.Infected_Growth,
            VoxelType.Rock,
            null);
        var data = new ChunkData(4, 12, null, Vector3Int.zero);

        snapshot.Generate(data, seed);

        for (int z = 0; z < data.chunkSize; z++)
        {
            for (int x = 0; x < data.chunkSize; x++)
            {
                float noise = MyNoise.OctavePerlin(x, z, settings, seed);
                noise = MyNoise.Redistribution(noise, settings);
                int surface = (int)MyNoise.RemapValue01ToInt(noise, 0, data.chunkHeight);
                for (int y = 0; y < data.chunkHeight; y++)
                {
                    VoxelType expected = y > surface ? VoxelType.Air :
                        y == surface ? VoxelType.Infected_Growth : VoxelType.Rock;
                    int index = x + data.chunkSize * y + data.chunkSize * data.chunkHeight * z;
                    Assert.That(data.voxels[index], Is.EqualTo(expected));
                }
            }
        }
        Object.DestroyImmediate(settings);
    }

    [Test]
    public void RendererReset_ClearsLodCollisionAndContentState()
    {
        GameObject go = new GameObject("Chunk Renderer Test");
        go.AddComponent<MeshFilter>();
        go.AddComponent<MeshRenderer>();
        go.AddComponent<MeshCollider>();
        ChunkRenderer renderer = go.AddComponent<ChunkRenderer>();
        ChunkData data = SolidChunk(2, 2, Vector3Int.zero);
        renderer.InitializeChunk(data);
        MeshData mesh = DistanceSimplifier.BuildMesh(data, ChunkLodLevel.Merge2);
        renderer.ApplyMesh(mesh, ChunkLodLevel.Merge2, data.ContentVersion, false);

        renderer.ResetForPool();

        Assert.That(renderer.ChunkData, Is.Null);
        Assert.That(renderer.ActiveLod, Is.EqualTo(ChunkLodLevel.Full));
        Assert.That(renderer.AppliedContentVersion, Is.EqualTo(-1));
        Assert.That(renderer.ActiveCollisionContentVersion, Is.EqualTo(-1));
        Assert.That(renderer.CollisionResolvedContentVersion, Is.EqualTo(-1));
        Assert.That(renderer.IsCollisionActive, Is.False);
        Object.DestroyImmediate(go);
    }

    [Test]
    public void Renderer_AllocatesCollisionMeshesOnlyWhenCollisionIsRequested()
    {
        GameObject go = new GameObject("Lazy Collision Renderer Test");
        go.AddComponent<MeshFilter>();
        go.AddComponent<MeshRenderer>();
        go.AddComponent<MeshCollider>();
        ChunkRenderer renderer = go.AddComponent<ChunkRenderer>();
        ChunkData data = SolidChunk(2, 2, Vector3Int.zero);
        renderer.InitializeChunk(data);

        MeshData farRender = DistanceSimplifier.BuildMesh(data, ChunkLodLevel.Merge16);
        ChunkRenderer.CollisionPreparation renderOnly = renderer.ApplyMesh(
            farRender,
            ChunkLodLevel.Merge16,
            data.ContentVersion,
            false);
        Assert.That(renderOnly.IsValid, Is.False);
        Assert.That(renderer.HasAllocatedCollisionMeshes, Is.False);

        MeshData farCollision = DistanceSimplifier.BuildMesh(
            new ChunkMeshContext(data), ChunkLodLevel.Merge16, true);
        ChunkRenderer.CollisionPreparation preparation = renderer.ApplyMesh(
            farCollision,
            ChunkLodLevel.Merge16,
            data.ContentVersion,
            true);
        Assert.That(preparation.IsValid, Is.True);
        Assert.That(renderer.HasAllocatedCollisionMeshes, Is.True);
        Object.DestroyImmediate(go);
    }

    [Test]
    public void Renderer_DisablingCollisionInvalidatesOutstandingBake()
    {
        GameObject go = new GameObject("Collision Bake Invalidation Test");
        go.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = go.AddComponent<MeshRenderer>();
        go.AddComponent<MeshCollider>();
        ChunkRenderer renderer = go.AddComponent<ChunkRenderer>();
        ChunkData data = SolidChunk(2, 2, Vector3Int.zero);
        renderer.InitializeChunk(data);
        MeshData collision = DistanceSimplifier.BuildMesh(
            new ChunkMeshContext(data), ChunkLodLevel.Full, true);

        ChunkRenderer.CollisionPreparation preparation = renderer.ApplyMesh(
            collision,
            ChunkLodLevel.Full,
            data.ContentVersion,
            true);

        Assert.That(preparation.IsValid, Is.True);
        Assert.That(renderer.IsCollisionPreparationCurrent(preparation), Is.True);
        Assert.That(meshRenderer.receiveShadows, Is.False,
            "Voxel terrain must not sample the camera-bounded shadow cutoff.");

        renderer.SetCollisionActive(false);

        Assert.That(renderer.IsCollisionPreparationCurrent(preparation), Is.False);
        Assert.That(renderer.TryActivateBakedCollision(
            preparation.leaseId,
            preparation.buildId,
            preparation.contentVersion), Is.False);
        Assert.That(renderer.IsCollisionActive, Is.False);
        Object.DestroyImmediate(go);
    }

    [Test]
    public void Renderer_ReactivatesCookedCollisionAfterBacktracking()
    {
        GameObject go = new GameObject("Reusable Collision Test");
        go.AddComponent<MeshFilter>();
        go.AddComponent<MeshRenderer>();
        go.AddComponent<MeshCollider>();
        ChunkRenderer renderer = go.AddComponent<ChunkRenderer>();
        ChunkData data = SolidChunk(2, 2, Vector3Int.zero);
        renderer.InitializeChunk(data);
        MeshData collision = DistanceSimplifier.BuildMesh(
            new ChunkMeshContext(data), ChunkLodLevel.Full, true);
        ChunkRenderer.CollisionPreparation preparation = renderer.ApplyMesh(
            collision,
            ChunkLodLevel.Full,
            data.ContentVersion,
            true);

        Assert.That(renderer.TryActivateBakedCollision(
            preparation.leaseId,
            preparation.buildId,
            preparation.contentVersion), Is.True);
        renderer.SetCollisionActive(false);

        Assert.That(renderer.IsCollisionActive, Is.False);
        Assert.That(renderer.HasReusableCollision, Is.True);
        Assert.That(go.GetComponent<MeshCollider>().sharedMesh, Is.Not.Null);
        Assert.That(renderer.ActiveCollisionContentVersion, Is.EqualTo(data.ContentVersion));
        Assert.That(renderer.TryReactivateCollision(data.ContentVersion), Is.True);
        Assert.That(renderer.IsCollisionActive, Is.True);

        MeshData renderOnly = DistanceSimplifier.BuildMesh(data, ChunkLodLevel.Merge4);
        renderer.ApplyMesh(renderOnly, ChunkLodLevel.Merge4, data.ContentVersion, false);
        renderer.SetCollisionActive(false);
        Assert.That(renderer.TryReactivateCollision(data.ContentVersion), Is.True,
            "An LOD-only rebuild discarded the cooked collision cache.");
        Object.DestroyImmediate(go);
    }

    [Test]
    public void Renderer_PoolLeaseUsesFreshPhysicsMeshIdentity()
    {
        GameObject go = new GameObject("Pooled Collision Mesh Identity Test");
        go.AddComponent<MeshFilter>();
        go.AddComponent<MeshRenderer>();
        go.AddComponent<MeshCollider>();
        ChunkRenderer renderer = go.AddComponent<ChunkRenderer>();
        ChunkData data = SolidChunk(2, 2, Vector3Int.zero);
        renderer.InitializeChunk(data);
        MeshData collision = DistanceSimplifier.BuildMesh(
            new ChunkMeshContext(data), ChunkLodLevel.Full, true);
        ChunkRenderer.CollisionPreparation first = renderer.ApplyMesh(
            collision, ChunkLodLevel.Full, data.ContentVersion, true);
        Assert.That(renderer.TryActivateBakedCollision(
            first.leaseId, first.buildId, first.contentVersion), Is.True);

        renderer.ResetForPool();
        Assert.That(renderer.HasAllocatedCollisionMeshes, Is.False);
        renderer.InitializeChunk(data);
        ChunkRenderer.CollisionPreparation second = renderer.ApplyMesh(
            collision, ChunkLodLevel.Full, data.ContentVersion, true);

        Assert.That(second.meshInstanceId, Is.Not.EqualTo(first.meshInstanceId));
        Assert.That(renderer.TryActivateBakedCollision(
            second.leaseId, second.buildId, second.contentVersion), Is.True);
        Object.DestroyImmediate(go);
    }

    [Test]
    public void World_DirectionChangeClearsAnAbandonedCollisionBlock()
    {
        GameObject worldObject = new GameObject("Direction Change World Test");
        World world = worldObject.AddComponent<World>();
        GameObject target = new GameObject("Streaming Target");
        target.transform.position = Vector3.zero;

        SetPrivateField(world, "streamingTarget", target.transform);
        SetPrivateField(world, "collisionMovementBlocked", true);
        SetPrivateField(world, "blockedCollisionChunk", new Vector3Int(world.chunkSize, 0, 0));
        SetPrivateField(world, "streamCenterChunk", Vector3Int.zero);

        InvokePrivate(world, "UpdateStreamingTargetPosition");

        Assert.That(world.IsCollisionSafetyBlocking, Is.False,
            "Returning to a different chunk left streaming pinned to the abandoned direction.");
        Object.DestroyImmediate(target);
        Object.DestroyImmediate(worldObject);
    }

    [Test]
    public void Renderer_MarksEmptyFullCollisionAsResolvedWithoutAllocatingMesh()
    {
        GameObject go = new GameObject("Empty Collision Renderer Test");
        go.AddComponent<MeshFilter>();
        go.AddComponent<MeshRenderer>();
        go.AddComponent<MeshCollider>();
        ChunkRenderer renderer = go.AddComponent<ChunkRenderer>();
        var data = new ChunkData(2, 2, null, Vector3Int.zero);
        data.MarkGenerated();
        renderer.InitializeChunk(data);

        MeshData empty = DistanceSimplifier.BuildMesh(
            new ChunkMeshContext(data),
            ChunkLodLevel.Full,
            true);
        ChunkRenderer.CollisionPreparation preparation = renderer.ApplyMesh(
            empty,
            ChunkLodLevel.Full,
            data.ContentVersion,
            true);

        Assert.That(preparation.IsValid, Is.False);
        Assert.That(renderer.IsCollisionActive, Is.False);
        Assert.That(renderer.CollisionResolvedContentVersion, Is.EqualTo(data.ContentVersion));
        Assert.That(renderer.HasAllocatedCollisionMeshes, Is.False);
        Object.DestroyImmediate(go);
    }

    [Test]
    public void VoxelType_UsesCompactByteStorageWithoutChangingValues()
    {
        Assert.That(System.Enum.GetUnderlyingType(typeof(VoxelType)), Is.EqualTo(typeof(byte)));
        Assert.That((int)VoxelType.Nothing, Is.EqualTo(0));
        Assert.That((int)VoxelType.Metal, Is.EqualTo(5));
        Assert.That(System.Runtime.InteropServices.Marshal.SizeOf<FarSurfaceColumn>(), Is.LessThanOrEqualTo(4));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void IncrementalQuadNormals_MatchLegacyWorkerNormalCalculation(bool alternativeSplit)
    {
        Vector3[] vertices =
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(0f, 1f, 0.15f),
            new Vector3(1f, 1f, 0f),
            new Vector3(1f, 0f, -0.1f)
        };
        var incremental = new MeshData(false);
        var reference = new MeshData(false);
        foreach (Vector3 vertex in vertices)
        {
            incremental.AddVertex(vertex, false);
            reference.AddVertex(vertex, false);
        }
        incremental.AddQuadTriangles(false, alternativeSplit);
        incremental.AddQuadNormals(alternativeSplit);
        reference.AddQuadTriangles(false, alternativeSplit);
        reference.CalculateNormals();

        Assert.That(incremental.normals.Count, Is.EqualTo(reference.normals.Count));
        for (int i = 0; i < incremental.normals.Count; i++)
            Assert.That(Vector3.Distance(incremental.normals[i], reference.normals[i]), Is.LessThan(0.0001f));
    }

    private static ChunkLodLevel SelectAtRing(
        int ring,
        ChunkLodLevel current,
        bool hasCurrent,
        DistanceSimplifierSettings settings)
    {
        Vector3Int chunk = new Vector3Int(ring * 16, 0, 0);
        return DistanceSimplifier.SelectLod(
            new Vector3Int(8, 0, 8), chunk, 16, current, hasCurrent, settings);
    }

    private static ChunkData SolidChunk(int size, int height, Vector3Int position)
    {
        var data = new ChunkData(size, height, null, position);
        for (int i = 0; i < data.voxels.Length; i++)
            data.voxels[i] = VoxelType.Rock;
        data.MarkGenerated();
        return data;
    }

    private static ChunkData ColumnChunk(int size, int height, Vector3Int position, int topY)
    {
        var data = new ChunkData(size, height, null, position);
        for (int z = 0; z < size; z++)
        {
            for (int x = 0; x < size; x++)
            {
                for (int y = 0; y <= topY; y++)
                    data.voxels[x + size * y + size * height * z] = VoxelType.Rock;
                for (int y = topY + 1; y < height; y++)
                    data.voxels[x + size * y + size * height * z] = VoxelType.Air;
                data.SetGeneratedSurfaceColumn(x, z, topY, VoxelType.Rock);
            }
        }
        data.MarkGeneratedFromSurfaceColumns();
        return data;
    }

    private static ChunkData SolidToHeightChunk(int size, int height, Vector3Int position, int topY)
    {
        var data = new ChunkData(size, height, null, position);
        for (int z = 0; z < size; z++)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < size; x++)
                    data.voxels[x + size * y + size * height * z] = y <= topY ? VoxelType.Rock : VoxelType.Air;
            }
        }
        data.MarkGenerated();
        return data;
    }

    private static TextureData Info(
        VoxelType type,
        bool solid,
        bool collider,
        bool smooth,
        int tile)
    {
        return new TextureData
        {
            voxelType = type,
            isSolid = solid,
            generatesCollider = collider,
            isSmoothable = smooth,
            up = new Vector2Int(tile, 0),
            down = new Vector2Int(tile, 0),
            side = new Vector2Int(tile, 0)
        };
    }

    private static void AssertBounds(MeshData mesh, int size, int height)
    {
        float scale = VoxelMetrics.Size;
        float min = -0.5f * scale;
        float maxHorizontal = (size - 0.5f) * scale;
        float maxVertical = (height - 0.5f) * scale;
        foreach (Vector3 vertex in mesh.vertices)
        {
            Assert.That(vertex.x, Is.InRange(min - 0.0001f, maxHorizontal + 0.0001f));
            Assert.That(vertex.y, Is.InRange(min - 0.0001f, maxVertical + 0.0001f));
            Assert.That(vertex.z, Is.InRange(min - 0.0001f, maxHorizontal + 0.0001f));
        }
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Missing field {fieldName}.");
        field.SetValue(target, value);
    }

    private static void InvokePrivate(object target, string methodName)
    {
        MethodInfo method = target.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, $"Missing method {methodName}.");
        method.Invoke(target, null);
    }
}
