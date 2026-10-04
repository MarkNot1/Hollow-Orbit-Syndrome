using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class Inventory : MonoBehaviour
{
    [Serializable]
    public sealed class Entry
    {
        [SerializeField] private ItemSO item;
        [SerializeField, Min(1)] private int amount;
        public ItemSO Item => item;
        public int Amount => amount;
        public Entry(ItemSO item, int amount) { this.item = item; this.amount = amount; }
        internal void Add(int value) => amount += value;
    }

    [Header("Inventory data (one entry per item type)")]
    [SerializeField] private List<Entry> items = new();
    [SerializeField] private ItemSO[] hotbarItems = new ItemSO[6];
    [Header("Existing scene references")]
    [SerializeField] private InputActionAsset inputActions;
    public GameObject hotbarObj;
    public GameObject container;
    [SerializeField] private InventoryMenu menu;
    public ItemSO Resistor;
    public ItemSO Battery;
    public Material highlightMaterial;
    public float equippedOpacity = 0.9f;
    public float normalOpacity = 0.5f;
    public Transform hand;
    [Header("Dropped Items")]
    [SerializeField, Min(0f)] private float dropForwardSpeed = 3f;
    [SerializeField, Min(0f)] private float dropUpwardSpeed = 1f;
    private const float MaxDroppedItemSize = 0.5f;

    private Slot[] hotbarSlots = Array.Empty<Slot>();
    private InputActionMap inventoryMap;
    private InputAction toggleAction, addResistorAction, addBatteryAction;
    private PlayerInput playerInput;
    private int equippedHotbarIndex;
    private ItemSO selectedItem, handItem;
    private GameObject currentHandItem;
    private Renderer lookedAtRenderer;
    private Material originalMaterial;
    private bool isOpen;

    public bool IsOpen => isOpen;
    public IReadOnlyList<Entry> Items => items;
    public ItemSO SelectedItem => selectedItem;
    public ItemSO EquippedItem => equippedHotbarIndex < hotbarItems.Length ? hotbarItems[equippedHotbarIndex] : null;

    private void Awake()
    {
        if (hotbarObj != null)
            hotbarSlots = hotbarObj.GetComponentsInChildren<Slot>(true);
        if (hotbarItems == null || hotbarItems.Length != 6)
            Array.Resize(ref hotbarItems, 6);
        inventoryMap = inputActions != null ? inputActions.FindActionMap("Inventory") : null;
        if (inventoryMap == null)
            Debug.LogError("Inventory: Assign an InputActionAsset with the Inventory action map.", this);
        toggleAction = inventoryMap?.FindAction("ToggleInventory");
        addResistorAction = inventoryMap?.FindAction("AddResistor");
        addBatteryAction = inventoryMap?.FindAction("AddBattery");
    }

    private void OnEnable() => inventoryMap?.Enable();

    private void OnDisable()
    {
        inventoryMap?.Disable();
        if (playerInput != null) playerInput.OnInteract -= PickUp;
        playerInput?.SetGameplayInputBlocked(false);
        playerInput = null;
        if (isOpen)
        {
            isOpen = false;
            container?.SetActive(false);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        SetLookedAtRenderer(null);
    }

    private void Start()
    {
        menu?.Initialize(this);
        if (items.Count > 0) selectedItem = items[0].Item;
        SetOpen(false);
        Refresh();
    }

    private void Update()
    {
        // GameManager spawns the player after the world is ready.
        if (playerInput == null)
        {
            playerInput = FindFirstObjectByType<PlayerInput>();
            if (playerInput != null)
            {
                playerInput.OnInteract += PickUp;
                playerInput.SetGameplayInputBlocked(isOpen);
                if (isOpen)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
            }
        }
        if (toggleAction != null && toggleAction.WasPressedThisFrame()) SetOpen(!isOpen);
        else if (isOpen && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) SetOpen(false);
        DetectLookedAtItem();
        if (isOpen)
        {
            if (addResistorAction != null && addResistorAction.WasPressedThisFrame()) AddItem(Resistor, 3);
            if (addBatteryAction != null && addBatteryAction.WasPressedThisFrame()) AddItem(Battery, 1);
        }
        HandleKeyboard();
    }

    public void SetOpen(bool open)
    {
        isOpen = open;
        container?.SetActive(open);
        playerInput?.SetGameplayInputBlocked(open);
        Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = open;
        if (open) menu?.ShowTab(0);
        else SetLookedAtRenderer(null);
    }

    public int GetAmount(ItemSO item)
    {
        int index = FindItem(item);
        return index >= 0 ? items[index].Amount : 0;
    }

    private int FindItem(ItemSO item)
    {
        if (item == null) return -1;
        for (int i = 0; i < items.Count; i++)
            if (items[i].Item == item) return i;
        return -1;
    }

    public void AddItem(ItemSO itemToAdd, int amount)
    {
        if (itemToAdd == null || amount <= 0) return;
        int index = FindItem(itemToAdd);
        if (index >= 0)
        {
            if (amount > int.MaxValue - items[index].Amount) return;
            items[index].Add(amount);
        }
        else
        {
            items.Add(new Entry(itemToAdd, amount));
            // Shortcuts reference the inventory; they never own another stack.
            for (int i = 0; i < hotbarItems.Length; i++)
                if (hotbarItems[i] == null) { hotbarItems[i] = itemToAdd; break; }
        }
        if (selectedItem == null) selectedItem = itemToAdd;
        Refresh();
    }

    public bool RemoveItem(ItemSO item, int amount)
    {
        int index = FindItem(item);
        if (amount <= 0 || index < 0 || items[index].Amount < amount) return false;
        items[index].Add(-amount);
        if (items[index].Amount == 0)
        {
            items.RemoveAt(index);
            for (int i = 0; i < hotbarItems.Length; i++)
                if (hotbarItems[i] == item) hotbarItems[i] = null;
            if (selectedItem == item)
                selectedItem = items.Count > 0 ? items[Mathf.Min(index, items.Count - 1)].Item : null;
        }
        Refresh();
        return true;
    }

    public void SelectItem(ItemSO item)
    {
        if (FindItem(item) < 0) return;
        selectedItem = item;
        if (isOpen) menu?.Refresh();
    }

    public void SelectHotbar(int index)
    {
        if (index < 0 || index >= hotbarItems.Length) return;
        equippedHotbarIndex = index;
        Refresh();
    }

    public void AssignSelectedToHotbar(int index)
    {
        if (selectedItem == null || index < 0 || index >= hotbarItems.Length) return;
        hotbarItems[index] = selectedItem;
        SelectHotbar(index);
    }

    public void EquipSelected() => AssignSelectedToHotbar(equippedHotbarIndex);
    public void DropSelected() => DropItem(selectedItem, 1);

    private void HandleKeyboard()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (isOpen && menu != null && !menu.InventoryTabSelected) return;
        // Digit keys are contiguous in the Input System's Key enum.
        for (int i = 0; i < hotbarItems.Length; i++)
            if (keyboard[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame)
            {
                if (isOpen) AssignSelectedToHotbar(i);
                else SelectHotbar(i);
                break;
            }
        if (keyboard.qKey.wasPressedThisFrame)
        {
            if (isOpen) DropSelected();
            else DropItem(EquippedItem, GetAmount(EquippedItem));
        }
        if (!isOpen) return;
        if (keyboard.enterKey.wasPressedThisFrame) EquipSelected();
        if (keyboard.upArrowKey.wasPressedThisFrame) menu?.MoveSelection(-1);
        if (keyboard.downArrowKey.wasPressedThisFrame) menu?.MoveSelection(1);
    }

    private void Refresh()
    {
        for (int i = 0; i < hotbarSlots.Length; i++)
        {
            hotbarSlots[i].Bind(this, hotbarItems[i], GetAmount(hotbarItems[i]), i);
            hotbarSlots[i].SetSelected(i == equippedHotbarIndex, equippedOpacity, normalOpacity);
        }
        EquipItemInHand();
        if (isOpen) menu?.Refresh();
    }

    public bool TryGetHoveredCircuitComponent(out ItemSO item, out CircuitElement element)
    {
        item = null;
        element = null;
        if (!isOpen) return false;
        item = menu != null ? menu.HoveredItem : null;
        if (item == null)
            foreach (Slot slot in hotbarSlots)
                if (slot.hovering) { item = slot.GetItem(); break; }
        if (item != null && item.itemPrefab != null)
            element = item.itemPrefab.GetComponentInChildren<CircuitElement>(true);
        if (element == null) item = null;
        return element != null;
    }

    private void PickUp()
    {
        if (isOpen || playerInput == null || !playerInput.TryGetReticleTarget(out Item item) ||
            item.item == null || item.amount <= 0 || item.amount > int.MaxValue - GetAmount(item.item)) return;
        AddItem(item.item, item.amount);
        SetLookedAtRenderer(null);
        Destroy(item.gameObject);
    }

    private void DetectLookedAtItem()
    {
        Renderer renderer = !isOpen && playerInput != null && playerInput.TryGetReticleTarget(out Item item)
            ? item.GetComponentInChildren<Renderer>() : null;
        SetLookedAtRenderer(renderer);
    }

    private void SetLookedAtRenderer(Renderer renderer)
    {
        if (renderer == lookedAtRenderer) return;
        if (lookedAtRenderer != null) lookedAtRenderer.sharedMaterial = originalMaterial;
        lookedAtRenderer = null;
        if (renderer == null || highlightMaterial == null) return;
        originalMaterial = renderer.sharedMaterial;
        renderer.sharedMaterial = highlightMaterial;
        lookedAtRenderer = renderer;
    }

    public bool DropItem(ItemSO item, int amount)
    {
        if (item == null || item.itemPrefab == null || amount <= 0 || GetAmount(item) < amount) return false;
        Transform origin = Camera.main != null ? Camera.main.transform : playerInput != null ? playerInput.transform : null;
        if (origin == null) return false;
        GameObject dropped = Instantiate(item.itemPrefab, origin.position + origin.forward * 0.8f, Quaternion.identity);
        FitDroppedItemWithinBounds(dropped);
        Rigidbody body = dropped.GetComponent<Rigidbody>();
        if (body == null) body = dropped.AddComponent<Rigidbody>();
        body.isKinematic = false;
        body.useGravity = true;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.linearVelocity = origin.forward * dropForwardSpeed + Vector3.up * dropUpwardSpeed;
        body.angularVelocity = UnityEngine.Random.insideUnitSphere * 2f;
        Item pickup = dropped.GetComponent<Item>();
        if (pickup == null) pickup = dropped.AddComponent<Item>();
        pickup.item = item;
        pickup.amount = amount;
        RemoveItem(item, amount);
        return true;
    }

    private static void FitDroppedItemWithinBounds(GameObject droppedItem)
    {
        Collider[] colliders = droppedItem.GetComponentsInChildren<Collider>();
        if (colliders.Length == 0) return;
        Bounds bounds = colliders[0].bounds;
        for (int i = 1; i < colliders.Length; i++) bounds.Encapsulate(colliders[i].bounds);
        float largestDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (largestDimension > MaxDroppedItemSize)
            droppedItem.transform.localScale *= MaxDroppedItemSize / largestDimension;
    }

    private void EquipItemInHand()
    {
        ItemSO item = EquippedItem;
        if (item == handItem && (item == null || currentHandItem != null || item.handItemPrefab == null)) return;
        if (currentHandItem != null) Destroy(currentHandItem);
        currentHandItem = null;
        handItem = item;
        if (item == null || item.handItemPrefab == null || hand == null) return;
        currentHandItem = Instantiate(item.handItemPrefab, hand);
        currentHandItem.transform.localPosition = Vector3.zero;
        currentHandItem.transform.localRotation = Quaternion.identity;
    }
}
