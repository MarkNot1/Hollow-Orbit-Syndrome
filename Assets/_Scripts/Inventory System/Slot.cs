using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Shared view for list rows and hotbar shortcuts. Inventory owns the item data.
public class Slot : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public bool hovering;
    [SerializeField] private TextMeshProUGUI itemNameText;
    private ItemSO heldItem;
    private int itemAmount;
    private Image iconImage, background;
    private TextMeshProUGUI amountTxt;
    private Inventory inventory;
    private int hotbarIndex = -1;

    private void Awake() => CacheReferences();
    private void CacheReferences()
    {
        if (iconImage != null) return;
        background = GetComponent<Image>();
        iconImage = transform.GetChild(0).GetComponent<Image>();
        amountTxt = transform.GetChild(1).GetComponent<TextMeshProUGUI>();
    }

    public ItemSO GetItem() => heldItem;
    public int GetItemAmount() => itemAmount;
    public bool hasItem() => heldItem != null && itemAmount > 0;

    public void Bind(Inventory owner, ItemSO item, int amount, int shortcutIndex = -1)
    {
        inventory = owner;
        hotbarIndex = shortcutIndex;
        SetItem(item, amount);
    }

    public void SetItem(ItemSO item, int amount = 1)
    {
        heldItem = amount > 0 ? item : null;
        itemAmount = heldItem != null ? amount : 0;
        UpdateSlot();
    }

    public void UpdateSlot()
    {
        CacheReferences();
        iconImage.enabled = heldItem != null && heldItem.itemIcon != null;
        iconImage.sprite = heldItem != null ? heldItem.itemIcon : null;
        amountTxt.text = heldItem != null ? itemAmount.ToString() : "";
        if (itemNameText != null) itemNameText.text = heldItem != null ? heldItem.itemName : "";
    }

    public void SetSelected(bool selected, float selectedAlpha = 0.22f, float normalAlpha = 0.04f)
    {
        CacheReferences();
        if (background != null)
            background.color = new Color(0.45f, 1f, 0.65f, selected ? selectedAlpha : normalAlpha);
    }

    public void ClearSlot() => SetItem(null, 0);
    public void OnPointerEnter(PointerEventData eventData) => hovering = true;
    public void OnPointerExit(PointerEventData eventData) => hovering = false;
    private void OnDisable() => hovering = false;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (inventory == null || !inventory.IsOpen || eventData.button != PointerEventData.InputButton.Left) return;
        if (hotbarIndex >= 0) inventory.SelectHotbar(hotbarIndex);
        else inventory.SelectItem(heldItem);
    }
}
