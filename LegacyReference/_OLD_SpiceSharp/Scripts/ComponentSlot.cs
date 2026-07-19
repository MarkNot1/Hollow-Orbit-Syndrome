using System.Collections;
using System.Collections.Generic;
using UnityEngine;
public enum SLOT_TYPE
{
    BATTERY,
    CAPACITOR,
    RESISTOR,
    BULB
}
public class ComponentSlot : MonoBehaviour
{
    public SLOT_TYPE type;
    public bool isFilled;
}
