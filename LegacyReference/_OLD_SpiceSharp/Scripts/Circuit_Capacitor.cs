using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class Circuit_Capacitor : Circuit_Component
{
    public Text MinValueLabel;
    public Text MaxValueLabel;
    [Space]
    public Slider ValueSlider;
    [Space]
    public float minValue;
    public float MaxValue;
    protected void Awake()
    {
        if (ValueSlider != null)
        {
            ValueSlider.minValue = minValue;
            ValueSlider.maxValue = MaxValue;

            MinValueLabel.text = minValue.ToString();
            MaxValueLabel.text = MaxValue.ToString();
        }
    }
    private void Start()
    {
        Circuit_Component_Item component = new Circuit_Component_Item();
        //Circuit_Capacitor newCapacitor = Instantiate(CapacitorPrefab, ComponentParent);
        //newCapacitor.transform.position = ComponentSpawnPosition.position;
        component.component = this;
        component.componentType = CIRCUIT_COMPONENT_TYPE.CAPACITOR;
        int resistanceCount = Circuit_ConnectionManager.Instance.ComponentsList.Where(x => x.componentType == CIRCUIT_COMPONENT_TYPE.CAPACITOR).Count();
        component.component.gameObject.name = "Capccitor" + resistanceCount.ToString();
        component.component.Name = component.component.gameObject.name;
        Circuit_ConnectionManager.Instance.ComponentsList.Add(component);
        OnSliderValueChanged(ValueSlider.value);
    }
    public void OnSliderValueChanged(Single val)
    {
        Label.text = Circuit_Utilities.FormatFarads(ValueSlider.value);   
    }
}
