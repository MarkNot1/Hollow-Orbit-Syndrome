using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
public enum CIRCUIT_COMPONENT_TYPE
{
    RESISTOR,
    WIRE,
    BULB,
    BATTERY,
    CAPACITOR
}
public class Circuit_Component : MonoBehaviour
{
    public CIRCUIT_COMPONENT_TYPE ComponentType;
    public Text Label;
  
    [Space]
    public string Name;
    [Space]
    public List<Circuit_ComponentNode> Nodes;
    /// <summary>
    /// boolean to check if the component is present in the circuit loop or not
    /// </summary>
    public bool FirstNodeVisited;
   
    public Circuit_ComponentNode GetOtherNode(Circuit_ComponentNode referenceNode)
    {

        if (referenceNode == Nodes[0])
            return Nodes[1];
        if (referenceNode == Nodes[1])
            return Nodes[0];
        else
            return null;

    }

}
