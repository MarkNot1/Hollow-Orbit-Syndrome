using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class StructureTests
{
    private readonly List<UnityEngine.Object> objects = new();
    private StructureSystem system;
    private SpecialPrefabDatabaseSO prefabs;
    private static readonly Vector3 TestPosition = new(10000f, 10000f, 10000f);

    [SetUp]
    public void SetUp()
    {
        VoxelDataSO data = AssetDatabase.LoadAssetAtPath<VoxelDataSO>("Assets/_Scripts/World Generation/VoxelData.asset");
        VoxelRuntimeData.Capture(data);
        VoxelDataManager.tileSizeX = data.textureSizeX;
        VoxelDataManager.tileSizeY = data.textureSizeY;
        prefabs = AssetDatabase.LoadAssetAtPath<SpecialPrefabDatabaseSO>("Assets/_Scripts/World Generation/SpecialPrefabDatabase.asset");
        system = NewObject("Structures").AddComponent<StructureSystem>();
        SetReference(system, "voxelMaterial", AssetDatabase.LoadAssetAtPath<Material>("Assets/_Textures/MainMaterial.mat"));
        SetReference(system, "prefabDatabase", prefabs);
        SetReference(system, "framePreviewMaterial", AssetDatabase.LoadAssetAtPath<Material>("Assets/_Textures/FramePreview.mat"));
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var value in objects.AsEnumerable().Reverse())
            if (value != null)
            {
                if (value is GameObject root) root.GetComponent<World>()?.OnDisable();
                UnityEngine.Object.DestroyImmediate(value);
            }
        objects.Clear();
    }

    [Test]
    public void FrameHasFiftySixEditableDarkMetalVoxelsAndOpenFaces()
    {
        StructureData frame = JsonUtility.FromJson<StructureData>(Frame.text);
        Assert.That(frame.voxels.Count, Is.EqualTo(56));
        Assert.That(frame.voxels.All(v => v.type == VoxelType.Dark_Ship_Metal), Is.True);
        foreach (var voxel in frame.voxels)
        {
            var p = voxel.position;
            Assert.That(p.x, Is.InRange(0, 5));
            Assert.That(p.y, Is.InRange(0, 5));
            Assert.That(p.z, Is.InRange(0, 5));
            Assert.That((p.x is 0 or 5 ? 1 : 0) + (p.y is 0 or 5 ? 1 : 0) + (p.z is 0 or 5 ? 1 : 0), Is.GreaterThanOrEqualTo(2));
        }
        VoxelStructure structure = NewStructure();
        structure.Restore(frame);
        Assert.That(structure.SetVoxel(Vector3Int.zero, VoxelType.Air), Is.True);
        Assert.That(structure.SetVoxel(new Vector3Int(10, 2, -4), VoxelType.Metal), Is.True);
        Assert.That(structure.VoxelCount, Is.EqualTo(56));
    }

    [Test]
    public void MasterFrameMarksOnlyTheCentrePairOfAllFourBottomEdges()
    {
        StructureData frame = JsonUtility.FromJson<StructureData>(MasterFrame.text);
        Assert.That(frame.hasMasterFrame, Is.True);
        Assert.That(frame.voxels.Count, Is.EqualTo(56));
        Vector3Int[] expected = { new(2, 0, 0), new(3, 0, 0), new(2, 0, 5), new(3, 0, 5),
            new(0, 0, 2), new(0, 0, 3), new(5, 0, 2), new(5, 0, 3) };
        Assert.That(frame.voxels.Where(v => v.type == VoxelType.Striped_Ship_Metal).Select(v => v.position), Is.EquivalentTo(expected));
        Assert.That(frame.voxels.Count(v => v.type == VoxelType.Dark_Ship_Metal), Is.EqualTo(48));
        Assert.That(frame.voxels.Single(v => v.position == Vector3Int.zero).type, Is.EqualTo(VoxelType.Dark_Ship_Metal));
        Assert.That(VoxelRuntimeData.TexturePosition(Direction.forward, VoxelType.Striped_Ship_Metal), Is.EqualTo(new Vector2Int(4, 0)));
    }

    [Test]
    public void MasterBorderCannotBeRemovedOrReplacedAndProtectionSurvivesJson()
    {
        VoxelStructure structure = NewStructure();
        StructureData frame = JsonUtility.FromJson<StructureData>(MasterFrame.text);
        frame.position = TestPosition;
        structure.Restore(frame);
        structure.transform.rotation = Quaternion.Euler(0, 25, 0);
        foreach (StructureVoxelData voxel in frame.voxels)
        {
            Assert.That(structure.IsProtectedVoxel(voxel.position), Is.True);
            Assert.That(structure.SetVoxel(voxel.position, VoxelType.Air), Is.False);
            Assert.That(structure.SetVoxel(voxel.position, VoxelType.Metal), Is.False);
        }
        Vector3Int interior = new(2, 2, 2), extension = new(6, 0, 0);
        Assert.That(structure.SetVoxel(interior, VoxelType.Metal), Is.True);
        Assert.That(structure.SetVoxel(extension, VoxelType.Striped_Ship_Metal), Is.True);
        string id = structure.Id;
        system.RestoreJson(system.CaptureJson());
        structure = system.GetComponentInChildren<VoxelStructure>();
        Assert.That(structure.HasMasterFrame, Is.True);
        Assert.That(structure.Id, Is.EqualTo(id));
        Assert.That(structure.transform.position, Is.EqualTo(TestPosition));
        foreach (StructureVoxelData voxel in frame.voxels)
            Assert.That(structure.SetVoxel(voxel.position, VoxelType.Air), Is.False);
        Assert.That(structure.GetVoxel(interior), Is.EqualTo(VoxelType.Air));
        Assert.That(structure.SetVoxel(extension, VoxelType.Air), Is.True);
        Assert.That(structure.VoxelCount, Is.EqualTo(56));
    }

    [Test]
    public void OnlyMasterFramesStartStructuresAndRegularFramesExtendThem()
    {
        GameObject terrain = NewObject("Terrain");
        terrain.SetActive(false);
        World world = terrain.AddComponent<World>();
        world.chunkSize = world.chunkHeight = 16;
        typeof(World).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(world, null);
        Vector3Int cell = Vector3Int.RoundToInt(TestPosition / VoxelMetrics.Size);
        ChunkData data = new(16, 16, world, cell);
        world.worldData.chunkDataDictionary[cell] = data;
        Chunk.SetVoxel(data, Vector3Int.zero, VoxelType.Dark_Ship_Metal);
        GameObject surface = NewObject("Terrain surface");
        surface.transform.position = TestPosition;
        surface.AddComponent<ChunkRenderer>().InitializeChunk(data);
        BoxCollider collider = surface.AddComponent<BoxCollider>();
        collider.size = Vector3.one * VoxelMetrics.Size;
        Physics.SyncTransforms();
        Assert.That(collider.Raycast(new Ray(TestPosition + Vector3.up * 2, Vector3.down), out RaycastHit hit, 3), Is.True);
        Assert.That(system.TryGetBlueprintPlacement(hit, MasterFrame, out BlueprintPlacement preview), Is.True);
        Assert.That(preview.canPlace, Is.True);
        GameObject obstruction = NewObject("Player obstruction");
        obstruction.transform.position = TestPosition + Vector3.up * VoxelMetrics.Size;
        obstruction.AddComponent<BoxCollider>().size = Vector3.one * .2f;
        Assert.That(system.TryGetBlueprintPlacement(hit, MasterFrame, out preview), Is.True);
        Assert.That(preview.canPlace, Is.False);
        Assert.That(system.TryPlaceBlueprint(hit, MasterFrame), Is.False);
        obstruction.SetActive(false);
        Assert.That(system.TryPlaceBlueprint(hit, Frame), Is.False);
        Assert.That(system.GetComponentsInChildren<VoxelStructure>(), Is.Empty);
        Assert.That(system.TryPlaceBlueprint(hit, MasterFrame), Is.True);
        VoxelStructure structure = system.GetComponentInChildren<VoxelStructure>();
        Assert.That(structure.HasMasterFrame, Is.True);
        Assert.That(structure.transform.position, Is.EqualTo(TestPosition + Vector3.up * VoxelMetrics.Size));
        StructureData saved = JsonUtility.FromJson<StructureSaveData>(system.CaptureJson()).structures.Single();
        Assert.That(saved.voxels.Count, Is.EqualTo(56));
        Assert.That(saved.voxels.Any(v => v.position == Vector3Int.down), Is.False, "Touching terrain must not join the ship.");
        Vector3Int underside = new(5, 0, 5);
        Assert.That(structure.TryPlaceVoxel(Hit(structure, underside, Vector3Int.down), VoxelType.Metal), Is.True);
        saved = JsonUtility.FromJson<StructureSaveData>(system.CaptureJson()).structures.Single();
        Assert.That(saved.voxels.Single(v => v.position == underside + Vector3Int.down).type, Is.EqualTo(VoxelType.Metal));
        Assert.That(saved.voxels.Any(v => v.position == Vector3Int.down), Is.False);
        hit = Hit(structure, new Vector3Int(0, 5, 0), Vector3Int.up);
        Assert.That(system.TryPlaceBlueprint(hit, MasterFrame), Is.False);
        Assert.That(system.TryPlaceBlueprint(hit, Frame), Is.True);
        Assert.That(system.GetComponentsInChildren<VoxelStructure>().Length, Is.EqualTo(1));
        Assert.That(structure.SetVoxel(new Vector3Int(0, 6, 0), VoxelType.Air), Is.True);
        Assert.That(structure.SetVoxel(new Vector3Int(0, 5, 0), VoxelType.Air), Is.False);
    }

    [Test]
    public void UndersideHitAtAnEmptyNeighbourBoundaryResolvesToTheShipFace()
    {
        VoxelStructure ship = NewStructure();
        Vector3Int cell = Vector3Int.right;
        ship.SetVoxel(cell, VoxelType.Metal);
        ship.RebuildMeshes();
        Physics.SyncTransforms();
        Vector3 seam = ship.transform.TransformPoint(new Vector3(VoxelMetrics.Size * 0.5f, -1f, 0f));
        Assert.That(ship.GetComponentInChildren<MeshCollider>().Raycast(new Ray(seam, Vector3.up), out var hit, 2f), Is.True);
        Assert.That(ship.TryPlaceVoxel(hit, VoxelType.Window), Is.True);
        Assert.That(ship.GetVoxel(cell + Vector3Int.down), Is.EqualTo(VoxelType.Window));
    }

    [Test]
    public void IncompleteMasterFrameSaveIsRejectedAndLegacyFramesStayEditable()
    {
        VoxelStructure structure = NewStructure();
        StructureData legacy = JsonUtility.FromJson<StructureData>(Frame.text);
        structure.Restore(legacy);
        Assert.That(structure.HasMasterFrame, Is.False);
        Assert.That(structure.SetVoxel(Vector3Int.zero, VoxelType.Air), Is.True);
        string previous = system.CaptureJson();
        StructureData master = JsonUtility.FromJson<StructureData>(MasterFrame.text);
        master.id = "invalid-master";
        master.voxels.RemoveAt(0);
        StructureSaveData save = new();
        save.structures.Add(master);
        Assert.Throws<InvalidDataException>(() => system.RestoreJson(JsonUtility.ToJson(save)));
        Assert.That(system.CaptureJson(), Is.EqualTo(previous));
    }

    [Test]
    public void StructuresWithoutAMasterFrameAreNotSaved()
    {
        VoxelStructure loose = NewStructure();
        loose.SetVoxel(Vector3Int.zero, VoxelType.Metal);
        Assert.That(JsonUtility.FromJson<StructureSaveData>(system.CaptureJson()).structures, Is.Empty);
        Assert.That(loose.VoxelCount, Is.EqualTo(1));
    }

    [TestCase(-1, 0, 0)]
    [TestCase(1, 0, 0)]
    [TestCase(0, -1, 0)]
    [TestCase(0, 1, 0)]
    [TestCase(0, 0, -1)]
    [TestCase(0, 0, 1)]
    public void SaveFollowsFaceConnectionsAcrossChunksAndRechecksBrokenBridges(int x, int y, int z)
    {
        VoxelStructure ship = NewShip();
        ship.transform.rotation = Quaternion.Euler(15, 70, 25);
        Vector3Int direction = new(x, y, z);
        Vector3Int start = x + y + z > 0 ? new Vector3Int(5, 5, 5) : Vector3Int.zero;
        for (int i = 1; i <= 20; i++) ship.SetVoxel(start + direction * i, VoxelType.Window);
        // Same-chunk floating voxel, edge-only contact, and corner-only contact.
        foreach (Vector3Int p in new[] { new Vector3Int(2, 2, 2), new Vector3Int(-1, 5, 6), new Vector3Int(-1, -1, -1) })
            ship.SetVoxel(p, VoxelType.Metal);
        Assert.That(ship.Capture().voxels.Count, Is.EqualTo(76));
        ship.SetVoxel(start + direction * 2, VoxelType.Air);
        Assert.That(ship.Capture().voxels.Count, Is.EqualTo(57));
        Assert.That(ship.VoxelCount, Is.EqualTo(78), "Saving must not delete detached voxels in the scene.");
        ship.SetVoxel(start + direction * 2, VoxelType.Window);
        string json = system.CaptureJson();
        system.RestoreJson(json);
        ship = system.GetComponentInChildren<VoxelStructure>();
        Assert.That(ship.VoxelCount, Is.EqualTo(76));
        Assert.That(ship.GetVoxel(start + direction * 20), Is.EqualTo(VoxelType.Window));
        Assert.That(system.CaptureJson(), Is.EqualTo(json));
    }

    [Test]
    public void DetachedAttachmentsAreSkippedAndAnAlternateConnectionKeepsThem()
    {
        VoxelStructure ship = NewShip();
        for (int x = -1; x >= -3; x--) ship.SetVoxel(new Vector3Int(x, 0, 0), VoxelType.Metal);
        ship.RebuildMeshes();
        Assert.That(ship.TryPlacePrefab(Hit(ship, Vector3Int.zero, Vector3Int.back), "Circuit"), Is.True);
        Assert.That(ship.TryPlacePrefab(Hit(ship, Vector3Int.left * 3, Vector3Int.back), "Circuit"), Is.True);
        // Attachment membership is independent of the circuit-state round-trip tested below.
        foreach (StructureAttachment attachment in ship.Attachments)
            UnityEngine.Object.DestroyImmediate(attachment.GetComponent<CircuitStructureState>());
        Assert.That(ship.Capture().prefabs.Count, Is.EqualTo(2));
        ship.SetVoxel(Vector3Int.left * 2, VoxelType.Air);
        StructureData detached = ship.Capture();
        Assert.That(detached.prefabs.Single().surfaceVoxel, Is.EqualTo(Vector3Int.zero));
        Assert.That(ship.Attachments.Count, Is.EqualTo(2));
        StructureSystem.Validate(detached, prefabs);
        for (int x = -1; x >= -3; x--) ship.SetVoxel(new Vector3Int(x, 1, 0), VoxelType.Metal);
        Assert.That(ship.Capture().prefabs.Count, Is.EqualTo(2));
        for (int x = -1; x >= -3; x--) ship.SetVoxel(new Vector3Int(x, 1, 0), VoxelType.Air);
        system.RestoreJson(system.CaptureJson());
        ship = system.GetComponentInChildren<VoxelStructure>();
        Assert.That(ship.Attachments.Count, Is.EqualTo(1));
        Assert.That(ship.GetVoxel(Vector3Int.left * 3), Is.EqualTo(VoxelType.Air));
    }

    [Test]
    public void IndependentRotatedGridsRoundTripWithStableIdsAndEditsOutsideTheFrame()
    {
        VoxelStructure first = NewShip();
        first.transform.rotation = Quaternion.Euler(15, 70, 25);
        for (int x = -1; x >= -18; x--) first.SetVoxel(new Vector3Int(x, 0, 0), VoxelType.Dark_Ship_Metal);
        VoxelStructure second = NewShip();
        second.transform.position += Vector3.right * 20;
        second.SetVoxel(Vector3Int.left, VoxelType.Metal);
        string id = first.Id;
        string json = system.CaptureJson();
        system.RestoreJson(json);
        Assert.That(system.CaptureJson(), Is.EqualTo(json));
        VoxelStructure restored = system.GetComponentsInChildren<VoxelStructure>().Single(s => s.Id == id);
        Assert.That(restored.GetVoxel(new Vector3Int(-18, 0, 0)), Is.EqualTo(VoxelType.Dark_Ship_Metal));
        Assert.That(restored.transform.position, Is.EqualTo(TestPosition));
        Assert.That(Quaternion.Angle(restored.transform.rotation, Quaternion.Euler(15, 70, 25)), Is.LessThan(0.01f));
    }

    [TestCase(-1)]
    [TestCase(15)]
    public void ChunkBoundaryFacesAreCulledAndRebuiltAfterRemoval(int x)
    {
        VoxelStructure structure = NewStructure();
        structure.SetVoxel(new Vector3Int(x, 0, 0), VoxelType.Dark_Ship_Metal);
        structure.SetVoxel(new Vector3Int(x + 1, 0, 0), VoxelType.Metal);
        structure.RebuildMeshes();
        Assert.That(VertexCount(structure), Is.EqualTo(40));
        structure.SetVoxel(new Vector3Int(x, 0, 0), VoxelType.Air);
        structure.RebuildMeshes();
        Assert.That(VertexCount(structure), Is.EqualTo(24));
        Assert.That(structure.GetComponentsInChildren<MeshCollider>().Length, Is.EqualTo(1));
    }

    [Test]
    public void PlacementUsesRotatedLocalFacesAndRejectsOccupiedPlayerAndStaleHits()
    {
        VoxelStructure structure = NewStructure();
        structure.transform.rotation = Quaternion.Euler(0, 90, 0);
        structure.SetVoxel(Vector3Int.zero, VoxelType.Dark_Ship_Metal);
        structure.RebuildMeshes();
        RaycastHit hit = Hit(structure, Vector3Int.zero, Vector3Int.right);
        GameObject player = NewObject("Player obstruction");
        player.transform.position = structure.transform.TransformPoint(Vector3.right * VoxelMetrics.Size);
        player.AddComponent<BoxCollider>().size = Vector3.one * 0.4f;
        Physics.SyncTransforms();
        Assert.That(structure.TryPlaceVoxel(hit, VoxelType.Metal), Is.False);
        player.SetActive(false);
        Physics.SyncTransforms();
        Assert.That(structure.TryPlaceVoxel(hit, VoxelType.Metal), Is.True);
        Assert.That(structure.GetVoxel(Vector3Int.right), Is.EqualTo(VoxelType.Metal));
        Assert.That(structure.TryPlaceVoxel(hit, VoxelType.Metal), Is.False);
        structure.SetVoxel(Vector3Int.zero, VoxelType.Air);
        Assert.That(structure.TryPlaceVoxel(hit, VoxelType.Metal), Is.False);
        Assert.That(structure.SetVoxel(Vector3Int.up, (VoxelType)255), Is.False);
    }

    [Test]
    public void FrameStampExtendsTheSameStructureAndFailureDoesNotPartiallyPlaceIt()
    {
        VoxelStructure structure = NewStructure();
        structure.SetVoxel(Vector3Int.zero, VoxelType.Dark_Ship_Metal);
        structure.RebuildMeshes();
        RaycastHit hit = Hit(structure, Vector3Int.zero, Vector3Int.up);
        structure.SetVoxel(new Vector3Int(5, 6, 5), VoxelType.Metal);
        Assert.That(system.TryPlaceBlueprint(hit, Frame), Is.False);
        Assert.That(structure.VoxelCount, Is.EqualTo(2));
        structure.SetVoxel(new Vector3Int(5, 6, 5), VoxelType.Air);
        Assert.That(system.TryPlaceBlueprint(hit, Frame), Is.True);
        Assert.That(structure.VoxelCount, Is.EqualTo(57));
        Assert.That(system.GetComponentsInChildren<VoxelStructure>().Length, Is.EqualTo(1));
        Assert.That(structure.GetVoxel(Vector3Int.up), Is.EqualTo(VoxelType.Dark_Ship_Metal));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    public void FramePreviewMatchesPlacementOnEveryRotatedFaceAndRechecksObstructions(int face)
    {
        Vector3Int[] normals = { Vector3Int.left, Vector3Int.right, Vector3Int.down,
            Vector3Int.up, Vector3Int.back, Vector3Int.forward };
        VoxelStructure structure = NewStructure();
        structure.transform.rotation = Quaternion.Euler(10, 35, 20);
        structure.SetVoxel(Vector3Int.zero, VoxelType.Dark_Ship_Metal);
        structure.RebuildMeshes();
        RaycastHit hit = Hit(structure, Vector3Int.zero, normals[face]);
        string before = system.CaptureJson();
        Assert.That(system.TryGetBlueprintPlacement(hit, Frame, out BlueprintPlacement placement), Is.True);
        Assert.That(placement.canPlace, Is.True);
        Assert.That(placement.structure, Is.SameAs(structure));
        system.ShowBlueprintPreview(hit, Frame);
        GameObject preview = GameObject.Find("Frame placement preview");
        Assert.That(preview, Is.Not.Null);
        Assert.That(preview.GetComponentsInChildren<Collider>(), Is.Empty);
        Assert.That(preview.transform.position, Is.EqualTo(placement.position));
        Assert.That(Quaternion.Angle(preview.transform.rotation, structure.transform.rotation), Is.LessThan(.01f));
        Assert.That(system.CaptureJson(), Is.EqualTo(before));
        Assert.That(preview.GetComponent<MeshRenderer>().sharedMaterial.color.g, Is.EqualTo(1));
        Mesh mesh = preview.GetComponent<MeshFilter>().sharedMesh;
        system.ShowBlueprintPreview(hit, Frame);
        Assert.That(preview.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(mesh));

        GameObject obstruction = NewObject("Moving obstruction");
        obstruction.transform.position = placement.position;
        obstruction.AddComponent<BoxCollider>().size = Vector3.one * .2f;
        system.ShowBlueprintPreview(hit, Frame);
        Assert.That(preview.GetComponent<MeshRenderer>().sharedMaterial.color.r, Is.EqualTo(1));
        Assert.That(system.TryPlaceBlueprint(hit, Frame), Is.False);
        Assert.That(system.CaptureJson(), Is.EqualTo(before));
        obstruction.SetActive(false);
        Assert.That(system.TryPlaceBlueprint(hit, Frame), Is.True);
        foreach (var voxel in FrameBlueprint.Create(false).voxels)
            Assert.That(structure.GetVoxel(voxel.position + placement.offset), Is.EqualTo(voxel.type));
        system.HideBlueprintPreview();
        Assert.That(preview.activeSelf, Is.False);
    }

    [TestCase(-1)]
    [TestCase(15)]
    public void WindowsRetainSolidNeighboursAndCollisionAcrossChunkBoundaries(int x)
    {
        Assert.That(VoxelRuntimeData.IsSolid(VoxelType.Window), Is.True);
        VoxelStructure structure = NewStructure();
        Vector3Int cell = new(x, 0, 0);
        structure.SetVoxel(cell, VoxelType.Window);
        structure.SetVoxel(cell + Vector3Int.right, VoxelType.Metal);
        structure.RebuildMeshes();
        Assert.That(VertexCount(structure), Is.EqualTo(44));
        Assert.That(structure.TryGetHitCell(Hit(structure, cell, Vector3Int.forward), out Vector3Int surface, out _), Is.True);
        Assert.That(surface, Is.EqualTo(cell));
        structure.SetVoxel(cell + Vector3Int.right, VoxelType.Window);
        structure.RebuildMeshes();
        Assert.That(VertexCount(structure), Is.EqualTo(40));
        Assert.That(structure.GetVoxel(cell), Is.EqualTo(VoxelType.Window));
        Assert.That(structure.SetVoxel(cell, VoxelType.Air), Is.True);
        structure.RebuildMeshes();
        Assert.That(VertexCount(structure), Is.EqualTo(24));
    }

    [Test]
    public void TerrainWindowsKeepTheirTextureAtDistanceAndDoNotHideSolidNeighbours()
    {
        ChunkData data = new(16, 16, null, Vector3Int.zero);
        Array.Fill(data.voxels, VoxelType.Air);
        Chunk.SetVoxel(data, new Vector3Int(4, 4, 4), VoxelType.Window);
        Chunk.SetVoxel(data, new Vector3Int(5, 4, 4), VoxelType.Metal);
        MeshData near = DistanceSimplifier.BuildMesh(data, ChunkLodLevel.Full);
        Assert.That(near.vertices.Count, Is.EqualTo(44));
        Assert.That(near.colliderVertices.Count, Is.EqualTo(44));
        MeshData far = DistanceSimplifier.BuildMesh(data, ChunkLodLevel.Merge16);
        Assert.That(far.tiledUvs.Count(uv => uv.x == -1), Is.EqualTo(20));
        Chunk.SetVoxel(data, new Vector3Int(5, 4, 4), VoxelType.Infected_Growth);
        MeshData window = VoxelHelper.GetMeshData(data, 4, 4, 4, new MeshData(), VoxelType.Window);
        Assert.That(window.uvs.All(uv => uv.x > 5f / 16 && uv.x < 6f / 16), Is.True);
    }

    [Test]
    public void JsonValidationRejectsBadDataBeforeReplacingExistingStructures()
    {
        VoxelStructure structure = NewShip();
        string original = system.CaptureJson();
        StructureSaveData save = JsonUtility.FromJson<StructureSaveData>(original);
        save.structures[0].voxels.Add(save.structures[0].voxels[0]);
        Assert.Throws<InvalidDataException>(() => system.RestoreJson(JsonUtility.ToJson(save)));
        save = JsonUtility.FromJson<StructureSaveData>(original);
        save.structures[0].prefabs.Add(new StructurePrefabData { id = "one", prefabId = "missing", normal = Vector3Int.up });
        Assert.Throws<InvalidDataException>(() => system.RestoreJson(JsonUtility.ToJson(save)));
        Assert.Throws<InvalidDataException>(() => system.RestoreJson("{\"version\":99,\"structures\":[]}"));
        Assert.Throws<ArgumentException>(() => system.RestoreJson("not json"));
        save.structures[0].prefabs[0].prefabId = "Circuit";
        save.structures[0].prefabs[0].state = "{\"schemaVersion\":99}";
        Assert.Throws<InvalidOperationException>(() => system.RestoreJson(JsonUtility.ToJson(save)));
        Assert.That(system.CaptureJson(), Is.EqualTo(original));
    }

    [TestCase(1, 0, 0)]
    [TestCase(-1, 0, 0)]
    [TestCase(0, 1, 0)]
    [TestCase(0, -1, 0)]
    [TestCase(0, 0, 1)]
    [TestCase(0, 0, -1)]
    public void SpecialPrefabsFitEveryFaceAndCannotBePlacedTwice(int x, int y, int z)
    {
        VoxelStructure structure = NewStructure();
        structure.transform.rotation = Quaternion.Euler(20, 30, 10);
        structure.SetVoxel(Vector3Int.zero, VoxelType.Dark_Ship_Metal);
        structure.RebuildMeshes();
        Vector3Int normal = new(x, y, z);
        RaycastHit hit = Hit(structure, Vector3Int.zero, normal);
        Assert.That(structure.TryPlacePrefab(hit, "Circuit"), Is.True);
        Assert.That(structure.TryPlacePrefab(hit, "Circuit"), Is.False);
        StructureAttachment attachment = structure.Attachments.Single();
        Vector3 viewDirection = attachment.GetComponent<SpecialPrefabInteractable>().GetPreferredViewDirection();
        Assert.That(Vector3.Dot(viewDirection, structure.transform.TransformDirection(normal)), Is.GreaterThan(0.999f));
        Assert.That(structure.TryPlaceVoxel(hit, VoxelType.Metal), Is.False);
        Assert.That(structure.RemoveAttachment(attachment), Is.True);
        Assert.That(structure.TryPlaceVoxel(hit, VoxelType.Metal), Is.True);
    }

    [Test]
    public void SaveFileRoundTripsAndKeepsThePreviousSnapshotAsBackup()
    {
        string path = Path.Combine(Path.GetTempPath(), "structure-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            VoxelStructure structure = NewShip();
            structure.SetVoxel(Vector3Int.left, VoxelType.Dark_Ship_Metal);
            system.SaveToFile(path);
            string original = File.ReadAllText(path);
            structure.SetVoxel(Vector3Int.left + Vector3Int.up, VoxelType.Metal);
            system.SaveToFile(path);
            Assert.That(File.ReadAllText(path + ".bak"), Is.EqualTo(original));
            structure.SetVoxel(Vector3Int.left * 2, VoxelType.Metal);
            system.LoadFromFile(path);
            Assert.That(system.GetComponentInChildren<VoxelStructure>().VoxelCount, Is.EqualTo(58));
        }
        finally
        {
            foreach (string suffix in new[] { "", ".bak", ".tmp", ".library", ".library.bak", ".library.tmp" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [Test]
    public void CircuitComponentsWiresAndValuesSurviveStructureSaveLoad()
    {
        GameObject systems = NewObject("Circuit systems");
        systems.SetActive(false);
        ObjectPlacer placer = systems.AddComponent<ObjectPlacer>();
        CircuitWireSystem wires = systems.AddComponent<CircuitWireSystem>();
        placementSystem placement = systems.AddComponent<placementSystem>();
        CircuitPuzzleSystem puzzles = systems.AddComponent<CircuitPuzzleSystem>();
        SetReference(placer, "componentDatabase", AssetDatabase.LoadAssetAtPath<componentDatabaseSO>("Assets/_Scripts/circuitPlacementLogic/ComponentDatabase.asset"));
        SetReference(placement, "objectPlacer", placer);
        SetReference(placement, "wireSystem", wires);
        SetReference(puzzles, "placementSystem", placement);
        SetReference(puzzles, "objectPlacer", placer);
        SetReference(puzzles, "wireSystem", wires);
        placer.SetWireSystem(wires);
        VoxelStructure structure = NewShip();
        structure.transform.rotation = Quaternion.Euler(0, 35, 0);
        structure.SetVoxel(Vector3Int.left, VoxelType.Dark_Ship_Metal);
        structure.RebuildMeshes();
        Assert.That(structure.TryPlacePrefab(Hit(structure, Vector3Int.left, Vector3Int.forward), "Circuit"), Is.True);
        StructureAttachment attachment = structure.Attachments.Single();
        SpecialPrefabInteractable circuit = attachment.GetComponent<SpecialPrefabInteractable>();
        Assert.That(Vector3.Dot(circuit.GetPreferredViewDirection(), structure.transform.forward), Is.GreaterThan(0.999f));
        placement.SetActiveCircuit(circuit);
        Assert.That(placer.TryPlaceSnapshotComponent(0, new Vector3Int(1, 1, 0), Vector2Int.one, 0, 1500,
            CircuitComponentState.Broken, "Test fault", out PlacedCircuitComponent first), Is.True);
        Assert.That(placer.TryPlaceSnapshotComponent(0, new Vector3Int(5, 1, 0), Vector2Int.one, 0, 2200,
            CircuitComponentState.Healthy, "", out PlacedCircuitComponent second), Is.True);
        wires.RestoreWire(first.Element.Terminal2, second.Element.Terminal1, new List<Vector2Int> { new(2, 1), new(3, 1), new(4, 1) });
        placement.ClearActiveCircuit();
        string json = system.CaptureJson();
        system.RestoreJson(json);
        VoxelStructure restored = system.GetComponentInChildren<VoxelStructure>();
        World world = StreamingWorld();
        system.SetStreamingWorld(world);
        Assert.That(restored == null, Is.True);
        Assert.That(JsonUtility.FromJson<StructureSaveData>(system.CaptureJson()).structures[0].prefabs[0].state,
            Is.EqualTo(JsonUtility.FromJson<StructureSaveData>(json).structures[0].prefabs[0].state));
        world.worldData.chunkDictionary[SurfaceChunk(world, TestPosition)] = null;
        NotifyStreaming(world);
        restored = system.GetComponentInChildren<VoxelStructure>();
        Assert.That(restored.Attachments.Single().Data.id, Is.EqualTo(JsonUtility.FromJson<StructureSaveData>(json).structures[0].prefabs[0].id));
        var components = restored.GetComponentsInChildren<PlacedCircuitComponent>().OrderBy(c => c.GridPosition.x).ToArray();
        Assert.That(components.Length, Is.EqualTo(2));
        Assert.That(components[0].Element.Value, Is.EqualTo(1500));
        Assert.That(components[0].Element.State, Is.EqualTo(CircuitComponentState.Broken));
        Assert.That(components[0].Element.FailureReason, Is.EqualTo("Test fault"));
        Assert.That(restored.GetComponentsInChildren<WireConnection>().Length, Is.EqualTo(1));
        StructureData before = JsonUtility.FromJson<StructureSaveData>(json).structures[0];
        StructureData after = JsonUtility.FromJson<StructureSaveData>(system.CaptureJson()).structures[0];
        Assert.That(after.prefabs[0].state, Is.EqualTo(before.prefabs[0].state));
        Assert.That(Vector3.Distance(after.prefabs[0].position, before.prefabs[0].position), Is.LessThan(0.0001f));
        Assert.That(Quaternion.Angle(after.prefabs[0].rotation, before.prefabs[0].rotation), Is.LessThan(0.01f));
        Assert.That(placement.ActiveBoard, Is.Null);
        restored.SetVoxel(Vector3Int.left, VoxelType.Air);
        Assert.That(restored.Attachments, Is.Empty);
    }

    [Test]
    public void StreamingReleasesMeshesAndPreservesUnsavedVoxelsAndDistantSaves()
    {
        VoxelStructure ship = NewShip();
        ship.SetVoxel(Vector3Int.left, VoxelType.Window);
        ship.SetVoxel(new Vector3Int(2, 2, 2), VoxelType.Metal); // Detached: retain in memory, exclude from saves.
        ship.RebuildMeshes();
        Mesh mesh = ship.GetComponentInChildren<MeshFilter>().sharedMesh;
        string json = system.CaptureJson();
        World world = StreamingWorld();
        Vector3Int key = SurfaceChunk(world, ship.transform.position);
        world.worldData.chunkDictionary[key] = null;
        system.SetStreamingWorld(world);
        Assert.That(ship != null, Is.True);
        world.worldData.chunkDictionary.Clear();
        NotifyStreaming(world);
        Assert.That(ship == null, Is.True);
        Assert.That(mesh == null, Is.True);
        Assert.That(system.GetComponentsInChildren<Collider>(true), Is.Empty);
        Assert.That(system.CaptureJson(), Is.EqualTo(json));
        world.worldData.chunkDictionary[key] = null;
        NotifyStreaming(world);
        ship = system.GetComponentInChildren<VoxelStructure>();
        Assert.That(ship.VoxelCount, Is.EqualTo(58));
        Assert.That(ship.GetVoxel(Vector3Int.left), Is.EqualTo(VoxelType.Window));
        Assert.That(ship.GetComponentInChildren<MeshCollider>().sharedMesh, Is.Not.Null);
        NotifyStreaming(world);
        Assert.That(system.GetComponentsInChildren<VoxelStructure>().Length, Is.EqualTo(1));
        Assert.That(system.CaptureJson(), Is.EqualTo(json));
    }

    [Test]
    public void DistantFileLoadCreatesNoObjectsAndReplacesPreviousDormantShips()
    {
        NewShip();
        string json = system.CaptureJson();
        World world = StreamingWorld();
        system.SetStreamingWorld(world);
        system.RestoreJson(json);
        Assert.That(system.GetComponentsInChildren<VoxelStructure>(true), Is.Empty);
        Assert.That(system.CaptureJson(), Is.EqualTo(json));
        Assert.Throws<ArgumentException>(() => system.RestoreJson("not json"));
        Assert.That(system.CaptureJson(), Is.EqualTo(json));
        system.RestoreJson(JsonUtility.ToJson(new StructureSaveData()));
        world.worldData.chunkDictionary[SurfaceChunk(world, TestPosition)] = null;
        NotifyStreaming(world);
        Assert.That(system.GetComponentsInChildren<VoxelStructure>(), Is.Empty);
        Assert.That(JsonUtility.FromJson<StructureSaveData>(system.CaptureJson()).structures, Is.Empty);
    }

    [Test]
    public void RotatedShipStaysLoadedWhileAnyOfItsChunksAreVisible()
    {
        VoxelStructure ship = NewShip();
        ship.transform.position = new Vector3(-100, 100, -100);
        ship.transform.rotation = Quaternion.Euler(0, 90, 0);
        for (int x = 6; x <= 40; x++) ship.SetVoxel(new Vector3Int(x, 0, 0), VoxelType.Metal);
        Vector3 end = ship.transform.TransformPoint(Vector3.right * (40 * VoxelMetrics.Size));
        World world = StreamingWorld();
        Vector3Int endChunk = SurfaceChunk(world, end);
        Assert.That(endChunk, Is.Not.EqualTo(SurfaceChunk(world, ship.transform.position)));
        world.worldData.chunkDictionary[endChunk] = null;
        system.SetStreamingWorld(world);
        Assert.That(ship != null, Is.True);
        world.worldData.chunkDictionary.Clear();
        NotifyStreaming(world);
        Assert.That(ship == null, Is.True);
        world.worldData.chunkDictionary[endChunk] = null;
        NotifyStreaming(world);
        Assert.That(system.GetComponentInChildren<VoxelStructure>().GetVoxel(new Vector3Int(40, 0, 0)), Is.EqualTo(VoxelType.Metal));
    }

    [Test]
    public void LibraryKeepsShipsOutsideTheWorldAndUpdatesSavedEditsById()
    {
        string path = Path.Combine(Path.GetTempPath(), "ship-library-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            VoxelStructure ship = NewShip();
            ship.SetVoxel(Vector3Int.left, VoxelType.Metal);
            ship.SetVoxel(Vector3Int.down, VoxelType.Metal);
            system.SaveShip(ship, "  Night Scout  ", path);
            system.SelectSavedShip(system.ReadSavedShips(path).Single());
            ship.SetVoxel(Vector3Int.left, VoxelType.Air);
            ship.SetVoxel(new Vector3Int(0, 6, 0), VoxelType.Window);
            system.SaveToFile(path);
            Assert.That(system.ReadSavedShips(path).Single().voxels.Any(v => v.position == Vector3Int.left), Is.True);
            system.SaveShip(ship, "Night Scout", path);
            StructureData latest = system.ReadSavedShips(path).Single();
            Assert.That(latest.id, Is.EqualTo(ship.Id));
            Assert.That(latest.name, Is.EqualTo("Night Scout"));
            Assert.That(latest.position, Is.EqualTo(Vector3.zero));
            Assert.That(latest.voxels.Any(v => v.position == Vector3Int.left), Is.False);
            Assert.That(latest.voxels.Single(v => v.position == new Vector3Int(0, 6, 0)).type, Is.EqualTo(VoxelType.Window));
            Assert.That(JsonUtility.ToJson(system.SelectedShip), Is.EqualTo(JsonUtility.ToJson(latest)));
            system.RestoreJson(JsonUtility.ToJson(new StructureSaveData()));
            system.SaveToFile(path);
            Assert.That(system.ReadSavedShips(path).Single().id, Is.EqualTo(latest.id));
            VoxelStructure support = NewShip();
            system.SelectSavedShip(system.ReadSavedShips(path).Single());
            Assert.That(system.TryPlaceSavedShip(Hit(support, new Vector3Int(5, 5, 5), Vector3Int.up)), Is.True);
            VoxelStructure copy = system.GetComponentsInChildren<VoxelStructure>().Single(s => s != support);
            Assert.That(copy.GetVoxel(new Vector3Int(0, 6, 0)), Is.EqualTo(VoxelType.Window));
            Assert.That(copy.GetVoxel(Vector3Int.left), Is.EqualTo(VoxelType.Air));
            Assert.That(copy.GetVoxel(Vector3Int.down), Is.EqualTo(VoxelType.Metal));
            Assert.That(copy.Id, Is.Not.EqualTo(latest.id));
        }
        finally
        {
            foreach (string suffix in new[] { "", ".bak", ".tmp", ".library", ".library.bak", ".library.tmp" })
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [Test]
    public void LocalRenameAndDeleteMigrateLegacySavesWithoutChangingWorldOrBuiltInShips()
    {
        string path = Path.Combine(Path.GetTempPath(), "ship-library-" + Guid.NewGuid().ToString("N") + ".json");
        SetReference(system, "builtInShipLibrary", AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/_Prefabs/ShipLibrary.json"));
        var builtIn = system.ReadBuiltInShips();
        Assert.That(builtIn.Select(ship => ship.name), Is.EquivalentTo(new[] { "Mule Cargo Shuttle", "Pathfinder Scout" }));
        foreach (var ship in builtIn)
        {
            Assert.That(ship.ConnectedCopy(out _).voxels.Count, Is.EqualTo(ship.voxels.Count));
            Assert.That(ship.voxels.Any(v => v.position.y < 0), Is.True);
        }
        try
        {
            VoxelStructure ship = NewShip();
            File.WriteAllText(path, system.CaptureJson()); // Legacy world-only save.
            system.SelectSavedShip(builtIn[0]);
            Assert.That(system.RenameSavedShip(builtIn[0].id, "New name", path), Is.False);
            Assert.That(system.DeleteSavedShip(builtIn[0].id, path), Is.False);
            Assert.That(system.SelectedShip, Is.SameAs(builtIn[0]));
            Assert.That(system.RenameSavedShip(ship.Id, "Legacy Scout", path), Is.True);
            Assert.That(system.ReadSavedShips(path).Single().name, Is.EqualTo("Legacy Scout"));
            Assert.That(system.DeleteSavedShip(ship.Id, path), Is.True);
            system.SaveToFile(path);
            Assert.That(system.ReadSavedShips(path), Is.Empty);
            Assert.That(JsonUtility.FromJson<StructureSaveData>(File.ReadAllText(path)).structures.Single().id, Is.EqualTo(ship.Id));
        }
        finally
        {
            foreach (string suffix in new[] { "", ".bak", ".library", ".library.bak" }) File.Delete(path + suffix);
        }
    }

    [Test]
    public void ShipCopiesHaveFreshIdsAndPreviewRejectsOccupiedSpaceWithoutMutatingTheSource()
    {
        VoxelStructure source = NewShip();
        Assert.That(source.TryPlacePrefab(Hit(source, Vector3Int.zero, Vector3Int.back), "Circuit"), Is.True);
        UnityEngine.Object.DestroyImmediate(source.Attachments.Single().GetComponent<CircuitStructureState>());
        StructureData template = source.Capture();
        string original = JsonUtility.ToJson(template);
        system.SelectSavedShip(template);
        RaycastHit hit = Hit(source, new Vector3Int(5, 5, 5), Vector3Int.up);
        system.ShowSavedShipPreview(hit);
        GameObject ghost = GameObject.Find("Frame placement preview");
        Assert.That(ghost.GetComponentsInChildren<Collider>(), Is.Empty);
        Mesh mesh = ghost.GetComponent<MeshFilter>().sharedMesh;
        system.ShowSavedShipPreview(hit);
        Assert.That(ghost.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(mesh));
        Assert.That(system.TryPlaceSavedShip(hit), Is.True);
        Assert.That(system.TryGetSavedShipPlacement(hit, out BlueprintPlacement blocked), Is.True);
        Assert.That(blocked.canPlace, Is.False);
        Assert.That(system.TryPlaceSavedShip(hit), Is.False);
        Assert.That(system.TryPlaceSavedShip(Hit(source, new Vector3Int(0, 5, 0), Vector3Int.left)), Is.True);
        VoxelStructure[] ships = system.GetComponentsInChildren<VoxelStructure>();
        Assert.That(ships.Length, Is.EqualTo(3));
        Assert.That(ships.Select(s => s.Id).Distinct().Count(), Is.EqualTo(3));
        Assert.That(ships.Select(s => s.Attachments.Single().Data.id).Distinct().Count(), Is.EqualTo(3));
        VoxelStructure copy = ships.First(s => s != source);
        copy.SetVoxel(Vector3Int.left, VoxelType.Window);
        Assert.That(source.GetVoxel(Vector3Int.left), Is.EqualTo(VoxelType.Air));
        Assert.That(JsonUtility.ToJson(template), Is.EqualTo(original));
        system.SelectSavedShip(null);
        Assert.That(ghost.activeSelf, Is.False);
    }

    [Test]
    public void SaveAndLoadHaveDistinctInputSystemBindings()
    {
        InputActionAsset input = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
        Assert.That(input.FindAction("Player/SaveStructures").bindings.Single().path, Is.EqualTo("<Keyboard>/f5"));
        Assert.That(input.FindAction("Player/LoadStructures").bindings.Single().path, Is.EqualTo("<Keyboard>/f9"));
    }

    private TextAsset Frame => AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/_Prefabs/InventoryPrefabs/Voxels/MetalFrame/MetalFrame.json");
    private TextAsset MasterFrame => AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/_Prefabs/InventoryPrefabs/Voxels/MasterFrame/MasterFrame.json");
    private VoxelStructure NewStructure() => system.CreateStructure("Test ship", TestPosition, Quaternion.identity);
    private World StreamingWorld()
    {
        GameObject root = NewObject("Streaming world");
        root.SetActive(false);
        World world = root.AddComponent<World>();
        world.chunkSize = world.chunkHeight = 16;
        typeof(World).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(world, null);
        return world;
    }
    private static Vector3Int SurfaceChunk(World world, Vector3 position)
    {
        Vector3Int key = WorldDataHelper.ChunkPositionFromVoxelCoords(world, VoxelMetrics.WorldToVoxelCoord(position));
        key.y = 0;
        return key;
    }
    private static void NotifyStreaming(World world) =>
        ((Action)typeof(World).GetField("OnChunkVisibilityChanged", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(world))?.Invoke();
    private VoxelStructure NewShip()
    {
        VoxelStructure structure = NewStructure();
        StructureData frame = FrameBlueprint.Create(true);
        frame.position = TestPosition;
        structure.Restore(frame);
        return structure;
    }
    private static int VertexCount(VoxelStructure structure) => structure.GetComponentsInChildren<MeshFilter>().Sum(f => f.sharedMesh.vertexCount);

    private GameObject NewObject(string name)
    {
        GameObject value = new(name);
        objects.Add(value);
        return value;
    }

    private static RaycastHit Hit(VoxelStructure structure, Vector3Int cell, Vector3Int normal)
    {
        Physics.SyncTransforms();
        Vector3 direction = structure.transform.TransformDirection(normal);
        Vector3 center = structure.transform.TransformPoint((Vector3)cell * VoxelMetrics.Size);
        Assert.That(Physics.Raycast(center + direction * 1f, -direction, out RaycastHit hit, 2f), Is.True);
        return hit;
    }

    private static void SetReference(UnityEngine.Object target, string name, UnityEngine.Object value)
    {
        SerializedObject serialized = new(target);
        serialized.FindProperty(name).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
