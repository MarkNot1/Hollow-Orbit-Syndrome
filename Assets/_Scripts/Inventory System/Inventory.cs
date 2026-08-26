using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class Inventory : MonoBehaviour
{
    [Header("Input Actions")]
    [SerializeField]
    private InputActionAsset inputActions;

    public GameObject hotbarObj;
    public GameObject inventorySlotParent;
    public GameObject container;


    public ItemSO Resistor;
    public ItemSO Battery;

    public Image dragIcon;
    public Material highlightMaterial;

    private Material originalMaterial;
    private Renderer lookedAtRenderer;


    private int equippedHotbarIndex = 0;
    public float equippedOpacity = 0.9f;
    public float normalOpacity = 0.5f;

    public Transform hand;
    private GameObject currentHandItem;

    [Header("Dropped Items")]
    [SerializeField, Min(0f)] private float dropForwardSpeed = 3f;
    [SerializeField, Min(0f)] private float dropUpwardSpeed = 1f;
    private const float MaxDroppedItemSize = 0.5f;

    private List<Slot> inventorySlots = new List<Slot>();
    private List<Slot> hotbarSlots = new List<Slot>();
    private List<Slot> allSlots = new List<Slot>();

    private Slot draggedSlot = null;
    private bool isDragging = false;
    private InputActionMap inventoryMap;
    private InputAction addResistorAction;
    private InputAction addBatteryAction;
    private InputAction primaryClickAction;
    private InputAction pointerPositionAction;
    private InputAction toggleAction;
    private PlayerInput playerInput;
    private PlayerInput subscribedPlayerInput;
    private bool isOpen;

    private void Awake()
    {
        inventorySlots.AddRange(inventorySlotParent.GetComponentsInChildren<Slot>());
        hotbarSlots.AddRange(hotbarObj.GetComponentsInChildren<Slot>());

        allSlots.AddRange(inventorySlots);
        allSlots.AddRange(hotbarSlots);

        // The drag icon must not intercept UI raycasts, otherwise it prevents
        // destination slots from receiving pointer enter/exit events.
        if (dragIcon != null)
            dragIcon.raycastTarget = false;

        if (inputActions == null)
        {
            Debug.LogError("Inventory: Assign the InputSystem_Actions asset in the Inspector.", this);
            return;
        }

        inventoryMap = inputActions.FindActionMap("Inventory");
        if (inventoryMap == null)
        {
            Debug.LogError("Inventory: InputActionAsset has no 'Inventory' action map.", this);
            return;
        }

        addResistorAction = inventoryMap.FindAction("AddResistor");
        addBatteryAction = inventoryMap.FindAction("AddBattery");
        primaryClickAction = inventoryMap.FindAction("PrimaryClick");
        pointerPositionAction = inventoryMap.FindAction("PointerPosition");
        toggleAction = inventoryMap.FindAction("ToggleInventory");
        playerInput = FindFirstObjectByType<PlayerInput>();
    }

    private void OnEnable()
    {
        inventoryMap?.Enable();
        BindPlayerInput();
    }

    private void OnDisable()
    {
        inventoryMap?.Disable();
        if (subscribedPlayerInput != null)
            subscribedPlayerInput.OnInteract -= PickUp;
        subscribedPlayerInput = null;
        SetLookedAtRenderer(null);
    }

    private void Start()
    {
        SetOpen(false);
        UpdateHotbarOpacity();
    }

    // Update is called once per frame
    void Update()
    {
        BindPlayerInput();

        if (toggleAction != null && toggleAction.WasPressedThisFrame())
            SetOpen(!isOpen);

        DetectLookedAtItem();

        if (!isOpen)
        {
            HandleHotbarSelection();
            HandleDropEquippedItem();
            return;
        }

        if (addResistorAction != null && addResistorAction.WasPressedThisFrame())
        {
            AddItem(Resistor, 3);
        }      

        if (addBatteryAction != null && addBatteryAction.WasPressedThisFrame())
        {
            AddItem(Battery, 1);
        }

        DetectLookedAtItem();
        PickUp();

        StartDrag();
        UpdateDragItemPossition();
        EndDrag();

        HandleHotbarSelection();
        HandleDropEquippedItem();
        UpdateHotbarOpacity();
    }

    private void BindPlayerInput()
    {
        if (playerInput == null)
            playerInput = FindFirstObjectByType<PlayerInput>();

        if (playerInput == null || subscribedPlayerInput == playerInput)
            return;

        if (subscribedPlayerInput != null)
            subscribedPlayerInput.OnInteract -= PickUp;

        subscribedPlayerInput = playerInput;
        subscribedPlayerInput.OnInteract += PickUp;
        subscribedPlayerInput.SetGameplayInputBlocked(isOpen);
    }

    private void SetOpen(bool open)
    {
        isOpen = open;
        container?.SetActive(open);
        playerInput?.SetGameplayInputBlocked(open);
        Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = open;
    }

    public void AddItem(ItemSO itemToAdd, int amount)
    {
        int remainingAmount = amount;

        foreach(Slot slot in allSlots)
        {
            if (slot.hasItem() && slot.GetItem() == itemToAdd)
            {
                int currentAmount = slot.GetItemAmount();
                int maxStack = itemToAdd.maxStackSize;

                if(currentAmount < maxStack)
                {
                    int spaceLeft = maxStack - currentAmount;
                    int amountToAdd = Mathf.Min(spaceLeft, remainingAmount);

                    slot.SetItem(itemToAdd, currentAmount + amountToAdd);
                    remainingAmount -= amountToAdd;

                    if(remainingAmount <= 0)
                    {
                        return;
                    }
                }
            }
        }

        foreach(Slot slot in allSlots)
        {
            if(!slot.hasItem())
            {
                int amountToAdd = Mathf.Min(itemToAdd.maxStackSize, remainingAmount);
                slot.SetItem(itemToAdd, amountToAdd);
                remainingAmount -= amountToAdd;

                if(remainingAmount <= 0)
                {
                    return;
                }
            }
        } 

        if(remainingAmount > 0)
        {   
            Debug.Log("Inventory is full, could not add " + remainingAmount + " of " + itemToAdd.itemName);
        }
    }


    private void StartDrag()
    {
        if (primaryClickAction != null && primaryClickAction.WasPressedThisFrame())
        {
            Slot hovered = GetHoveredSlot();

            if(hovered != null && hovered.hasItem())
            {
                draggedSlot = hovered;
                isDragging = true;

                dragIcon.sprite = hovered.GetItem().itemIcon;
                dragIcon.color = new Color(1,1,1, 0.5f);
                dragIcon.enabled = true;
            }
        }
    }

    private void EndDrag()
    {
        if (primaryClickAction != null && primaryClickAction.WasReleasedThisFrame() && isDragging)
        {
            Slot hovered = GetHoveredSlot();

            if(hovered != null)
            {
                HandleDrop(draggedSlot, hovered);
            }

            draggedSlot = null;
            isDragging = false;
            dragIcon.enabled = false;
        }
    }

    private Slot GetHoveredSlot()
    {
        foreach(Slot slot in allSlots)
        {
            if(slot.hovering)
            {
                return slot;
            }
        }
        return null;
    }

    private void HandleDrop(Slot from, Slot to)
    {
        if(from == to) return;

        //Stacking
        if(to.hasItem() && from.GetItem() == to.GetItem())
        {
            
            int maxStack = to.GetItem().maxStackSize;
            int space = maxStack - to.GetItemAmount();

            if(space > 0)
            {
                int move = Mathf.Min(space, from.GetItemAmount());

                to.SetItem(to.GetItem(), to.GetItemAmount() + move);
                from.SetItem(from.GetItem(), from.GetItemAmount() - move);
                
                
                if(from.GetItemAmount() <= 0)
                {
                    from.ClearSlot();
                }
                return;
            }
        }
        

        //Different Item
        if(to.hasItem()){
            
            ItemSO tempItem = to.GetItem();
            int tempAmount = to.GetItemAmount();

            to.SetItem(from.GetItem(), from.GetItemAmount());
            from.SetItem(tempItem, tempAmount);
            return;
        }


        //Empty slot
        to.SetItem(from.GetItem(), from.GetItemAmount());
        from.ClearSlot();
    }

    private void UpdateDragItemPossition()
    {
        if(isDragging)
        {
            if (pointerPositionAction != null)
                dragIcon.transform.position = pointerPositionAction.ReadValue<Vector2>();
        }
    }

    private void PickUp()
    {
        if (playerInput == null || !playerInput.TryGetReticleTarget(out Item item))
            return;

        AddItem(item.item, item.amount);
        SetLookedAtRenderer(null);
        Destroy(item.gameObject);
        EquipItemInHand();
    }

    private void DetectLookedAtItem()
    {
        Renderer rend = playerInput != null && playerInput.TryGetReticleTarget(out Item item)
            ? item.GetComponentInChildren<Renderer>()
            : null;
        SetLookedAtRenderer(rend);
    }

    private void SetLookedAtRenderer(Renderer rend)
    {
        if (rend == lookedAtRenderer)
            return;
        if (lookedAtRenderer != null)
            lookedAtRenderer.material = originalMaterial;

        lookedAtRenderer = null;
        if (rend == null || highlightMaterial == null)
            return;

        originalMaterial = rend.material;
        rend.material = highlightMaterial;
        lookedAtRenderer = rend;
    }


    private void UpdateHotbarOpacity()
    {
        for(int i = 0; i < hotbarSlots.Count; i++)
        {
            Image slotImage = hotbarSlots[i].GetComponent<Image>();
            if(slotImage != null)
            {
                Color color = slotImage.color;
                color.a = (i == equippedHotbarIndex) ? equippedOpacity : normalOpacity;
                slotImage.color = color;
            }
        }
    }

    private void HandleHotbarSelection()
    {
        if (Keyboard.current == null)
            return;

        Key[] numberKeys =
        {
            Key.Digit1,
            Key.Digit2,
            Key.Digit3,
            Key.Digit4,
            Key.Digit5,
            Key.Digit6
        };

        for (int i = 0; i < numberKeys.Length; i++)
        {
            if (Keyboard.current[numberKeys[i]].wasPressedThisFrame)
            {
                equippedHotbarIndex = i;
                UpdateHotbarOpacity();
                EquipItemInHand();
                break;
            }
        }
    }

    private void HandleDropEquippedItem()
    {
        if (Keyboard.current == null || playerInput == null || hotbarSlots.Count == 0)
            return;

        if (Keyboard.current.qKey.wasPressedThisFrame)
        {
            equippedHotbarIndex = Mathf.Clamp(equippedHotbarIndex, 0, hotbarSlots.Count - 1);
            Slot equippedSlot = hotbarSlots[equippedHotbarIndex];
            if (equippedSlot.hasItem())
            {
                // Drop the item in the game world
                ItemSO itemToDrop = equippedSlot.GetItem();
                int amountToDrop = equippedSlot.GetItemAmount();

                Transform dropOrigin = Camera.main != null ? Camera.main.transform : playerInput.transform;
                Vector3 dropDirection = dropOrigin.forward;
                Vector3 spawnPosition = dropOrigin.position + dropDirection * 0.8f;

                // Instantiate the item prefab in front of the player.
                GameObject droppedItem = Instantiate(itemToDrop.itemPrefab, spawnPosition, Quaternion.identity);
                FitDroppedItemWithinBounds(droppedItem);
                ConfigureDroppedItemPhysics(droppedItem, dropDirection);
                Item droppedItemComponent = droppedItem.GetComponent<Item>();
                if (droppedItemComponent != null)
                {
                    droppedItemComponent.item = itemToDrop;
                    droppedItemComponent.amount = amountToDrop;
                }

                // Clear the slot
                equippedSlot.ClearSlot();

                EquipItemInHand();
            }
        }
    }

    private static void FitDroppedItemWithinBounds(GameObject droppedItem)
    {
        Collider[] colliders = droppedItem.GetComponentsInChildren<Collider>();
        if (colliders.Length == 0)
            return;

        Bounds bounds = colliders[0].bounds;
        for (int i = 1; i < colliders.Length; i++)
            bounds.Encapsulate(colliders[i].bounds);

        Vector3 size = bounds.size;
        float largestDimension = Mathf.Max(size.x, size.y, size.z);
        if (largestDimension <= MaxDroppedItemSize || largestDimension <= Mathf.Epsilon)
            return;

        float scaleFactor = MaxDroppedItemSize / largestDimension;
        droppedItem.transform.localScale *= scaleFactor;
    }

    private void ConfigureDroppedItemPhysics(GameObject droppedItem, Vector3 dropDirection)
    {
        Rigidbody body = droppedItem.GetComponent<Rigidbody>();
        if (body == null)
            body = droppedItem.AddComponent<Rigidbody>();

        body.isKinematic = false;
        body.useGravity = true;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.linearVelocity = dropDirection.normalized * dropForwardSpeed + Vector3.up * dropUpwardSpeed;
        body.angularVelocity = Random.insideUnitSphere * 2f;
    }

    private void EquipItemInHand()
    {
        if (currentHandItem != null)
            Destroy(currentHandItem);

        Slot equippedSlot = hotbarSlots[equippedHotbarIndex];
        if (!equippedSlot.hasItem())
            return;

        ItemSO item = equippedSlot.GetItem();

        if (item.handItemPrefab == null)
            return;

        currentHandItem = Instantiate(item.handItemPrefab, hand);
        currentHandItem.transform.localPosition = Vector3.zero;
        currentHandItem.transform.localRotation = Quaternion.identity;
    }
}
