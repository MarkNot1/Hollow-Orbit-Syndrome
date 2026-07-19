using System;
using UnityEngine;

public enum CircuitElementKind
{
    Resistor,
    Capacitor,
    Led,
    Diode,
    Battery
}

public sealed class CircuitElement : MonoBehaviour
{
    public static event Action<CircuitElement> ElectricalValueChanged;

    [SerializeField]
    private CircuitElementKind kind;

    [SerializeField, Min(0.000001f)]
    private double value = 1000d;

    [SerializeField, Min(1e-20f)]
    private double saturationCurrent = 1e-14d;

    [SerializeField, Min(0.1f)]
    private double emissionCoefficient = 1d;

    [SerializeField]
    private CircuitNode terminal1;

    [SerializeField]
    private CircuitNode terminal2;

    [SerializeField, HideInInspector]
    private bool defaultsInitialized;

    public CircuitElementKind Kind => kind;
    public double Value => value;
    public double SaturationCurrent => saturationCurrent;
    public double EmissionCoefficient => emissionCoefficient;
    public CircuitNode Terminal1 => terminal1;
    public CircuitNode Terminal2 => terminal2;
    public PlacedCircuitComponent Owner { get; private set; }
    public string Prefix => GetPrefix(kind);
    public string EntityName { get; internal set; }

    public void Initialize(PlacedCircuitComponent owner)
    {
        Owner = owner;
        ResolveTerminals();
    }

    public void ConfigureFromComponentId(int componentId)
    {
        kind = componentId switch
        {
            0 => CircuitElementKind.Resistor,
            1 => CircuitElementKind.Capacitor,
            2 => CircuitElementKind.Led,
            3 => CircuitElementKind.Diode,
            4 => CircuitElementKind.Battery,
            _ => kind
        };

        if (!defaultsInitialized)
        {
            value = kind switch
            {
                CircuitElementKind.Battery => 5d,
                CircuitElementKind.Capacitor => 1e-6d,
                _ => value > 0d ? value : 1000d
            };

            if (kind == CircuitElementKind.Led)
            {
                saturationCurrent = 1e-20d;
                emissionCoefficient = 2d;
            }

            defaultsInitialized = true;
        }
    }

    public bool HasValidTerminals
        => terminal1 != null && terminal2 != null && terminal1 != terminal2;

    internal void AssignTerminalLabels(string first, string second)
    {
        ResolveTerminals();
        terminal1?.SetTerminalLabel(first);
        terminal2?.SetTerminalLabel(second);
    }

    internal void SetValueForTesting(double newValue)
        => value = newValue;

    public bool TrySetValue(double newValue)
    {
        if (!double.IsFinite(newValue) || newValue <= 0d || value == newValue)
            return false;

        value = newValue;
        ElectricalValueChanged?.Invoke(this);
        return true;
    }

    private void ResolveTerminals()
    {
        if (terminal1 != null && terminal2 != null && terminal1 != terminal2)
            return;

        CircuitNode[] nodes = GetComponentsInChildren<CircuitNode>(true);
        Array.Sort(nodes, CompareNodes);
        terminal1 = nodes.Length > 0 ? nodes[0] : null;
        terminal2 = nodes.Length > 1 ? nodes[1] : null;
    }

    private static int CompareNodes(CircuitNode first, CircuitNode second)
    {
        int firstIndex = ParseTerminalIndex(first != null ? first.TerminalId : string.Empty);
        int secondIndex = ParseTerminalIndex(second != null ? second.TerminalId : string.Empty);
        int indexComparison = firstIndex.CompareTo(secondIndex);
        return indexComparison != 0
            ? indexComparison
            : string.Compare(first?.TerminalId, second?.TerminalId, StringComparison.Ordinal);
    }

    private static int ParseTerminalIndex(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return int.MaxValue;

        int start = value.Length;
        while (start > 0 && char.IsDigit(value[start - 1]))
            start--;

        return start < value.Length && int.TryParse(value.Substring(start), out int index)
            ? index
            : int.MaxValue;
    }

    public static string GetPrefix(CircuitElementKind elementKind)
        => elementKind switch
        {
            CircuitElementKind.Resistor => "R",
            CircuitElementKind.Capacitor => "C",
            CircuitElementKind.Led => "L",
            CircuitElementKind.Diode => "D",
            CircuitElementKind.Battery => "B",
            _ => "N"
        };
}
