using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class InventoryTests
{
    private readonly List<Object> created = new();
    private Scene preview;
    private CursorLockMode cursorLock;
    private bool cursorVisible;

    [SetUp]
    public void SetUp() { cursorLock = Cursor.lockState; cursorVisible = Cursor.visible; }

    [TearDown]
    public void TearDown()
    {
        if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
        for (int i = created.Count - 1; i >= 0; i--)
            if (created[i] != null) Object.DestroyImmediate(created[i]);
        created.Clear();
        Cursor.lockState = cursorLock;
        Cursor.visible = cursorVisible;
    }

    private Inventory NewInventory()
    {
        GameObject go = new("Inventory test");
        go.SetActive(false);
        created.Add(go);
        return go.AddComponent<Inventory>();
    }

    private ItemSO NewItem(string name = "Test item")
    {
        ItemSO item = ScriptableObject.CreateInstance<ItemSO>();
        item.itemName = name;
        item.maxStackSize = 10;
        created.Add(item);
        return item;
    }

    [Test]
    public void ListAggregatesQuantitiesWithoutGridCapacity()
    {
        Inventory inventory = NewInventory();
        ItemSO item = NewItem();
        inventory.AddItem(item, 250);
        inventory.AddItem(item, 5);
        for (int i = 0; i < 50; i++) inventory.AddItem(NewItem(i.ToString()), 1);
        Assert.That(inventory.Items.Count, Is.EqualTo(51));
        Assert.That(inventory.GetAmount(item), Is.EqualTo(255));
        Assert.That(inventory.Items[0].Item, Is.SameAs(item));
    }

    [Test]
    public void InvalidAmountsAndOverflowLeaveDataUnchanged()
    {
        Inventory inventory = NewInventory();
        ItemSO item = NewItem();
        inventory.AddItem(null, 1);
        inventory.AddItem(item, 0);
        inventory.AddItem(item, -1);
        Assert.That(inventory.Items, Is.Empty);
        inventory.AddItem(item, int.MaxValue);
        inventory.AddItem(item, 1);
        Assert.That(inventory.GetAmount(item), Is.EqualTo(int.MaxValue));
        Assert.That(inventory.RemoveItem(item, -1), Is.False);
        Assert.That(inventory.RemoveItem(null, 1), Is.False);
    }

    [Test]
    public void ShortcutsShareQuantityAndClearWhenLastItemIsRemoved()
    {
        Inventory inventory = NewInventory();
        ItemSO first = NewItem("First"), second = NewItem("Second");
        inventory.AddItem(first, 5);
        inventory.AddItem(second, 2);
        inventory.SelectItem(first);
        inventory.AssignSelectedToHotbar(5);
        Assert.That(inventory.EquippedItem, Is.SameAs(first));
        Assert.That(inventory.GetAmount(first), Is.EqualTo(5));
        Assert.That(inventory.RemoveItem(first, 6), Is.False);
        Assert.That(inventory.RemoveItem(first, 3), Is.True);
        Assert.That(inventory.GetAmount(first), Is.EqualTo(2));
        Assert.That(inventory.RemoveItem(first, 2), Is.True);
        Assert.That(inventory.SelectedItem, Is.SameAs(second));
        inventory.SelectHotbar(0);
        Assert.That(inventory.EquippedItem, Is.Null);
        inventory.SelectHotbar(5);
        Assert.That(inventory.EquippedItem, Is.Null);
        inventory.RemoveItem(second, 2);
        Assert.That(inventory.Items, Is.Empty);
        Assert.That(inventory.SelectedItem, Is.Null);
    }

    [Test]
    public void SerializedListAndShortcutsRoundTrip()
    {
        Inventory source = NewInventory(), restored = NewInventory();
        ItemSO item = AssetDatabase.LoadAssetAtPath<ItemSO>("Assets/_Prefabs/SMD components/Resistor/Resistor.asset");
        source.AddItem(item, 37);
        source.AssignSelectedToHotbar(4);
        EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(source), restored);
        Assert.That(restored.Items.Count, Is.EqualTo(1));
        Assert.That(restored.GetAmount(item), Is.EqualTo(37));
        restored.SelectHotbar(4);
        Assert.That(restored.EquippedItem, Is.SameAs(item));
    }

    private Inventory OpenScene(out InventoryMenu menu, out Canvas canvas)
    {
        preview = EditorSceneManager.OpenPreviewScene("Assets/Scenes/SampleScene.unity");
        canvas = preview.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<Canvas>(true)).First();
        // A preview scene has no Game view to supply the overlay canvas dimensions.
        GameObject cameraObject = new("UI test camera");
        SceneManager.MoveGameObjectToScene(cameraObject, preview);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.scene = preview;
        RenderTexture target = new(1600, 900, 24);
        created.Add(target);
        camera.targetTexture = target;
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 1;
        Inventory inventory = canvas.GetComponentInChildren<Inventory>(true);
        menu = inventory.GetComponent<InventoryMenu>();
        Assert.That(menu, Is.Not.Null);
        inventory.hand = null; // No hand rendering is needed in these edit-mode UI checks.
        // Legacy finite-inventory scenarios start empty; palette wiring has its own tests.
        var serialized = new SerializedObject(inventory);
        serialized.FindProperty("items").ClearArray();
        SerializedProperty shortcuts = serialized.FindProperty("hotbarItems");
        for (int i = 0; i < shortcuts.arraySize; i++) shortcuts.GetArrayElementAtIndex(i).objectReferenceValue = null;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        Call(inventory, "Awake");
        Call(inventory, "Start");
        inventory.SetOpen(true);
        Canvas.ForceUpdateCanvases();
        menu.Refresh();
        return inventory;
    }

    [Test]
    public void MenuBlocksGameplayAndReleasesItWhenDisabled()
    {
        Inventory inventory = NewInventory();
        GameObject playerObject = new("Player input test");
        playerObject.SetActive(false);
        created.Add(playerObject);
        PlayerInput input = playerObject.AddComponent<PlayerInput>();
        typeof(Inventory).GetField("playerInput", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(inventory, input);
        inventory.SetOpen(true);
        Assert.That(Get<bool>(input, "gameplayInputBlocked"), Is.True);
        Assert.That(input.MovementInput, Is.EqualTo(Vector3.zero));
        inventory.SetOpen(false);
        Assert.That(Get<bool>(input, "gameplayInputBlocked"), Is.False);
        inventory.SetOpen(true);
        Call(inventory, "OnDisable");
        Assert.That(Get<bool>(input, "gameplayInputBlocked"), Is.False);
        Assert.That(inventory.IsOpen, Is.False);
    }

    [Test]
    public void SceneRowsSelectionAndHoverUseTheExistingAssets()
    {
        Inventory inventory = OpenScene(out InventoryMenu menu, out _);
        Assert.That(inventory.container.GetComponentsInChildren<Button>(true), Does.Contain(Get<Button>(menu, "equipButton")));
        Assert.That(inventory.container.GetComponentsInChildren<Button>(true), Does.Contain(Get<Button>(menu, "dropButton")));
        Assert.That(Get<TextMeshProUGUI>(menu, "itemTitle").text, Is.Empty);
        Assert.That(Get<GameObject>(menu, "emptyMessage").activeSelf, Is.True);
        Assert.That(Get<Button>(menu, "equipButton").interactable, Is.False);
        inventory.AddItem(inventory.Resistor, 3);
        inventory.AddItem(inventory.Battery, 1);
        menu.Refresh();
        ScrollRect scroll = Get<ScrollRect>(menu, "scroll");
        Assert.That(scroll.horizontal, Is.False);
        Assert.That(scroll.viewport.GetComponent<RectMask2D>(), Is.Not.Null);
        Assert.That(scroll.content.GetComponent<GridLayoutGroup>(), Is.Null);
        List<Slot> rows = Get<List<Slot>>(menu, "rows");
        rows[1].OnPointerClick(new PointerEventData(null) { button = PointerEventData.InputButton.Left });
        Assert.That(inventory.SelectedItem, Is.SameAs(inventory.Battery));
        Assert.That(Get<TextMeshProUGUI>(menu, "itemTitle").text, Is.EqualTo(inventory.Battery.itemName));
        Assert.That(Get<TextMeshProUGUI>(menu, "itemCount").text, Does.Contain("1"));
        rows[0].OnPointerEnter(new PointerEventData(null));
        Assert.That(inventory.TryGetHoveredCircuitComponent(out ItemSO item, out CircuitElement element), Is.True);
        Assert.That(item, Is.SameAs(inventory.Resistor));
        Assert.That(element, Is.Not.Null);
        inventory.SetOpen(false);
        Assert.That(inventory.container.activeSelf, Is.False);
        Assert.That(inventory.TryGetHoveredCircuitComponent(out _, out _), Is.False);
    }

    [Test]
    public void UnlimitedVoxelShowsInfinityAndCannotBeDroppedFromTheMenu()
    {
        Inventory inventory = OpenScene(out InventoryMenu menu, out _);
        ItemSO voxel = NewItem("Dark Ship Metal");
        voxel.unlimited = true;
        GameObject prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        created.Add(prefab);
        voxel.itemPrefab = prefab;
        inventory.AddItem(voxel, 1);
        menu.Refresh();
        Assert.That(Get<TextMeshProUGUI>(menu, "itemCount").text, Is.EqualTo("Unlimited"));
        Assert.That(Get<Button>(menu, "dropButton").interactable, Is.False);
        Slot row = Get<List<Slot>>(menu, "rows")[0];
        Assert.That(row.transform.GetChild(1).GetComponent<TextMeshProUGUI>().text, Is.EqualTo("∞"));
        inventory.DropSelected();
        Assert.That(inventory.GetAmount(voxel), Is.EqualTo(1));
    }

    [Test]
    public void LongListReusesVisibleRowsAndClampsAfterRemoval()
    {
        Inventory inventory = OpenScene(out InventoryMenu menu, out _);
        for (int i = 0; i < 250; i++) inventory.AddItem(NewItem($"Item {i:000}"), 1);
        ScrollRect scroll = Get<ScrollRect>(menu, "scroll");
        List<Slot> rows = Get<List<Slot>>(menu, "rows");
        int poolSize = rows.Count;
        Assert.That(poolSize, Is.LessThan(30));
        Assert.That(scroll.content.rect.height, Is.EqualTo(250 * 44f));
        scroll.content.anchoredPosition = new Vector2(0, 100 * 44f);
        scroll.onValueChanged.Invoke(Vector2.zero);
        Assert.That(rows[0].GetItem(), Is.SameAs(inventory.Items[100].Item));
        Assert.That(rows.Count, Is.EqualTo(poolSize));
        inventory.SelectItem(inventory.Items[249].Item);
        menu.MoveSelection(1);
        Assert.That(inventory.SelectedItem, Is.SameAs(inventory.Items[249].Item));
        Assert.That(scroll.content.anchoredPosition.y, Is.GreaterThan(100 * 44f));
        Assert.That(rows.Any(r => r.gameObject.activeSelf && r.GetItem() == inventory.SelectedItem), Is.True);
        menu.MoveSelection(-1);
        Assert.That(inventory.SelectedItem, Is.SameAs(inventory.Items[248].Item));
        for (int i = 0; i < 249; i++) inventory.RemoveItem(inventory.Items[0].Item, 1);
        Assert.That(scroll.content.anchoredPosition.y, Is.EqualTo(0));
        Assert.That(rows.Count(r => r.gameObject.activeSelf), Is.EqualTo(1));
        Assert.That(rows[0].GetItem(), Is.SameAs(inventory.Items[0].Item));
        inventory.RemoveItem(inventory.Items[0].Item, 1);
        Assert.That(rows.Count(r => r.gameObject.activeSelf), Is.Zero);
        Assert.That(Get<GameObject>(menu, "emptyMessage").activeSelf, Is.True);
        Assert.That(Get<Button>(menu, "dropButton").interactable, Is.False);
    }

    [Test]
    public void MouseWheelAndDragScrollThroughRows()
    {
        Inventory inventory = OpenScene(out InventoryMenu menu, out Canvas canvas);
        for (int i = 0; i < 30; i++) inventory.AddItem(NewItem($"Item {i}"), 1);
        ScrollRect scroll = Get<ScrollRect>(menu, "scroll");
        Slot row = Get<List<Slot>>(menu, "rows")[0];
        Assert.That(ExecuteEvents.GetEventHandler<IScrollHandler>(row.gameObject), Is.SameAs(scroll.gameObject));
        Assert.That(ExecuteEvents.GetEventHandler<IDragHandler>(row.gameObject), Is.SameAs(scroll.gameObject));
        scroll.verticalNormalizedPosition = 1;
        PointerEventData pointer = new(null)
        {
            button = PointerEventData.InputButton.Left,
            scrollDelta = new Vector2(0, -2),
            position = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, scroll.viewport.position),
            pointerPressRaycast = new RaycastResult { module = canvas.GetComponent<GraphicRaycaster>() }
        };
        ExecuteEvents.ExecuteHierarchy(row.gameObject, pointer, ExecuteEvents.scrollHandler);
        Assert.That(scroll.content.anchoredPosition.y, Is.GreaterThan(0));
        float beforeDrag = scroll.content.anchoredPosition.y;
        scroll.OnBeginDrag(pointer);
        pointer.position += new Vector2(0, 60);
        scroll.OnDrag(pointer);
        scroll.OnEndDrag(pointer);
        Assert.That(scroll.content.anchoredPosition.y, Is.GreaterThan(beforeDrag));
        Call(scroll, "LateUpdate");
        List<Slot> rows = Get<List<Slot>>(menu, "rows");
        int first = Mathf.FloorToInt(scroll.content.anchoredPosition.y / 44);
        Assert.That(rows[0].GetItem(), Is.SameAs(inventory.Items[first].Item));
    }

    [Test]
    public void DroppedItemRemainsPickableAndQuantityIsConserved()
    {
        Inventory inventory = NewInventory();
        GameObject cameraObject = new("Drop camera");
        created.Add(cameraObject);
        cameraObject.tag = "MainCamera";
        cameraObject.AddComponent<Camera>();
        ItemSO item = NewItem();
        GameObject prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        created.Add(prefab);
        prefab.name = "Drop test item";
        item.itemPrefab = prefab;
        inventory.AddItem(item, 5);
        Assert.That(inventory.DropItem(item, 2), Is.True);
        Item pickup = Object.FindObjectsByType<Item>(FindObjectsSortMode.None).First(p => p.item == item);
        created.Add(pickup.gameObject);
        Assert.That(pickup.amount, Is.EqualTo(2));
        Assert.That(inventory.GetAmount(item), Is.EqualTo(3));
        Assert.That(pickup.GetComponent<Rigidbody>().isKinematic, Is.False);
        Assert.That(pickup.GetComponent<Collider>().bounds.size.x, Is.LessThanOrEqualTo(0.5f));
        inventory.AddItem(pickup.item, pickup.amount);
        Assert.That(inventory.GetAmount(item), Is.EqualTo(5));
    }

    internal static T Get<T>(object target, string field) =>
        (T)target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static void Call(object target, string method) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
}
