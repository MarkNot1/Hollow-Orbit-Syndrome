using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public sealed class CircuitSimulationTests
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
        Assert.That(CircuitSimulationService.GetNodeName(first.Element.Terminal2), Is.EqualTo("0"));
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
        string midpoint = CircuitSimulationService.GetNodeName(first.Element.Terminal2);

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
            CircuitNode.WouldShortSupply(battery.Element.Terminal1, battery.Element.Terminal2),
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
