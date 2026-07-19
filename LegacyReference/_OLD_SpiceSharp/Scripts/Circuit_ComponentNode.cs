using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SpiceSharp;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[System.Serializable]


public class Circuit_ComponentNode : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler

{
    public bool isStartNode;
    public bool isEndNode;

    [Space]
    public string NodeName;
    public List<Circuit_Wire> connectedWithWires;
    [Space]
    public Circuit_Component parentComponent;

    public Image img;
    public Color HilightedColor;
    public Color NormalColor;
    void Awake()
    {
        SetNodeName("node");
    }
    private void Start()
    {
        if (parentComponent == null)
        {
            ComponentNodesManager.Instance.nodes.Add(this);
        }
    }
    public Circuit_Wire GetOtherWire(Circuit_Wire referenceWire)
    {
        foreach (Circuit_Wire wire in connectedWithWires)
        {
            if (wire != referenceWire)
            {
                return wire;
            }
        }
        return null;
    }

    public void SetNodeName(string name, bool force = false)
    {
        if (isStartNode)
        {
            NodeName = "In";
        }
        else if (isEndNode)
        {
            NodeName = "0";
        }
        else
        {
            NodeName = name;
        }
    }
    
    public void OnPointerEnter(PointerEventData eventData)
    {
        Circuit_ConnectionManager.Instance.currentHoveredNode = this;
        img.color = HilightedColor;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        Circuit_ConnectionManager.Instance.currentHoveredNode = null;
        img.color = NormalColor;
    }


}
