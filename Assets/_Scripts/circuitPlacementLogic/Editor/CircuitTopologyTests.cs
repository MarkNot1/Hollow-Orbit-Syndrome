using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class CircuitTopologyTests
{
    private readonly List<GameObject> createdObjects = new();

    [TearDown]
    public void TearDown()
    {
        for (int i = createdObjects.Count - 1; i >= 0; i--)
        {
            if (createdObjects[i] != null)
                UnityEngine.Object.DestroyImmediate(createdObjects[i]);
        }
        createdObjects.Clear();
    }

    [Test]
    public void BatteryDatabaseFootprintIsOneColumnByTwoVerticalCells()
    {
        componentDatabaseSO database = AssetDatabase.LoadAssetAtPath<componentDatabaseSO>(
            "Assets/_Scripts/circuitPlacementLogic/ComponentDatabase.asset");
        componentData battery = database.componentDataList.Find(component => component.ID == 4);

        Assert.That(battery, Is.Not.Null);
        Assert.That(battery.Size, Is.EqualTo(new Vector2Int(1, 2)));
    }

    [Test]
    public void CircuitDetailsActionUsesBothControlKeys()
    {
        InputActionAsset actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(
            "Assets/InputSystem_Actions.inputactions");
        InputAction details = actions.FindAction("Player/CircuitDetails", true);

        Assert.That(details.bindings, Has.Some.Property("effectivePath").EqualTo("<Keyboard>/leftCtrl"));
        Assert.That(details.bindings, Has.Some.Property("effectivePath").EqualTo("<Keyboard>/rightCtrl"));
    }

    [Test]
    public void AllocatesConsecutiveTerminalPairsPerComponentType()
    {
        PlacedCircuitComponent first = CreateComponent(0, new Vector3Int(0, 0));
        PlacedCircuitComponent second = CreateComponent(0, new Vector3Int(1, 0));

        CircuitTopology.Build(new[] { first, second });

        Assert.That(first.Element.Terminal1.TerminalLabel, Is.EqualTo("R1"));
        Assert.That(first.Element.Terminal2.TerminalLabel, Is.EqualTo("R2"));
        Assert.That(second.Element.Terminal1.TerminalLabel, Is.EqualTo("R3"));
        Assert.That(second.Element.Terminal2.TerminalLabel, Is.EqualTo("R4"));
    }

    [Test]
    public void ReusesLowestFreePairWithoutRenamingSurvivors()
    {
        PlacedCircuitComponent first = CreateComponent(0, new Vector3Int(0, 0));
        PlacedCircuitComponent survivor = CreateComponent(0, new Vector3Int(1, 0));
        CircuitTopology.Build(new[] { first, survivor });
        PlacedCircuitComponent replacement = CreateComponent(0, new Vector3Int(2, 0));

        CircuitTopology.Build(new[] { survivor, replacement });

        Assert.That(survivor.Element.Terminal1.TerminalLabel, Is.EqualTo("R3"));
        Assert.That(survivor.Element.Terminal2.TerminalLabel, Is.EqualTo("R4"));
        Assert.That(replacement.Element.Terminal1.TerminalLabel, Is.EqualTo("R1"));
        Assert.That(replacement.Element.Terminal2.TerminalLabel, Is.EqualTo("R2"));
    }

    [Test]
    public void ConnectedNetUsesLowestNumericTerminalAndRestoresAfterSplit()
    {
        PlacedCircuitComponent resistor = CreateComponent(0, new Vector3Int(0, 0));
        PlacedCircuitComponent capacitor = CreateComponent(1, new Vector3Int(1, 0));
        CircuitTopology.Build(new[] { resistor, capacitor });
        WireConnection wire = Connect(resistor.Element.Terminal2, capacitor.Element.Terminal1);

        CircuitTopology.Build(new[] { resistor, capacitor });

        Assert.That(resistor.Element.Terminal2.NetName, Is.EqualTo("C1"));
        Assert.That(capacitor.Element.Terminal1.NetName, Is.EqualTo("C1"));

        wire.Disconnect();
        CircuitTopology.Build(new[] { resistor, capacitor });

        Assert.That(resistor.Element.Terminal2.NetName, Is.EqualTo("R2"));
        Assert.That(capacitor.Element.Terminal1.NetName, Is.EqualTo("C1"));
    }

    [Test]
    public void ThreeWiresOnOneTerminalRemainOneNet()
    {
        PlacedCircuitComponent resistor = CreateComponent(0, new Vector3Int(0, 0));
        PlacedCircuitComponent capacitor = CreateComponent(1, new Vector3Int(1, 0));
        PlacedCircuitComponent diode = CreateComponent(3, new Vector3Int(2, 0));
        PlacedCircuitComponent led = CreateComponent(2, new Vector3Int(3, 0));
        CircuitTopology.Build(new[] { resistor, capacitor, diode, led });

        Connect(resistor.Element.Terminal2, capacitor.Element.Terminal1);
        Connect(resistor.Element.Terminal2, diode.Element.Terminal2);
        Connect(resistor.Element.Terminal2, led.Element.Terminal1);
        CircuitTopology.Build(new[] { resistor, capacitor, diode, led });

        Assert.That(resistor.Element.Terminal2.NetName, Is.EqualTo("C1"));
        Assert.That(capacitor.Element.Terminal1.NetName, Is.EqualTo("C1"));
        Assert.That(diode.Element.Terminal2.NetName, Is.EqualTo("C1"));
        Assert.That(led.Element.Terminal1.NetName, Is.EqualTo("C1"));
        Assert.That(resistor.Element.Terminal2.Connections.Count, Is.EqualTo(3));
    }

    [Test]
    public void WireUsesGeneratedSolidMeshInsteadOfLineRenderer()
    {
        PlacedCircuitComponent resistor = CreateComponent(0, new Vector3Int(0, 0));
        PlacedCircuitComponent capacitor = CreateComponent(1, new Vector3Int(1, 0));

        WireConnection wire = Connect(resistor.Element.Terminal1, capacitor.Element.Terminal1);

        Assert.That(wire.GetComponent<LineRenderer>(), Is.Null);
        Assert.That(wire.GetComponent<MeshRenderer>(), Is.Not.Null);
        Assert.That(wire.GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.GreaterThan(0));
    }

    [Test]
    public void RouterNeverCrossesBlockedComponentFootprint()
    {
        HashSet<Vector2Int> componentCells = new()
        {
            new Vector2Int(2, 1),
            new Vector2Int(2, 2),
            new Vector2Int(3, 1),
            new Vector2Int(3, 2)
        };

        bool found = CircuitWireRouter.TryFindPath(
            6,
            4,
            new Vector2Int(1, 1),
            new Vector2Int(4, 1),
            componentCells.Contains,
            null,
            out List<Vector2Int> path);

        Assert.That(found, Is.True);
        Assert.That(path.Exists(componentCells.Contains), Is.False);
    }

    [Test]
    public void ParallelResistorsShareSupplyNetsAndSimulateCorrectly()
    {
        PlacedCircuitComponent battery = CreateComponent(4, new Vector3Int(0, 0));
        PlacedCircuitComponent first = CreateComponent(0, new Vector3Int(2, 0));
        PlacedCircuitComponent second = CreateComponent(0, new Vector3Int(3, 0));
        CircuitTopology.Build(new[] { battery, first, second });

        Connect(battery.Element.Terminal1, first.Element.Terminal1);
        Connect(battery.Element.Terminal1, second.Element.Terminal1);
        Connect(battery.Element.Terminal2, first.Element.Terminal2);
        Connect(battery.Element.Terminal2, second.Element.Terminal2);

        CircuitTopologyResult topology = CircuitTopology.Build(new[] { battery, first, second });
        CircuitSimulationResult result = CircuitSimulationService.Simulate(topology);

        Assert.That(topology.IsValid, Is.True, string.Join("\n", topology.Errors));
        Assert.That(first.Element.Terminal1.NetName, Is.EqualTo("VCC"));
        Assert.That(second.Element.Terminal2.NetName, Is.EqualTo("GND"));
        Assert.That(first.Element.Terminal2.SpiceNodeName, Is.EqualTo("0"));
        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.NodeVoltages["VCC"], Is.EqualTo(5d).Within(1e-9));
        Assert.That(Math.Abs(result.Elements[first.Element].Current), Is.EqualTo(0.005d).Within(1e-9));
        Assert.That(Math.Abs(result.Elements[second.Element].Current), Is.EqualTo(0.005d).Within(1e-9));
        Assert.That(Math.Abs(result.Elements[battery.Element].Current), Is.EqualTo(0.01d).Within(1e-9));
    }

    [Test]
    public void TwoSeriesResistorsProduceHalfSupplyAtTheirSharedNode()
    {
        PlacedCircuitComponent battery = CreateComponent(4, new Vector3Int(0, 0));
        PlacedCircuitComponent first = CreateComponent(0, new Vector3Int(1, 0));
        PlacedCircuitComponent second = CreateComponent(0, new Vector3Int(2, 0));
        CircuitTopology.Build(new[] { battery, first, second });

        Connect(battery.Element.Terminal1, first.Element.Terminal1);
        Connect(first.Element.Terminal2, second.Element.Terminal1);
        Connect(second.Element.Terminal2, battery.Element.Terminal2);

        CircuitTopologyResult topology = CircuitTopology.Build(new[] { battery, first, second });
        CircuitSimulationResult result = CircuitSimulationService.Simulate(topology);
        string midpoint = first.Element.Terminal2.SpiceNodeName;

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.NodeVoltages[midpoint], Is.EqualTo(2.5d).Within(1e-9));
        Assert.That(result.Elements[first.Element].Voltage, Is.EqualTo(2.5d).Within(1e-9));
        Assert.That(result.Elements[second.Element].Voltage, Is.EqualTo(2.5d).Within(1e-9));
        Assert.That(Math.Abs(result.Elements[first.Element].Current), Is.EqualTo(0.0025d).Within(1e-9));
    }

    [Test]
    public void CapacitorInParallelHasNoDcOperatingPointCurrent()
    {
        PlacedCircuitComponent battery = CreateComponent(4, new Vector3Int(0, 0));
        PlacedCircuitComponent resistor = CreateComponent(0, new Vector3Int(1, 0));
        PlacedCircuitComponent capacitor = CreateComponent(1, new Vector3Int(2, 0));
        CircuitTopology.Build(new[] { battery, resistor, capacitor });

        Connect(battery.Element.Terminal1, resistor.Element.Terminal1);
        Connect(battery.Element.Terminal2, resistor.Element.Terminal2);
        Connect(battery.Element.Terminal1, capacitor.Element.Terminal1);
        Connect(battery.Element.Terminal2, capacitor.Element.Terminal2);

        CircuitSimulationResult result = CircuitSimulationService.Simulate(
            CircuitTopology.Build(new[] { battery, resistor, capacitor }));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(result.Elements[capacitor.Element].Voltage, Is.EqualTo(5d).Within(1e-9));
        Assert.That(result.Elements[capacitor.Element].Current, Is.Zero);
        Assert.That(result.Elements[capacitor.Element].Power, Is.Zero);
    }

    [Test]
    public void RejectsSupplyShortBeforeAndAfterConnection()
    {
        PlacedCircuitComponent battery = CreateComponent(4, new Vector3Int(0, 0));
        CircuitTopology.Build(new[] { battery });

        Assert.That(battery.Element.Terminal2.TerminalLabel, Is.EqualTo("0"));

        Assert.That(
            CircuitTopology.WouldShortSupply(battery.Element.Terminal1, battery.Element.Terminal2),
            Is.True);

        Connect(battery.Element.Terminal1, battery.Element.Terminal2);
        CircuitTopologyResult topology = CircuitTopology.Build(new[] { battery });
        Assert.That(topology.IsValid, Is.False);
        Assert.That(topology.Errors, Has.Some.Contains("short circuit"));
    }

    private PlacedCircuitComponent CreateComponent(int id, Vector3Int position)
    {
        GameObject root = new($"Component {id}");
        createdObjects.Add(root);
        root.transform.position = new Vector3(position.x, 0f, position.y);
        CreateNode(root.transform, "Node_1");
        CreateNode(root.transform, "Node_2");
        PlacedCircuitComponent placed = root.AddComponent<PlacedCircuitComponent>();
        placed.Initialize(id, position, Vector2Int.one);
        return placed;
    }

    private void CreateNode(Transform parent, string nodeName)
    {
        GameObject node = new(nodeName);
        node.transform.SetParent(parent, false);
        node.AddComponent<CircuitNode>();
    }

    private WireConnection Connect(CircuitNode first, CircuitNode second)
    {
        GameObject wireObject = new("Wire", typeof(MeshFilter), typeof(MeshRenderer), typeof(WireConnection));
        createdObjects.Add(wireObject);
        WireConnection wire = wireObject.GetComponent<WireConnection>();
        wire.Initialize(
            first,
            second,
            Array.Empty<Vector2Int>(),
            new[] { Vector3.zero, Vector3.one },
            0.025f,
            null,
            Color.orange,
            false);
        return wire;
    }
}
