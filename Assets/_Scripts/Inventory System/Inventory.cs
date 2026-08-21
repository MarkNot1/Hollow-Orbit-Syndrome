using UnityEngine;
using System.Collections.Generic;

public class Inventory : MonoBehaviour
{
    public GameObject hotbarObj;
    public GameObject inventorySlotParent;


    private List<Slot> inventorySlots = new List<Slot>();
    private List<Slot> hotbarSlots = new List<Slot>();
    private List<Slot> allSlots = new List<Slot>();

    private void Awake()
    {
        inventorySlots.AddRange(inventorySlotParent.GetComponentsInChildren<Slot>());
        hotbarSlots.AddRange(hotbarObj.GetComponentsInChildren<Slot>());

        allSlots.AddRange(inventorySlots);
        allSlots.AddRange(hotbarSlots);
    }

    // Update is called once per frame
    void Update()
    {
        
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
}
