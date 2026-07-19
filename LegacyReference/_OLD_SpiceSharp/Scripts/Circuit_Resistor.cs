using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;


public class Circuit_Resistor : Circuit_Component
{
    public Text MinValueLabel;
    public Text MaxValueLabel;
    [Space]
    public Slider ValueSlider;
    [Space]
    public int minValue;
    public int MaxValue;
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
        //Circuit_Resistor newResistor = Instantiate(ResistorPrefab, ComponentParent);
        //newResistor.transform.position = ComponentSpawnPosition.position;
        component.component = this;
        component.componentType = CIRCUIT_COMPONENT_TYPE.RESISTOR;
        int resistanceCount = Circuit_ConnectionManager.Instance.ComponentsList.Where(x => x.componentType == CIRCUIT_COMPONENT_TYPE.RESISTOR).Count();
        component.component.gameObject.name = "Resistor" + resistanceCount.ToString();
        component.component.Name = component.component.gameObject.name;
        Circuit_ConnectionManager.Instance.ComponentsList.Add(component);
        OnSliderValueChanged(ValueSlider.value);
    }
    public void OnSliderValueChanged(Single val)
    {
        Label.text = Circuit_Utilities.FormatOhms(ValueSlider.value);
    }

   
   

}
