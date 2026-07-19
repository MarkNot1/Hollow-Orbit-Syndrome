using System.Collections;
using System.Collections.Generic;
using SpiceSharp.Components;
using SpiceSharp.Simulations;
using SpiceSharp;
using UnityEngine;
using System.Linq;
using System.ComponentModel;
using System;
using UnityEngine.UI;

[System.Serializable]
public class Circuit_Component_Item
{
    public CIRCUIT_COMPONENT_TYPE componentType;
    public Circuit_Component component;
    public bool isConnectedInCircuit;
}

public class Circuit_ConnectionManager : MonoBehaviour
{
    public static Circuit_ConnectionManager Instance;
    public Circuit_ComponentNode currentHoveredNode;
    public Text HelpText;
    [Space]
    public Circuit_Resistor ResistorPrefab;
    public Circuit_Capacitor CapacitorPrefab;
    public Circuit_Bulb BulbPrefab;
    public Circuit_Battery BatteryPrefab;
    public List<Button> HudButtons;
    public bool isCircuitCompleted;
    Circuit_Component_Item getBatteryComponent
    {
        get
        {
            return ComponentsList.Where(x => x.componentType == CIRCUIT_COMPONENT_TYPE.BATTERY).FirstOrDefault();
        }
    }
    [Space]
    public Transform ComponentSpawnPosition;
    public Transform ComponentParent;
    public float BOARD_GRID_SIZE = 1f; // Size of each grid square
    public float BOARD_GRID_SIZE_2D = 1f; // Size of each grid square
    public Vector2 BOARD_GRID_SIZE_2D_OFFSET = Vector2.zero; // Size of each grid square


    /// <summary>
    /// 
    /// ////////////////////////////////////
    /// </summary>

    public List<Circuit_Component_Item> ComponentsList;
    VoltageSource Voltage_Source;
    Circuit circuit;
    public List<string> circuitNodeNames;
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        circuit = new Circuit();
    }
    public bool AddingWires;
    public bool RemovingWires;
    public bool AddingNodes;

    public bool IsMouseOverComponent;
    public void AllowAddingNodes(bool val)
    {
        if (val)
        {
            HelpText.text = "click anywhere on the canvas to add a component connector node.";
        }
        else
        {
            HelpText.text = "";
        }
        AddingNodes = val;
        foreach (Button button in HudButtons)
        {
            button.interactable = !AddingNodes;
        }
    }
    public void AllowAddingWires(bool val)
    {
        if (val)
        {
            HelpText.text = "click on a node pointer and drag it to the node pointer you want to connect to.";
        }
        else
        {
            HelpText.text = "";
        }
        AddingWires = val;
        foreach (Button button in HudButtons)
        {
            button.interactable = !AddingWires;
        }
    }
    public void AllowRemovingWires(bool val)
    {
        if (val)
        {
            HelpText.text = "click on a wire you want to remove.";
        }
        else
        {
            HelpText.text = "";
        }
        RemovingWires = val;
        foreach (Button button in HudButtons)
        {
            button.interactable = !AddingWires;
        }
    }
    public void AddBattery()
    {
        if (getBatteryComponent == null)
        {
         //   Circuit_Component_Item component = new Circuit_Component_Item();
            Circuit_Battery newBattery = Instantiate(BatteryPrefab, ComponentParent);
            newBattery.transform.position = ComponentSpawnPosition.position;
          //  component.component = newBattery;
          //  component.componentType = CIRCUIT_COMPONENT_TYPE.BATTERY;
         //   ComponentsList.Add(component);

        }
        else
        {

            //battery is added already . you can add only one battery in a circuit;
        }
    }
    public void AddResistance()
    {
       // Circuit_Component_Item component = new Circuit_Component_Item();
        Circuit_Resistor newResistor = Instantiate(ResistorPrefab, ComponentParent);
        newResistor.transform.position = ComponentSpawnPosition.position;
        //component.component = newResistor;
        //component.componentType = CIRCUIT_COMPONENT_TYPE.RESISTOR;
        //int resistanceCount = ComponentsList.Where(x => x.componentType == CIRCUIT_COMPONENT_TYPE.RESISTOR).Count();
        //component.component.gameObject.name = "Resistor" + resistanceCount.ToString();
        //component.component.Name = component.component.gameObject.name;
        //ComponentsList.Add(component);

    }
    public void AddCapacitor()
    {
       // Circuit_Component_Item component = new Circuit_Component_Item();
        Circuit_Capacitor newCapacitor = Instantiate(CapacitorPrefab, ComponentParent);
        newCapacitor.transform.position = ComponentSpawnPosition.position;
        //component.component = newCapacitor;
        //component.componentType = CIRCUIT_COMPONENT_TYPE.CAPACITOR;
        //int resistanceCount = ComponentsList.Where(x => x.componentType == CIRCUIT_COMPONENT_TYPE.CAPACITOR).Count();
        //component.component.gameObject.name = "Capccitor" + resistanceCount.ToString();
        //component.component.Name = component.component.gameObject.name;
        //ComponentsList.Add(component);

    }
    public void AddBulb()
    {
        //Circuit_Component_Item component = new Circuit_Component_Item();
        Circuit_Bulb newBulb = Instantiate(BulbPrefab, ComponentParent);
        newBulb.transform.position = ComponentSpawnPosition.position;
        //component.component = newBulb;
        //component.componentType = CIRCUIT_COMPONENT_TYPE.BULB;
        //int bulbCount = ComponentsList.Where(x => x.componentType == CIRCUIT_COMPONENT_TYPE.BULB).Count();
        //component.component.gameObject.name = "Bulb" + bulbCount.ToString();
        //component.component.Name = component.component.gameObject.name;
        //ComponentsList.Add(component);

    }
    public void AddWire(Circuit_Wire wire)
    {
        Circuit_Component_Item component = new Circuit_Component_Item();
        component.component = wire;
        component.componentType = CIRCUIT_COMPONENT_TYPE.WIRE;
        int wireCount = ComponentsList.Where(x => x.componentType == CIRCUIT_COMPONENT_TYPE.WIRE).Count();
        component.component.gameObject.name = "Wire" + wireCount.ToString();
        component.component.Name = component.component.gameObject.name;
        ComponentsList.Add(component);

    }
    public void RemoveWire(Circuit_Wire wire)
    {

        ComponentsList.Remove(ComponentsList.Where(x => x.component == wire).First());

    }
    public void ClearBoard()
    {
        StopAllCoroutines();
        foreach (Circuit_Component_Item c in ComponentsList)
        {
            Destroy(c.component.gameObject);
        }
        LineSelector.Instance.RemoveAllLines();
        ComponentNodesManager.Instance.RemoveAllNodes();
        ComponentsList.Clear();
        isCircuitCompleted = false;
        circuit.Clear();


    }
    public void TryCompletecircuit()
    {
        StopAllCoroutines();
        StartCoroutine(TryCompletecircuitCoroutine());
    }
    public IEnumerator TryCompletecircuitCoroutine()
    {
        //first we iterate over all the components and set the node names of the components according to the connection sequence;
        //since we are building the circuit in series/
        isCircuitCompleted = false;
        //we start with the battery 
        if (getBatteryComponent == null)
        {
            yield break;
        }
        else
        {
            if (circuitNodeNames != null)
                circuitNodeNames.Clear();
            else
                circuitNodeNames = new List<string>();
            //reset the components for visiting again
            foreach (Circuit_Component_Item component in ComponentsList.Where(x => x.componentType != CIRCUIT_COMPONENT_TYPE.WIRE).ToList())
            {
                component.component.FirstNodeVisited = false;
            }
            Circuit_ComponentNode NextNode = null;
            Circuit_Wire connectedWithWire = null;
            int nodeIndex = 1;
            string nodeName = "";
            try
            {
                Circuit_ComponentNode startingNode = getBatteryComponent.component.Nodes.Where(x => x.isStartNode).First();
                connectedWithWire = startingNode.connectedWithWires[0]; // starting node should have only one wire connected.
                connectedWithWire.firstNode = startingNode;
                nodeName = startingNode.NodeName;
                if (!circuitNodeNames.Contains(nodeName))
                {
                    circuitNodeNames.Add(nodeName);
                }

                connectedWithWire.wireName = nodeName;
                NextNode = connectedWithWire.GetOtherNode(startingNode); // we get the other node connected to the wire
                connectedWithWire.SecondNode = NextNode;
                NextNode.SetNodeName(nodeName); // set the node name as the previous node of the wire.
            }
            catch (Exception ex)
            {
                Debug.Log("circuit not completed yet.");
                yield break;
            }
            while (!NextNode.isEndNode)
            {
                yield return null;
                try
                {

                    connectedWithWire = NextNode.GetOtherWire(connectedWithWire);//now we get the other wire connected to the node to continue parsing the circuit.

                    if (connectedWithWire == null)
                    {
                        //there is only one wire connected with this node. this can mean 2 things.
                        // #1 there are no other connections
                        // #2 the node is a child of a circuit component.
                        if (NextNode.parentComponent != null)
                        {
                            if (NextNode.parentComponent.FirstNodeVisited) //this is the condition where there are no further connections and circuit is incomplete
                            {
                                break;
                            }
                            NextNode.parentComponent.FirstNodeVisited = true; //we mark it as true so that the code does not cycle between the nodes gain and again in case of a broken connection
                            // we have come across a circuit component. 
                            // here we will update the node name to the next node.
                            nodeName = "n" + nodeIndex;
                            nodeIndex++;
                            if (!circuitNodeNames.Contains(nodeName))
                            {
                                circuitNodeNames.Add(nodeName);
                            }

                            //get the other node of the circuit component. so that we can move forward
                            NextNode = NextNode.parentComponent.GetOtherNode(NextNode);
                            //now assign the node name to the next node of the circuit component
                            NextNode.NodeName = nodeName;
                        }
                        else
                        {
                            // we are at a dead end.
                            break;
                        }

                    }
                    else
                    {
                        //we are now at the second wire going out of the node.
                        //set the name of the wire as previously set name.
                        connectedWithWire.wireName = nodeName;
                        connectedWithWire.firstNode = NextNode;
                        //get the other attached node of the wire.
                        NextNode = connectedWithWire.GetOtherNode(NextNode);
                        connectedWithWire.SecondNode = NextNode;
                        //set the node name of the node as previously set name. ONLY if its not the negative node of battery
                        if (!NextNode.isEndNode)
                        {
                            NextNode.NodeName = nodeName;
                        }

                    }

                }
                catch (Exception ex)
                {
                    Debug.Log("circuit not completed yet.");
                }

            }
            //since we have reached the battey negative node, we will back track the wire to fix the node name until the last circuit component.
            if (NextNode.isEndNode)
            {
                nodeName = NextNode.NodeName;
                if (!circuitNodeNames.Contains(nodeName))
                {
                    circuitNodeNames.Add(nodeName);
                }

                connectedWithWire = NextNode.connectedWithWires[0];
                connectedWithWire.wireName = nodeName;
                //we get to the other node of the wire connected to the present node.
                NextNode = connectedWithWire.GetOtherNode(NextNode);
                NextNode.NodeName = nodeName;
                connectedWithWire = NextNode.GetOtherWire(connectedWithWire);
                while (NextNode.parentComponent == null)
                {
                    connectedWithWire.wireName = nodeName;
                    NextNode = connectedWithWire.GetOtherNode(NextNode);
                    NextNode.NodeName = nodeName;
                    connectedWithWire = NextNode.GetOtherWire(connectedWithWire);

                    yield return null;
                }
                isCircuitCompleted = true;
            }
        }
        if (isCircuitCompleted)
        {
            circuit.Clear();
            foreach (Circuit_Component_Item component in ComponentsList)
            {
                //if (component.component.Nodes.Where(x => x.IsConnected).FirstOrDefault() == null)
                //    continue;
                if (component.componentType == CIRCUIT_COMPONENT_TYPE.BATTERY) //we dont check the battery to be included in the circuit as its the first component and needed in a circuit
                {
                    //Start with a voltage source i.e a battery
                    //we save the voltage source for later use in the code
                    Voltage_Source = new VoltageSource(component.component.Name,
                                                       component.component.Nodes.Where(x => x.isStartNode).First().NodeName,
                                                       component.component.Nodes.Where(x => x.isEndNode).First().NodeName,
                                                       ((Circuit_Battery)component.component).Value);
                    circuit.Add(Voltage_Source);
                }
                if (component.componentType == CIRCUIT_COMPONENT_TYPE.RESISTOR && component.component.FirstNodeVisited)
                {
                    //add a resistance to the circuit
                    circuit.Add(new Resistor(component.component.Name,
                                             component.component.Nodes[0].NodeName,
                                             component.component.Nodes[1].NodeName,
                                             ((Circuit_Resistor)component.component).ValueSlider.value));
                }
                if (component.componentType == CIRCUIT_COMPONENT_TYPE.CAPACITOR && component.component.FirstNodeVisited)
                {
                    //add a resistance to the circuit
                    circuit.Add(new Capacitor(component.component.Name,
                                             component.component.Nodes[0].NodeName,
                                             component.component.Nodes[1].NodeName,
                                             ((Circuit_Capacitor)component.component).ValueSlider.value));
                }
                //if (component.componentType == CIRCUIT_COMPONENT_TYPE.WIRE)
                //{
                //    //add a resistance to the circuit
                //    circuit.Add(new Resistor(component.component.Name,
                //                             component.component.Nodes[0].NodeName,
                //                             component.component.Nodes[1].NodeName,
                //                             0));
                //}
                if (component.componentType == CIRCUIT_COMPONENT_TYPE.BULB && component.component.FirstNodeVisited)
                {
                    //add a bulb to the circuit
                    circuit.Add(new Resistor(component.component.Name,
                                             component.component.Nodes[0].NodeName,
                                             component.component.Nodes[1].NodeName,
                                             ((Circuit_Bulb)component.component).resistance));
                }

            }

            // Create a DC sweep and register to the event for exporting simulation data
            //simulation will be done in a single step as we want to run the circuit with the desired voltage of the battery source once.
            var dc = new DC("DC 1", Voltage_Source.Name, Voltage_Source.Parameters.DcValue, Voltage_Source.Parameters.DcValue, 1);

            StartCoroutine(TrySimulateCircuit(dc));
            //ElectronsSimulatorManager.Instance.ResetAllSimulators();
        }
    }

    IEnumerator TrySimulateCircuit(DC dc)
    {
        while (true)
        {
            // Catch exported data
            foreach (int _ in dc.Run(circuit))
            {
                foreach (Circuit_Component_Item component in ComponentsList.Where(x => x.componentType == CIRCUIT_COMPONENT_TYPE.WIRE).ToList())
                {
                    Circuit_Wire wire = (Circuit_Wire)component.component;
                    wire.SetParameters((float)dc.GetVoltage(wire.wireName),MathF.Abs((float)new RealPropertyExport(dc, Voltage_Source.Name, "i").Value));
                }
                foreach (Circuit_Component_Item component in ComponentsList.Where(x => x.componentType == CIRCUIT_COMPONENT_TYPE.BULB).ToList())
                {
                    Circuit_Bulb bulb = (Circuit_Bulb)component.component;
                    bulb.GetVoltageAcrossBulb(dc); 

                }

            }

            yield return null;
        }
    }


}
