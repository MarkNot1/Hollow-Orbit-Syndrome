using System;
using System.Collections.Generic;
using SpiceSharp;
using SpiceSharp.Components;
using SpiceSharp.Simulations;

public sealed class CircuitElementResult
{
    public double Voltage { get; }
    public double Current { get; }
    public double Power { get; }

    public CircuitElementResult(double voltage, double current)
    {
        Voltage = voltage;
        Current = current;
        Power = voltage * current;
    }
}

public sealed class CircuitSimulationResult
{
    public bool Success { get; }
    public string Message { get; }
    public IReadOnlyDictionary<string, double> NodeVoltages { get; }
    public IReadOnlyDictionary<CircuitElement, CircuitElementResult> Elements { get; }

    private CircuitSimulationResult(
        bool success,
        string message,
        IReadOnlyDictionary<string, double> nodeVoltages,
        IReadOnlyDictionary<CircuitElement, CircuitElementResult> elements)
    {
        Success = success;
        Message = message;
        NodeVoltages = nodeVoltages;
        Elements = elements;
    }

    public static CircuitSimulationResult Failed(string message)
        => new(false, message, new Dictionary<string, double>(),
            new Dictionary<CircuitElement, CircuitElementResult>());

    public static CircuitSimulationResult Completed(
        Dictionary<string, double> nodeVoltages,
        Dictionary<CircuitElement, CircuitElementResult> elements)
        => new(true, string.Empty, nodeVoltages, elements);

    public double GetNodeVoltage(CircuitNode node)
    {
        string name = CircuitSimulationService.GetNodeName(node);
        return name == "0" ? 0d : NodeVoltages.TryGetValue(name, out double voltage) ? voltage : 0d;
    }
}

public static class CircuitSimulationService
{
    public static CircuitSimulationResult Simulate(CircuitTopologyResult topology)
    {
        if (topology == null)
            return CircuitSimulationResult.Failed("Circuit topology is missing.");
        if (!topology.IsValid)
            return CircuitSimulationResult.Failed(string.Join("\n", topology.Errors));

        try
        {
            Circuit circuit = new();
            foreach (CircuitElement element in topology.Elements)
                AddElement(circuit, element);

            OP simulation = new("Circuit operating point");
            Dictionary<string, RealVoltageExport> voltageExports = new(StringComparer.Ordinal);
            foreach (CircuitNet net in topology.Nets)
            {
                if (net.SpiceName != "0" && !voltageExports.ContainsKey(net.SpiceName))
                    voltageExports.Add(net.SpiceName, new RealVoltageExport(simulation, net.SpiceName));
            }

            Dictionary<CircuitElement, RealPropertyExport> currentExports = new();
            foreach (CircuitElement element in topology.Elements)
                currentExports[element] = new RealPropertyExport(simulation, element.EntityName, "i");

            Dictionary<string, double> nodeVoltages = new(StringComparer.Ordinal)
            {
                ["0"] = 0d,
                ["GND"] = 0d
            };
            Dictionary<CircuitElement, CircuitElementResult> elementResults = new();
            bool exported = false;

            foreach (int _ in simulation.Run(circuit, OP.ExportOperatingPoint))
            {
                exported = true;
                foreach (KeyValuePair<string, RealVoltageExport> export in voltageExports)
                    nodeVoltages[export.Key] = export.Value.Value;

                foreach (CircuitElement element in topology.Elements)
                {
                    double positive = GetNodeVoltage(nodeVoltages, GetNodeName(element.Terminal1));
                    double negative = GetNodeVoltage(nodeVoltages, GetNodeName(element.Terminal2));
                    double current = TryGetCurrent(currentExports[element], element.Kind);
                    elementResults[element] = new CircuitElementResult(positive - negative, current);
                }
            }

            return exported
                ? CircuitSimulationResult.Completed(nodeVoltages, elementResults)
                : CircuitSimulationResult.Failed("SpiceSharp returned no operating-point result.");
        }
        catch (Exception exception)
        {
            return CircuitSimulationResult.Failed(
                $"SpiceSharp operating-point simulation failed: {exception.Message}");
        }
    }

    private static void AddElement(Circuit circuit, CircuitElement element)
    {
        string first = GetNodeName(element.Terminal1);
        string second = GetNodeName(element.Terminal2);
        switch (element.Kind)
        {
            case CircuitElementKind.Battery:
                circuit.Add(new VoltageSource(element.EntityName, first, second, element.Value));
                break;
            case CircuitElementKind.Resistor:
                circuit.Add(new Resistor(element.EntityName, first, second, element.Value));
                break;
            case CircuitElementKind.Capacitor:
                circuit.Add(new Capacitor(element.EntityName, first, second, element.Value));
                break;
            case CircuitElementKind.Diode:
            case CircuitElementKind.Led:
                string modelName = $"{element.EntityName}Model";
                DiodeModel model = new(modelName);
                model.Parameters.SaturationCurrent = element.SaturationCurrent;
                model.Parameters.EmissionCoefficient = element.EmissionCoefficient;
                circuit.Add(model);
                circuit.Add(new Diode(element.EntityName, first, second, modelName));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(element.Kind), element.Kind, null);
        }
    }

    private static double GetNodeVoltage(IReadOnlyDictionary<string, double> voltages, string nodeName)
        => nodeName == "0"
            ? 0d
            : voltages.TryGetValue(nodeName, out double value) ? value : 0d;

    public static string GetNodeName(CircuitNode node)
    {
        string netName = node?.NetName;
        return string.Equals(netName, "GND", StringComparison.OrdinalIgnoreCase) || netName == "0"
            ? "0"
            : netName;
    }

    private static double TryGetCurrent(RealPropertyExport export, CircuitElementKind kind)
    {
        if (kind == CircuitElementKind.Capacitor)
            return 0d;

        try
        {
            return export.IsValid ? export.Value : 0d;
        }
        catch (SpiceSharpException)
        {
            return 0d;
        }
    }
}
