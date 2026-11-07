using System.Collections.Generic;
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
        inventory = owner;
        equipButton.onClick.AddListener(inventory.EquipSelected);
        dropButton.onClick.AddListener(inventory.DropSelected);
        scroll.onValueChanged.AddListener(_ => RefreshRows(false));
    }

    public void Refresh()
    {
        if (inventory == null) return;
        scroll.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, inventory.Items.Count * RowHeight);
        float maximum = Mathf.Max(0f, scroll.content.rect.height - scroll.viewport.rect.height);
        Vector2 position = scroll.content.anchoredPosition;
        position.y = Mathf.Clamp(position.y, 0f, maximum);
        scroll.content.anchoredPosition = position;
        RefreshRows(true);
        ItemSO item = inventory.SelectedItem;
        itemTitle.text = item != null ? item.itemName : "";
        itemDescription.text = item == null ? "Select an item to inspect it." :
            string.IsNullOrWhiteSpace(item.description) ? "No description available." : item.description;
        itemCount.text = item != null ? $"Quantity  {inventory.GetAmount(item)}" : "";
        itemIcon.sprite = item != null ? item.itemIcon : null;
        itemIcon.enabled = itemIcon.sprite != null;
        emptyMessage.SetActive(inventory.Items.Count == 0);
        equipButton.interactable = item != null;
        dropButton.interactable = item != null && item.itemPrefab != null;
    }

    // Only visible rows exist. Rebind on scrolling, without rebuilding a layout or the whole list.
    private void RefreshRows(bool force)
    {
        if (inventory == null) return;
        int needed = Mathf.Max(2, Mathf.CeilToInt(scroll.viewport.rect.height / RowHeight) + 2);
        bool grew = false;
        while (rows.Count < Mathf.Min(needed, inventory.Items.Count))
        {
            rows.Add(Instantiate(rowPrefab, scroll.content));
            grew = true;
        }
        int first = Mathf.Clamp(Mathf.FloorToInt(scroll.content.anchoredPosition.y / RowHeight), 0,
            Mathf.Max(0, inventory.Items.Count - 1));
        if (!force && !grew && first == firstVisible) return;
        firstVisible = first;
        for (int i = 0; i < rows.Count; i++)
        {
            int index = first + i;
            bool visible = i < needed && index < inventory.Items.Count;
            rows[i].gameObject.SetActive(visible);
            if (!visible) continue;
            Inventory.Entry entry = inventory.Items[index];
            ((RectTransform)rows[i].transform).anchoredPosition = new Vector2(0f, -index * RowHeight);
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
        if (inventory.Items.Count == 0) return;
        int index = 0;
        for (int i = 0; i < inventory.Items.Count; i++)
            if (inventory.Items[i].Item == inventory.SelectedItem) { index = i; break; }
        index = Mathf.Clamp(index + direction, 0, inventory.Items.Count - 1);
        inventory.SelectItem(inventory.Items[index].Item);
        Vector2 position = scroll.content.anchoredPosition;
        float top = index * RowHeight;
        if (top < position.y) position.y = top;
        else if (top + RowHeight > position.y + scroll.viewport.rect.height)
            position.y = top + RowHeight - scroll.viewport.rect.height;
        scroll.StopMovement();
        scroll.content.anchoredPosition = position;
        RefreshRows(true);
    }
}
