using System.Collections.Generic;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class InventoryMenu : MonoBehaviour
{
    [SerializeField] private ScrollRect scroll;
    [SerializeField] private Slot rowPrefab;
    [SerializeField] private TextMeshProUGUI itemTitle, itemDescription, itemCount;
    [SerializeField] private Image itemIcon;
    [SerializeField] private GameObject emptyMessage;
    [SerializeField] private Button equipButton, dropButton;
    private const float RowHeight = 44f;
    private readonly List<Slot> rows = new();
    private Inventory inventory;
    private int firstVisible = -1;
    private StructureSystem structures;
    private readonly List<StructureData> ships = new();
    private int selectedShip, builtInCount;
    private VoxelStructure shipToSave;
    private string saveToRename, deleteConfirmation, shipMessage;
    private Button itemsTab, devTab, renameButton, deleteButton;
    private TMP_InputField shipName;
    private TMP_Text controls;
    private string itemControls;
    public bool IsDevTab { get; private set; }
    public bool IsEditingShip => shipName != null && shipName.gameObject.activeSelf;
    public bool IsPlacingShip => structures != null && structures.SelectedShip != null;
    private int RowCount => IsDevTab ? ships.Count : inventory.Items.Count;
    public ItemSO HoveredItem
    {
        get
        {
            foreach (Slot row in rows)
                if (row.gameObject.activeInHierarchy && row.hovering) return row.GetItem();
            return null;
        }
    }

    public void Initialize(Inventory owner)
    {
        if (inventory != null) return;
        inventory = owner;
        structures = FindFirstObjectByType<StructureSystem>();
        controls = scroll.transform.parent.parent.Find("Controls")?.GetComponent<TMP_Text>();
        itemControls = controls != null ? controls.text : "";
        equipButton.onClick.AddListener(ActivateSelected);
        dropButton.onClick.AddListener(() =>
        {
            if (IsEditingShip) { Closed(); Refresh(); }
            else if (IsDevTab) SaveDevShips();
            else inventory.DropSelected();
        });
        scroll.onValueChanged.AddListener(_ => RefreshRows(false));
        itemsTab = CreateTab("ITEMS", -170, false);
        devTab = CreateTab("SHIP LIBRARY", -18, true);
        renameButton = CreateShipButton(equipButton, "RENAME SAVE", RenameSelected);
        deleteButton = CreateShipButton(dropButton, "DELETE SAVE", DeleteSelected);
    }

    private Button CreateTab(string label, float x, bool dev)
    {
        Button button = Instantiate(equipButton, scroll.transform.parent.parent);
        button.name = label;
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(() => ShowDevTab(dev));
        button.GetComponentInChildren<TMP_Text>().text = label;
        RectTransform rect = (RectTransform)button.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
        rect.anchoredPosition = new Vector2(x, -16);
        rect.sizeDelta = new Vector2(140, 30);
        return button;
    }

    public void ShowDevTab(bool dev)
    {
        Closed();
        IsDevTab = dev;
        CancelShipPlacement();
        scroll.StopMovement();
        scroll.content.anchoredPosition = Vector2.zero;
        if (dev) ReloadShips();
        Refresh();
    }

    public void Opened()
    {
        CancelShipPlacement();
        if (IsDevTab) ReloadShips();
    }

    public void Closed()
    {
        shipToSave = null;
        saveToRename = deleteConfirmation = shipMessage = null;
        if (shipName != null) shipName.gameObject.SetActive(false);
    }

    private void ReloadShips(string localId = null)
    {
        string id = localId ?? (selectedShip < ships.Count ? ships[selectedShip].id : null);
        bool preferLocal = localId != null || selectedShip >= builtInCount;
        shipMessage = null;
        ships.Clear();
        builtInCount = selectedShip = 0;
        if (structures == null) structures = FindFirstObjectByType<StructureSystem>();
        if (structures == null) return;
        try
        {
            ships.AddRange(structures.ReadBuiltInShips());
            builtInCount = ships.Count;
        }
        catch (Exception error) { shipMessage = "Cannot load built-in ships. " + error.Message; }
        try { ships.AddRange(structures.ReadSavedShips()); }
        catch (Exception error) { shipMessage = "Cannot load local saves. " + error.Message; }
        int start = preferLocal ? builtInCount : 0, end = preferLocal ? ships.Count : builtInCount;
        for (int i = start; i < end; i++)
            if (ships[i].id == id) { selectedShip = i; break; }
    }

    public void CancelShipPlacement() { if (structures != null) structures.SelectSavedShip(null); }

    public void ActivateSelected()
    {
        if (!IsDevTab) { inventory.EquipSelected(); return; }
        if (IsEditingShip) { SubmitShipName(); return; }
        if (structures == null || ships.Count == 0) return;
        structures.SelectSavedShip(ships[selectedShip]);
        inventory.SetOpen(false);
    }

    public void SaveDevShips()
    {
        if (structures == null || IsEditingShip) return;
        deleteConfirmation = null;
        try { structures.SaveToFile(structures.SavePath); ReloadShips(); shipMessage ??= "World ships saved. F9 to load."; }
        catch (Exception error) { shipMessage = "World save failed. " + error.Message; }
        Refresh();
    }

    public void BeginShipSave(VoxelStructure ship)
    {
        ShowDevTab(true);
        shipToSave = ship;
        int index = ships.FindIndex(builtInCount, entry => entry.id == ship.Id);
        EditShipName(index < 0 ? ship.name : ships[index].name);
    }

    private void RenameSelected()
    {
        if (selectedShip < builtInCount || selectedShip >= ships.Count) return;
        saveToRename = ships[selectedShip].id;
        EditShipName(ships[selectedShip].name);
    }

    private void EditShipName(string name)
    {
        deleteConfirmation = shipMessage = null;
        if (shipName == null)
        {
            GameObject field = TMP_DefaultControls.CreateInputField(default);
            field.name = "Ship name";
            RectTransform rect = (RectTransform)field.transform;
            rect.SetParent(itemDescription.transform.parent, false);
            rect.anchorMin = rect.pivot = Vector2.zero;
            rect.anchorMax = Vector2.right;
            rect.anchoredPosition = Vector2.up * 42f;
            rect.sizeDelta = new Vector2(0f, 34f);
            shipName = field.GetComponent<TMP_InputField>();
            Image background = field.GetComponent<Image>();
            background.color = new Color(.06f, .14f, .1f);
            shipName.targetGraphic = background;
            shipName.characterLimit = StructureSystem.MaxShipNameLength;
            shipName.fontAsset = itemDescription.font;
            shipName.richText = false;
            shipName.textComponent.color = itemDescription.color;
            ((TMP_Text)shipName.placeholder).text = "Ship name";
        }
        shipName.gameObject.SetActive(true);
        shipName.text = name;
        Refresh();
        shipName.Select();
        shipName.ActivateInputField();
    }

    private void SubmitShipName()
    {
        string id = saveToRename ?? (shipToSave != null ? shipToSave.Id : null);
        try
        {
            if (saveToRename == null) structures.SaveShip(shipToSave, shipName.text);
            else if (!structures.RenameSavedShip(id, shipName.text)) throw new InvalidOperationException("This save is no longer available.");
            Closed();
            ReloadShips(id);
            shipMessage ??= "Ship saved locally.";
        }
        catch (Exception error) { shipMessage = error.Message; }
        Refresh();
    }

    private void DeleteSelected()
    {
        if (selectedShip < builtInCount || selectedShip >= ships.Count) return;
        string id = ships[selectedShip].id;
        if (deleteConfirmation != id)
        {
            deleteConfirmation = id;
            shipMessage = "Confirm deletion of this local save. Placed ships stay in the world.";
        }
        else
        {
            deleteConfirmation = null;
            try
            {
                bool deleted = structures.DeleteSavedShip(id);
                ReloadShips();
                shipMessage ??= deleted ? "Local save deleted." : "This save is already gone.";
            }
            catch (Exception error) { shipMessage = "Delete failed. " + error.Message; }
        }
        Refresh();
    }

    private static Button CreateShipButton(Button source, string label, UnityEngine.Events.UnityAction onClick)
    {
        Button button = Instantiate(source, source.transform.parent);
        button.name = label;
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(onClick);
        button.GetComponentInChildren<TMP_Text>().text = label;
        ((RectTransform)button.transform).anchoredPosition += Vector2.up * 42f;
        button.gameObject.SetActive(false);
        return button;
    }

    public void Refresh()
    {
        if (inventory == null || itemsTab == null || devTab == null) return;
        scroll.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, RowCount * RowHeight);
        float maximum = Mathf.Max(0f, scroll.content.rect.height - scroll.viewport.rect.height);
        Vector2 position = scroll.content.anchoredPosition;
        position.y = Mathf.Clamp(position.y, 0f, maximum);
        scroll.content.anchoredPosition = position;
        RefreshRows(true);
        itemsTab.interactable = devTab.interactable = true;
        Color selected = new(.2f, .5f, .32f, 1), idle = new(.08f, .18f, .12f, 1);
        itemsTab.image.color = IsDevTab ? idle : selected;
        devTab.image.color = IsDevTab ? selected : idle;
        bool editing = IsEditingShip;
        if (controls != null) controls.text = IsDevTab
            ? editing ? "ENTER Save name   TAB / ESC Close" : "TAB / ESC Close   UP/DOWN Select   ENTER Preview\nAim at a ship and press F5 to save it. F9 loads world ships."
            : itemControls;
        equipButton.GetComponentInChildren<TMP_Text>().text = !IsDevTab ? "EQUIP" : !editing ? "PLACE COPY" : saveToRename == null ? "SAVE SHIP" : "RENAME";
        dropButton.GetComponentInChildren<TMP_Text>().text = !IsDevTab ? "DROP" : editing ? "CANCEL" : "SAVE WORLD";
        bool local = IsDevTab && !editing && selectedShip >= builtInCount && selectedShip < ships.Count;
        renameButton.gameObject.SetActive(local);
        deleteButton.gameObject.SetActive(local);
        if (local) deleteButton.GetComponentInChildren<TMP_Text>().text = deleteConfirmation == null ? "DELETE SAVE" : "CONFIRM DELETE";
        itemTitle.richText = itemDescription.richText = !IsDevTab;
        if (IsDevTab)
        {
            StructureData ship = ships.Count > 0 ? ships[selectedShip] : null;
            itemTitle.text = editing ? "Ship name" : ship?.name ?? "Ship library";
            itemCount.text = ship == null || editing ? "" : $"{(local ? "MY SAVE" : "BUILT-IN")}  •  {ship.voxels.Count} voxels";
            itemDescription.text = shipMessage ?? (editing ? "Enter a name for this local save." : ship == null
                ? "Aim at a master-frame ship and press F5 to save it."
                : "Place a copy with right click. Press Esc to cancel.");
            itemIcon.enabled = false;
            emptyMessage.SetActive(false);
            equipButton.interactable = editing || ship != null;
            dropButton.interactable = editing || structures != null;
            return;
        }
        ItemSO item = inventory.SelectedItem;
        itemTitle.text = item != null ? item.itemName : "";
        itemDescription.text = item == null ? "Select an item to inspect it." :
            string.IsNullOrWhiteSpace(item.description) ? "No description available." : item.description;
        itemCount.text = item == null ? "" : item.unlimited ? "Unlimited" : $"Quantity  {inventory.GetAmount(item)}";
        itemIcon.sprite = item != null ? item.itemIcon : null;
        itemIcon.enabled = itemIcon.sprite != null;
        itemIcon.preserveAspect = true;
        emptyMessage.SetActive(inventory.Items.Count == 0);
        equipButton.interactable = item != null;
        dropButton.interactable = item != null && !item.unlimited && item.itemPrefab != null;
    }

    // Only visible rows exist. Rebind on scrolling, without rebuilding a layout or the whole list.
    private void RefreshRows(bool force)
    {
        if (inventory == null) return;
        int needed = Mathf.Max(2, Mathf.CeilToInt(scroll.viewport.rect.height / RowHeight) + 2);
        bool grew = false;
        while (rows.Count < Mathf.Min(needed, RowCount))
        {
            rows.Add(Instantiate(rowPrefab, scroll.content));
            grew = true;
        }
        int first = Mathf.Clamp(Mathf.FloorToInt(scroll.content.anchoredPosition.y / RowHeight), 0,
            Mathf.Max(0, RowCount - 1));
        if (!force && !grew && first == firstVisible) return;
        firstVisible = first;
        for (int i = 0; i < rows.Count; i++)
        {
            int index = first + i;
            bool visible = i < needed && index < RowCount;
            rows[i].gameObject.SetActive(visible);
            if (!visible) continue;
            ((RectTransform)rows[i].transform).anchoredPosition = new Vector2(0f, -index * RowHeight);
            if (IsDevTab)
            {
                StructureData ship = ships[index];
                rows[i].BindShip(inventory, this, index, $"{(index < builtInCount ? "Built-in" : "My save")}  ·  {ship.name}");
                rows[i].SetSelected(index == selectedShip);
                continue;
            }
            Inventory.Entry entry = inventory.Items[index];
            rows[i].Bind(inventory, entry.Item, entry.Amount);
            rows[i].SetSelected(entry.Item == inventory.SelectedItem);
        }
    }

    private void OnRectTransformDimensionsChange()
    {
        if (inventory != null && inventory.IsOpen) Refresh();
    }

    public void MoveSelection(int direction)
    {
        if (RowCount == 0 || IsEditingShip) return;
        int index = IsDevTab ? selectedShip : 0;
        for (int i = 0; !IsDevTab && i < inventory.Items.Count; i++)
            if (inventory.Items[i].Item == inventory.SelectedItem) { index = i; break; }
        index = Mathf.Clamp(index + direction, 0, RowCount - 1);
        if (IsDevTab) SelectShip(index);
        else inventory.SelectItem(inventory.Items[index].Item);
        Vector2 position = scroll.content.anchoredPosition;
        float top = index * RowHeight;
        if (top < position.y) position.y = top;
        else if (top + RowHeight > position.y + scroll.viewport.rect.height)
            position.y = top + RowHeight - scroll.viewport.rect.height;
        scroll.StopMovement();
        scroll.content.anchoredPosition = position;
        RefreshRows(true);
    }

    internal void SelectShip(int index)
    {
        if (IsEditingShip) return;
        selectedShip = index;
        deleteConfirmation = shipMessage = null;
        Refresh();
    }
}
