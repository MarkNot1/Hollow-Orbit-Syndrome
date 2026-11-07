using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class VoxelInventoryTests
{
    private readonly List<Object> objects = new();
    private readonly List<InputDevice> devices = new();
    private readonly List<PlayerInput> inputs = new();
    private CursorLockMode cursorLock;
    private bool cursorVisible;
    private int placedCount, destroyedCount;
    private void CountPlaced() => placedCount++;
    private void CountDestroyed() => destroyedCount++;
    private static readonly Vector3 Origin = new(1000f, 1000f, 1000f);

    [SetUp]
    public void SetUp()
    {
        cursorLock = Cursor.lockState;
        cursorVisible = Cursor.visible;
        VoxelRuntimeData.Capture(AssetDatabase.LoadAssetAtPath<VoxelDataSO>("Assets/_Scripts/World Generation/VoxelData.asset"));
    }

    [TearDown]
    public void TearDown()
    {
        foreach (PlayerInput input in inputs) if (input != null) Call(input, "OnDestroy");
        foreach (InputDevice device in devices) InputSystem.RemoveDevice(device);
        for (int i = objects.Count - 1; i >= 0; i--) if (objects[i] != null) Object.DestroyImmediate(objects[i]);
        objects.Clear(); devices.Clear(); inputs.Clear();
        Cursor.lockState = cursorLock;
        Cursor.visible = cursorVisible;
    }

    [UnityTearDown]
    public IEnumerator ExitPlayModeAfterInputTest()
    {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    private GameObject NewObject(string name, bool active = true)
    {
        GameObject go = new(name);
        go.SetActive(active);
        objects.Add(go);
        return go;
    }

    private Inventory NewInventory() => NewObject("Voxel inventory", false).AddComponent<Inventory>();
    private ItemSO NewItem(bool unlimited = true)
    {
        ItemSO item = ScriptableObject.CreateInstance<ItemSO>();
        objects.Add(item);
        item.unlimited = unlimited;
        item.placementKind = ItemPlacementKind.TerrainVoxel;
        item.voxelType = VoxelType.Dark_Ship_Metal;
        return item;
    }

    private PlayerInput NewInput()
    {
        PlayerInput input = NewObject("Voxel input", false).AddComponent<PlayerInput>();
        InputActionAsset actions = Object.Instantiate(AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions"));
        objects.Add(actions);
        Set(input, "inputActions", actions);
        Camera camera = NewObject("Voxel test camera").AddComponent<Camera>();
        camera.enabled = false;
        camera.transform.position = Origin;
        Set(input, "reticleCamera", camera);
        Call(input, "Awake");
        inputs.Add(input);
        return input;
    }

    [Test]
    public void UnlimitedEntriesRemainAvailableAndCannotBeDroppedOrRemoved()
    {
        Inventory inventory = NewInventory();
        ItemSO voxel = NewItem();
        inventory.AddItem(voxel, 100);
        inventory.AddItem(voxel, int.MaxValue);
        Assert.That(inventory.Items.Count, Is.EqualTo(1));
        Assert.That(inventory.GetAmount(voxel), Is.EqualTo(1));
        Assert.That(inventory.RemoveItem(voxel, 1), Is.False);
        Assert.That(inventory.DropItem(voxel, 1), Is.False);
        inventory.AssignSelectedToHotbar(5);
        Assert.That(inventory.EquippedItem, Is.SameAs(voxel));
    }

    [Test]
    public void CyclingWrapsIncludesEmptySlotsAndDoesNotScrollWhileMenusOwnInput()
    {
        Inventory inventory = NewInventory();
        ItemSO voxel = NewItem();
        inventory.AddItem(voxel, 1);
        inventory.CycleHotbar(-1);
        Assert.That(inventory.EquippedHotbarIndex, Is.EqualTo(5));
        Assert.That(inventory.EquippedItem, Is.Null);
        inventory.CycleHotbar(1);
        Assert.That(inventory.EquippedItem, Is.SameAs(voxel));
        inventory.SetOpen(true);
        inventory.CycleHotbar(1);
        Assert.That(inventory.EquippedHotbarIndex, Is.Zero);
        inventory.SetOpen(false);
        PlayerInput input = NewInput();
        Set(inventory, "playerInput", input);
        input.SetGameplayInputBlocked(true);
        inventory.CycleHotbar(1);
        inventory.SetOpen(true);
        Assert.That(inventory.IsOpen, Is.False);
        Assert.That(inventory.EquippedHotbarIndex, Is.Zero);
        // Inventory cleanup cannot release the inspection lock.
        Call(inventory, "OnDisable");
        Assert.That(input.IsGameplayInputBlocked, Is.True);
    }

    [UnityTest]
    public IEnumerator MouseActionsUseVoxelNamesAndFireOncePerPress()
    {
        yield return new EnterPlayMode();
        InputSettings previousSettings = InputSystem.settings;
        bool previousBackground = Application.runInBackground;
        InputSettings testSettings = Object.Instantiate(previousSettings);
        try
        {
            testSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            testSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings = testSettings;
            Application.runInBackground = true;
            PlayerInput input = NewInput();
            InputActionAsset actions = Get<InputActionAsset>(input, "inputActions");
            InputActionMap map = actions.FindActionMap("Player");
            Assert.That(map.FindAction("PlaceBlock"), Is.Null);
            Assert.That(map.FindAction("DestroyBlock"), Is.Null);
            Assert.That(map.FindAction("DestroyVoxel").id.ToString(), Is.EqualTo("c3d4e5f6-a7b8-9012-cdef-345678901234"));
            placedCount = destroyedCount = 0;
            input.OnPlaceVoxel += CountPlaced;
            input.OnDestroyVoxel += CountDestroyed;
            Mouse mouse = InputSystem.AddDevice<Mouse>(); devices.Add(mouse);
            map.Enable();
            Assert.That(map.FindAction("PlaceVoxel").controls, Does.Contain(mouse.rightButton));
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Right)); InputSystem.Update();
            Assert.That(placedCount, Is.EqualTo(1)); Assert.That(destroyedCount, Is.Zero);
            InputSystem.Update(); Assert.That(placedCount, Is.EqualTo(1));
            InputSystem.QueueStateEvent(mouse, new MouseState()); InputSystem.Update();
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left)); InputSystem.Update();
            Assert.That(destroyedCount, Is.EqualTo(1));
            input.SetGameplayInputBlocked(true);
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Right)); InputSystem.Update();
            Assert.That(placedCount, Is.EqualTo(1));
            map.Disable();
        }
        finally
        {
            InputSystem.settings = previousSettings;
            Application.runInBackground = previousBackground;
            Object.DestroyImmediate(testSettings);
        }
        yield return new ExitPlayMode();
    }

    [Test]
    public void ReticleReachIsFifteenUnitsAndDoesNotSelectThroughObstructions()
    {
        PlayerInput input = NewInput();
        Assert.That(input.InteractionReach, Is.EqualTo(15f));
        GameObject pickup = NewObject("Reach pickup");
        pickup.AddComponent<BoxCollider>().size = Vector3.one * 0.2f;
        Item item = pickup.AddComponent<Item>();
        pickup.transform.position = Origin + Vector3.forward * 15.05f;
        Physics.SyncTransforms();
        Assert.That(input.TryGetReticleTarget(out Item found), Is.True);
        Assert.That(found, Is.SameAs(item));
        pickup.transform.position = Origin + Vector3.forward * 15.2f;
        Physics.SyncTransforms();
        Assert.That(input.TryGetReticleHit(out _), Is.False);
        pickup.transform.position = Origin + Vector3.forward * 10f;
        GameObject obstacle = NewObject("Reach obstruction");
        obstacle.AddComponent<BoxCollider>(); obstacle.transform.position = Origin + Vector3.forward * 5f;
        Physics.SyncTransforms();
        Assert.That(input.TryGetReticleTarget(out Item _), Is.False);
        Assert.That(input.TryGetReticleHit(out RaycastHit hit), Is.True);
        Assert.That(hit.collider.gameObject, Is.SameAs(obstacle));
    }

    [UnityTest]
    public IEnumerator HeldVisualHidesForMenusAndIsRemovedWithInventory()
    {
        yield return new EnterPlayMode();
        Inventory inventory = NewInventory();
        PlayerInput input = NewInput();
        Set(inventory, "inputActions", Get<InputActionAsset>(input, "inputActions"));
        Set(inventory, "playerInput", input);
        inventory.hand = NewObject("Hand anchor").transform;
        ItemSO item = NewItem();
        item.handItemPrefab = NewObject("Held visual prefab");
        inventory.AddItem(item, 1);
        inventory.gameObject.SetActive(true);
        yield return null;
        GameObject held = Get<GameObject>(inventory, "currentHandItem");
        Call(inventory, "LateUpdate");
        Assert.That(held.activeSelf, Is.True);
        inventory.SetOpen(true);
        Call(inventory, "LateUpdate");
        Assert.That(held.activeSelf, Is.False);
        inventory.SetOpen(false);
        input.SetGameplayInputBlocked(true);
        Call(inventory, "LateUpdate");
        Assert.That(held.activeSelf, Is.False);
        input.SetGameplayInputBlocked(false);
        Call(inventory, "LateUpdate");
        Assert.That(held.activeSelf, Is.True);
        Object.Destroy(inventory.gameObject);
        yield return null;
        yield return null;
        Assert.That(held == null, Is.True);
        yield return new ExitPlayMode();
    }

    private World NewWorld(Vector3Int surface, out ChunkData data, out RaycastHit hit)
    {
        World world = NewObject("Placement world", false).AddComponent<World>();
        world.chunkSize = 4; world.chunkHeight = 4;
        Call(world, "Awake");
        Vector3Int key = WorldDataHelper.ChunkPositionFromVoxelCoords(world, surface);
        data = new ChunkData(4, 4, world, key);
        world.worldData.chunkDataDictionary[key] = data;
        Chunk.SetVoxel(data, surface - key, VoxelType.Dark_Ship_Metal);
        GameObject chunkObject = NewObject("Placement surface");
        ChunkRenderer renderer = chunkObject.AddComponent<ChunkRenderer>();
        renderer.InitializeChunk(data);
        BoxCollider collider = chunkObject.AddComponent<BoxCollider>();
        collider.size = Vector3.one * VoxelMetrics.Size;
        chunkObject.transform.position = (Vector3)surface * VoxelMetrics.Size;
        Physics.SyncTransforms();
        Assert.That(collider.Raycast(new Ray(chunkObject.transform.position + Vector3.up * 2f, Vector3.down), out hit, 3f), Is.True);
        return world;
    }

    [TestCase(VoxelType.Dark_Ship_Metal)]
    [TestCase(VoxelType.Infected_Growth)]
    [TestCase(VoxelType.Ship_Metal)]
    [TestCase(VoxelType.Metal)]
    [TestCase(VoxelType.Striped_Ship_Metal)]
    [TestCase(VoxelType.Window)]
    public void PlacementWritesSelectedTypeAndRejectsRepeatedPlacementBeforeRemeshing(VoxelType type)
    {
        Vector3Int surface = new(2000, 2000, 2000);
        World world = NewWorld(surface, out ChunkData data, out RaycastHit hit);
        Assert.That(PlaceVoxel(world, hit, type), Is.True);
        Assert.That(Chunk.GetVoxelFromChunkCoordinates(data, Vector3Int.up), Is.EqualTo(type));
        int version = data.ContentVersion;
        Assert.That(PlaceVoxel(world, hit, VoxelType.Metal), Is.False);
        Assert.That(data.ContentVersion, Is.EqualTo(version));
        Assert.That(data.modifiedByThePlayer, Is.True);
    }

    [Test]
    public void PlacementRejectsInvalidTypesMissingChunksAndPlayerOverlap()
    {
        World world = NewWorld(new Vector3Int(2000, 2003, 2000), out ChunkData data, out RaycastHit hit);
        Assert.That(PlaceVoxel(world, hit, VoxelType.Air), Is.False);
        Assert.That(PlaceVoxel(world, hit, (VoxelType)255), Is.False);
        Assert.That(PlaceVoxel(world, hit, VoxelType.Metal), Is.False);
        Vector3Int nextKey = data.worldPosition + Vector3Int.up * 4;
        ChunkData next = new(4, 4, world, nextKey);
        world.worldData.chunkDataDictionary[nextKey] = next;
        GameObject player = NewObject("Player overlap");
        player.AddComponent<BoxCollider>().size = Vector3.one * 0.4f;
        player.transform.position = (Vector3)nextKey * VoxelMetrics.Size;
        Physics.SyncTransforms();
        Assert.That(PlaceVoxel(world, hit, VoxelType.Metal), Is.False);
        player.SetActive(false);
        Assert.That(PlaceVoxel(world, hit, VoxelType.Metal), Is.True);
        Assert.That(next.voxels[0], Is.EqualTo(VoxelType.Metal));
        Assert.That(next.modifiedByThePlayer, Is.True);
    }

    [Test]
    public void EquippedItemControlsPlacementAndEmptyOrOrdinaryItemsDoNothing()
    {
        Vector3Int surface = new(2000, 2000, 2020);
        World world = NewWorld(surface, out ChunkData data, out _);
        PlayerInput input = NewInput();
        Camera camera = Get<Camera>(input, "reticleCamera");
        camera.transform.position = (Vector3)surface * VoxelMetrics.Size + Vector3.up * 14.9f;
        camera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
        Inventory inventory = NewInventory();
        ItemSO darkMetal = NewItem(), metal = NewItem(); metal.voxelType = VoxelType.Metal;
        inventory.AddItem(darkMetal, 1); inventory.AddItem(metal, 1);
        PlayerPlacementController placement = input.gameObject.AddComponent<PlayerPlacementController>();
        Set(placement, "world", world); Set(placement, "inventory", inventory); Call(placement, "Awake");
        inventory.SelectHotbar(1);
        Assert.That(placement.TryPlaceEquippedVoxel(), Is.True);
        Assert.That(Chunk.GetVoxelFromChunkCoordinates(data, Vector3Int.up), Is.EqualTo(VoxelType.Metal));
        Assert.That(inventory.GetAmount(metal), Is.EqualTo(1));
        Chunk.SetVoxel(data, Vector3Int.up, VoxelType.Air);
        inventory.SelectHotbar(0);
        camera.transform.position += Vector3.up;
        Assert.That(placement.TryPlaceEquippedVoxel(), Is.False);
        camera.transform.position -= Vector3.up;
        inventory.SelectHotbar(5);
        Assert.That(placement.TryPlaceEquippedVoxel(), Is.False);
        inventory.SelectHotbar(0); darkMetal.placementKind = ItemPlacementKind.None;
        Assert.That(placement.TryPlaceEquippedVoxel(), Is.False);
        darkMetal.placementKind = ItemPlacementKind.TerrainVoxel;
        input.SetGameplayInputBlocked(true);
        Assert.That(placement.TryPlaceEquippedVoxel(), Is.False);
    }

    [Test]
    public void CircuitUsesTheEquippedPaletteEntryAndSerializedDatabase()
    {
        Vector3Int surface = new(2000, 2000, 2020);
        World world = NewWorld(surface, out _, out RaycastHit hit);
        Set(world, "specialPrefabDatabase", AssetDatabase.LoadAssetAtPath<SpecialPrefabDatabaseSO>(
            "Assets/_Scripts/World Generation/SpecialPrefabDatabase.asset"));
        ChunkRenderer renderer = hit.collider.GetComponent<ChunkRenderer>();
        world.worldData.chunkDictionary[renderer.ChunkData.worldPosition] = renderer;
        PlayerInput input = NewInput();
        Camera camera = Get<Camera>(input, "reticleCamera");
        camera.transform.position = (Vector3)surface * VoxelMetrics.Size + Vector3.up * 2f;
        camera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
        Inventory inventory = NewInventory();
        ItemSO item = NewItem();
        item.placementKind = ItemPlacementKind.SpecialPrefab; item.specialPrefabId = "Circuit";
        inventory.AddItem(item, 1);
        PlayerPlacementController placement = input.gameObject.AddComponent<PlayerPlacementController>();
        Set(placement, "world", world); Set(placement, "inventory", inventory); Call(placement, "Awake");
        Assert.That(placement.TryPlaceEquippedVoxel(), Is.True);
        Assert.That(world.GetComponentsInChildren<SpecialPrefabInteractable>(true).Length, Is.EqualTo(1));
        Assert.That(placement.TryPlaceEquippedVoxel(), Is.False);
        Assert.That(inventory.GetAmount(item), Is.EqualTo(1));
    }

    [Test]
    public void DestructionUsesTheSameFifteenUnitReachAndRejectsStaleHits()
    {
        Vector3Int surface = new(2000, 2000, 2020);
        World world = NewWorld(surface, out ChunkData data, out RaycastHit hit);
        PlayerInput input = NewInput();
        Camera camera = Get<Camera>(input, "reticleCamera");
        camera.transform.position = (Vector3)surface * VoxelMetrics.Size + Vector3.up * 15.3f;
        camera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
        Character character = input.gameObject.AddComponent<Character>();
        Set(character, "playerInput", input); character.world = world;
        Call(character, "TryDeleteTargetVoxel");
        Assert.That(data.voxels[0], Is.EqualTo(VoxelType.Dark_Ship_Metal));
        camera.transform.position -= Vector3.up * 0.1f;
        Call(character, "TryDeleteTargetVoxel");
        Assert.That(data.voxels[0], Is.EqualTo(VoxelType.Air));
        Assert.That((bool)typeof(World).GetMethod("SetVoxel", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(world, new object[] { hit, VoxelType.Air }), Is.False);
    }

    [Test]
    public void ScenePaletteContainsFrameAndStripedItemsWithSixHotbarSlotsAndFifteenUnitReach()
    {
        Scene preview = EditorSceneManager.OpenPreviewScene("Assets/Scenes/SampleScene.unity");
        try
        {
            Inventory inventory = preview.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<Inventory>(true)).Single();
            Assert.That(inventory.Items.Take(5).Select(e => e.Item.itemName), Is.EqualTo(new[] { "Dark Ship Metal", "Infected Growth", "Ship Metal", "Metal", "Circuit" }));
            ItemSO[] palette = inventory.Items.Select(e => e.Item).Where(item => item.placementKind != ItemPlacementKind.None).ToArray();
            Assert.That(palette.Length, Is.EqualTo(9));
            foreach (ItemSO item in palette)
            {
                Assert.That(item.unlimited, Is.True);
                Assert.That(item.itemIcon, Is.Not.Null);
                Assert.That(item.itemIcon.rect.size, Is.EqualTo(new Vector2(128, 128)));
                Texture2D icon = new(2, 2); objects.Add(icon);
                Assert.That(icon.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(item.itemIcon))), Is.True);
                Assert.That(icon.GetPixel(0, 0).a, Is.Zero, item.itemName + " icon background must be transparent.");
                Assert.That(icon.GetPixels().Any(pixel => pixel.a > 0.9f), Is.True);
                Assert.That(item.handItemPrefab.GetComponentsInChildren<Collider>(true), Is.Empty);
                Assert.That(item.handItemPrefab.GetComponentsInChildren<MonoBehaviour>(true), Is.Empty);
            }
            for (int i = 0; i < 5; i++)
                Assert.That(Get<ItemSO[]>(inventory, "hotbarItems")[i], Is.SameAs(inventory.Items[i].Item));
            Assert.That(Get<ItemSO[]>(inventory, "hotbarItems")[5].placementKind, Is.EqualTo(ItemPlacementKind.StructureBlueprint));
            Assert.That(Get<ItemSO[]>(inventory, "hotbarItems")[5].itemName, Is.EqualTo("Master Frame"));
            Assert.That(Get<ItemSO[]>(inventory, "hotbarItems")[5].structureBlueprint, Is.Not.Null);
            Assert.That(palette.Any(item => item.itemName == "Metal Frame"), Is.True);
            Assert.That(palette.Any(item => item.voxelType == VoxelType.Striped_Ship_Metal), Is.True);
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Prefabs/Player.prefab");
            Assert.That(player.GetComponent<PlayerInput>().InteractionReach, Is.EqualTo(15f));
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }

    [Test]
    public void CubeUsesSeparateTopBottomAndSideAtlasCoordinates()
    {
        VoxelDataSO source = ScriptableObject.CreateInstance<VoxelDataSO>(); objects.Add(source);
        source.textureSizeX = source.textureSizeY = 1f / 16f;
        source.textureDataList = new List<TextureData> { new() { voxelType = VoxelType.Dark_Ship_Metal,
            up = new Vector2Int(1, 2), down = new Vector2Int(3, 4), side = new Vector2Int(5, 6) } };
        Mesh cube = VoxelPaletteEditor.BuildCube(source, VoxelType.Dark_Ship_Metal); objects.Add(cube);
        Assert.That(cube.vertexCount, Is.EqualTo(24));
        Assert.That(cube.uv[0].x, Is.EqualTo(1f / 16f + VoxelDataManager.textureOffset).Within(0.00001f));
        Assert.That(cube.uv[4].x, Is.EqualTo(3f / 16f + VoxelDataManager.textureOffset).Within(0.00001f));
        Assert.That(cube.uv[8].x, Is.EqualTo(5f / 16f + VoxelDataManager.textureOffset).Within(0.00001f));
        Assert.That(VoxelMetrics.FaceNormal(new Vector3(0.7f, 0.8f, 0f)), Is.EqualTo(Vector3Int.up));
    }

    [Test]
    public void FramePreviewHidesForInventoryInspectionEmptySlotsAndMissingTargets()
    {
        StructureSystem structures = NewObject("Structures").AddComponent<StructureSystem>();
        Set(structures, "framePreviewMaterial", AssetDatabase.LoadAssetAtPath<Material>("Assets/_Textures/FramePreview.mat"));
        VoxelStructure structure = structures.CreateStructure("Ship", Origin + Vector3.forward * 3, Quaternion.identity);
        structure.SetVoxel(Vector3Int.zero, VoxelType.Metal);
        structure.RebuildMeshes();
        PlayerInput input = NewInput();
        Inventory inventory = NewInventory();
        ItemSO frame = AssetDatabase.LoadAssetAtPath<ItemSO>(VoxelPaletteEditor.AssetFolder + "/MetalFrameItem.asset");
        inventory.AddItem(frame, 1);
        PlayerPlacementController controller = input.gameObject.AddComponent<PlayerPlacementController>();
        Set(controller, "playerInput", input);
        Set(controller, "inventory", inventory);
        Set(controller, "structures", structures);
        Physics.SyncTransforms();
        Call(controller, "LateUpdate");
        GameObject preview = GameObject.Find("Frame placement preview");
        Assert.That(preview, Is.Not.Null);
        inventory.SetOpen(true);
        Call(controller, "LateUpdate");
        Assert.That(preview.activeSelf, Is.False);
        inventory.SetOpen(false);
        Call(controller, "LateUpdate");
        Assert.That(preview.activeSelf, Is.True);
        input.SetGameplayInputBlocked(true);
        Call(controller, "LateUpdate");
        Assert.That(preview.activeSelf, Is.False);
        input.SetGameplayInputBlocked(false);
        inventory.SelectHotbar(5);
        Call(controller, "LateUpdate");
        Assert.That(preview.activeSelf, Is.False);
        inventory.SelectHotbar(0);
        structure.gameObject.SetActive(false);
        Physics.SyncTransforms();
        Call(controller, "LateUpdate");
        Assert.That(preview.activeSelf, Is.False);
    }

    [Test]
    public void WindowAtlasHasAClearCentreAndPreservesExistingTiles()
    {
        Texture2D oldAtlas = new(2, 2), atlas = new(2, 2);
        objects.Add(oldAtlas); objects.Add(atlas);
        oldAtlas.LoadImage(File.ReadAllBytes("Assets/_Textures/pixil-frame-0 (4).png"));
        atlas.LoadImage(File.ReadAllBytes("Assets/_Textures/VoxelAtlas.png"));
        Assert.That(atlas.GetPixel(5 * 32 + 16, 16).a, Is.Zero);
        Assert.That(atlas.GetPixel(5 * 32 + 16, 0).a, Is.GreaterThan(.9f));
        foreach (Vector2Int tile in new[] { new Vector2Int(0, 0), new(2, 0), new(0, 2), new(1, 2), new(4, 0) })
            Assert.That(atlas.GetPixels(tile.x * 32, tile.y * 32, 32, 32), Is.EqualTo(oldAtlas.GetPixels(tile.x * 32, tile.y * 32, 32, 32)));
    }

    private static bool PlaceVoxel(World world, RaycastHit hit, VoxelType type) =>
        (bool)typeof(World).GetMethod("PlaceVoxel", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(world, new object[] { hit, type });
    private static void Set(object target, string field, object value) =>
        target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static void Call(object target, string method) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
}
