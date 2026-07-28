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
    public void DetectsSupplyShortBeforeSimulation()
    {
        PlacedCircuitComponent battery = CreateComponent(4, new Vector3Int(0, 0));
        CircuitTopology.Build(new[] { battery });

        Assert.That(battery.Element.Terminal2.TerminalLabel, Is.EqualTo("0"));

        Connect(battery.Element.Terminal1, battery.Element.Terminal2);
        CircuitTopologyResult topology = CircuitTopology.Build(new[] { battery });
        Assert.That(topology.IsValid, Is.True);
        Assert.That(topology.HasBatteryShort, Is.True);
        Assert.That(topology.ShortedBattery, Is.SameAs(battery.Element));
    }

    [Test]
    public void BatteryShortIsStructuredAndBatteryNeverBreaks()
    {
        PlacedCircuitComponent battery = CreateComponent(4, new Vector3Int(0, 0));
        CircuitTopology.Build(new[] { battery });
        WireConnection shortWire = Connect(battery.Element.Terminal1, battery.Element.Terminal2);

        CircuitTopologyResult shorted = CircuitTopology.Build(new[] { battery });

        Assert.That(shorted.HasBatteryShort, Is.True);
        Assert.That(shorted.ShortedBattery, Is.SameAs(battery.Element));
        Assert.That(battery.Element.Break("test"), Is.False);
        Assert.That(battery.Element.IsBroken, Is.False);
        Assert.That(battery.Element.SetBatteryShorted(true, false), Is.True);
        Assert.That(battery.Element.State, Is.EqualTo(CircuitComponentState.BatteryShorted));

        shortWire.Disconnect();
        CircuitTopologyResult recovered = CircuitTopology.Build(new[] { battery });
        battery.Element.SetBatteryShorted(recovered.HasBatteryShort, false);

        Assert.That(recovered.HasBatteryShort, Is.False);
        Assert.That(battery.Element.State, Is.EqualTo(CircuitComponentState.Healthy));
    }

    [Test]
    public void BrokenComponentSimulatesAsAirGapResistance()
    {
        PlacedCircuitComponent battery = CreateComponent(4, new Vector3Int(0, 0));
        PlacedCircuitComponent resistor = CreateComponent(0, new Vector3Int(1, 0));
        CircuitTopology.Build(new[] { battery, resistor });
        Connect(battery.Element.Terminal1, resistor.Element.Terminal1);
        Connect(battery.Element.Terminal2, resistor.Element.Terminal2);
        Assert.That(resistor.Element.Break("Overload", false), Is.True);

        CircuitSimulationResult result = CircuitSimulationService.Simulate(
            CircuitTopology.Build(new[] { battery, resistor }));

        Assert.That(result.Success, Is.True, result.Message);
        Assert.That(
            Math.Abs(result.Elements[resistor.Element].Current),
            Is.EqualTo(5d / CircuitElement.BrokenResistance).Within(1e-15));
        Assert.That(resistor.Element.Value, Is.EqualTo(1000d));
        Assert.That(resistor.Element.EffectiveResistance, Is.EqualTo(CircuitElement.BrokenResistance));
    }

    [Test]
    public void OpenSlotKeepsWireAndUsesUniqueTerminalLabels()
    {
        PlacedCircuitComponent resistor = CreateComponent(0, new Vector3Int(0, 0));
        PlacedCircuitComponent capacitor = CreateComponent(1, new Vector3Int(2, 0));
        CircuitTopology.Build(new[] { resistor, capacitor });
        WireConnection wire = Connect(resistor.Element.Terminal1, capacitor.Element.Terminal1);

        GameObject slotObject = new("Open Slot");
        createdObjects.Add(slotObject);
        CircuitNode first = CreateStandaloneNode(slotObject.transform, "A");
        CircuitNode second = CreateStandaloneNode(slotObject.transform, "B");
        OpenCircuitSlot slot = slotObject.AddComponent<OpenCircuitSlot>();
        slot.Initialize("slot-one", Vector3Int.zero, Vector2Int.one, 0, first, second);
        Assert.That(wire.ReplaceEndpoint(resistor.Element.Terminal1, first), Is.True);
        slot.RefreshLabels();

        Assert.That(wire.StartNode, Is.SameAs(first));
        Assert.That(first.Connections, Contains.Item(wire));
        Assert.That(first.TerminalLabel, Does.StartWith("OPEN_"));
        Assert.That(first.TerminalLabel, Is.Not.EqualTo(second.TerminalLabel));
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

    private static CircuitNode CreateStandaloneNode(Transform parent, string nodeName)
    {
        GameObject node = new(nodeName);
        node.transform.SetParent(parent, false);
        return node.AddComponent<CircuitNode>();
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
            4f,
            false);
        return wire;
    }
}
