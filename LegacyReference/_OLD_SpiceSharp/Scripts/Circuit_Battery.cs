using System;
using UnityEngine;
using UnityEngine.UI;

public class Circuit_Battery : Circuit_Component
{

    public InputField ValueRaw;
    public float Value;

    [Space]
    public int minValue = 0;
    public int maxValue = 100;

    private void Start()
    {
        Circuit_Component_Item component = new Circuit_Component_Item();
        component.component = this;
        component.componentType = CIRCUIT_COMPONENT_TYPE.BATTERY;
       Circuit_ConnectionManager.Instance.ComponentsList.Add(component);

    }
    public void OnInputValueChange(string val)
    {
        if (int.Parse(val) > minValue && int.Parse(val) < maxValue)
        {
            Value = float.Parse(val);
            Label.text = Value + " Volt battery";

        }
        Circuit_ConnectionManager.Instance.TryCompletecircuit();

    }


}