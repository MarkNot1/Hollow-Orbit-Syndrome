using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed class CircuitNet
{
    public string DisplayName { get; }
    public string SpiceName { get; }
    public IReadOnlyList<CircuitNode> Nodes { get; }

    public CircuitNet(string displayName, IReadOnlyList<CircuitNode> nodes)
    {
        DisplayName = displayName;
        SpiceName = string.Equals(displayName, "GND", StringComparison.OrdinalIgnoreCase) || displayName == "0"
            ? "0"
            : displayName;
        Nodes = nodes;
    }
}

public sealed class CircuitTopologyResult
{
    public IReadOnlyList<CircuitElement> Elements { get; }
    public IReadOnlyList<CircuitNet> Nets { get; }
    public IReadOnlyList<string> Errors { get; }
    public bool IsValid => Errors.Count == 0;

    public CircuitTopologyResult(
        IReadOnlyList<CircuitElement> elements,
        IReadOnlyList<CircuitNet> nets,
        IReadOnlyList<string> errors)
    {
        Elements = elements;
        Nets = nets;
        Errors = errors;
    }
}

public static class CircuitTopology
{
    public static CircuitTopologyResult Build(IEnumerable<PlacedCircuitComponent> placedComponents)
    {
        List<string> errors = new();
        List<CircuitElement> elements = placedComponents
            .Where(component => component != null)
            .Select(component =>
            {
                component.ConfigureNodes();
                return component.Element;
            })
            .Where(element => element != null)
            .Distinct()
            .OrderBy(element => element.Owner.GridPosition.y)
            .ThenBy(element => element.Owner.GridPosition.x)
            .ThenBy(element => element.Owner.ComponentId)
            .ToList();

        AssignMissingTerminalLabels(elements);
        AssignEntityNames(elements);

        foreach (CircuitElement element in elements)
        {
            if (!element.HasValidTerminals)
                errors.Add($"{element.name} must have exactly two ordered circuit terminals.");
            else if (!double.IsFinite(element.Value) || element.Value <= 0d)
                errors.Add($"{element.EntityName} has an invalid electrical value.");
        }

        List<CircuitNode> nodes = elements
            .Where(element => element.HasValidTerminals)
            .SelectMany(element => element.GetTerminals())
            .Distinct()
            .ToList();

        DisjointSet<CircuitNode> sets = new(nodes);
        foreach (CircuitNode node in nodes)
        {
            foreach (WireConnection connection in node.Connections)
            {
                if (connection?.StartNode != null && connection.EndNode != null &&
                    sets.Contains(connection.StartNode) && sets.Contains(connection.EndNode))
                {
                    sets.Union(connection.StartNode, connection.EndNode);
                }
            }
        }

        List<CircuitNet> nets = new();
        foreach (List<CircuitNode> group in nodes.GroupBy(sets.Find).Select(group => group.ToList()))
        {
            bool hasGround = group.Any(node => IsGroundLabel(node.TerminalLabel));
            bool hasVcc = group.Any(node => LabelEquals(node.TerminalLabel, "VCC"));
            if (hasGround && hasVcc)
                errors.Add("VCC and GND are connected to the same net (battery short circuit).");

            string netName = hasGround
                ? "GND"
                : hasVcc
                    ? "VCC"
                    : group.Select(node => node.TerminalLabel)
                        .Where(label => !string.IsNullOrWhiteSpace(label))
                        .OrderBy(label => label, TerminalLabelComparer.Instance)
                        .FirstOrDefault() ?? "UNNAMED";

            foreach (CircuitNode node in group)
                node.SetNetName(netName);
            nets.Add(new CircuitNet(netName, group));
        }

        List<CircuitElement> batteries = elements
            .Where(element => element.Kind == CircuitElementKind.Battery)
            .ToList();
        if (batteries.Count != 1)
            errors.Add($"Circuit requires exactly one battery; found {batteries.Count}.");

        foreach (CircuitElement element in elements.Where(element => element.HasValidTerminals))
        {
            if (element.Terminal1.Connections.Count == 0 || element.Terminal2.Connections.Count == 0)
                errors.Add($"{element.EntityName} has an unconnected terminal.");
        }

        return new CircuitTopologyResult(elements, nets, errors.Distinct().ToList());
    }

    public static bool WouldShortSupply(CircuitNode first, CircuitNode second)
    {
        if (first == null || second == null)
            return false;

        string firstName = first.NetName;
        string secondName = second.NetName;
        return LabelEquals(firstName, "VCC") && IsGroundLabel(secondName) ||
            IsGroundLabel(firstName) && LabelEquals(secondName, "VCC");
    }

    private static void AssignMissingTerminalLabels(IReadOnlyList<CircuitElement> elements)
    {
        Dictionary<string, HashSet<int>> usedByPrefix = new(StringComparer.Ordinal);
        foreach (CircuitElement element in elements)
        {
            if (element.Kind == CircuitElementKind.Battery)
                continue;

            string prefix = element.Prefix;
            if (!usedByPrefix.TryGetValue(prefix, out HashSet<int> used))
            {
                used = new HashSet<int>();
                usedByPrefix.Add(prefix, used);
            }

            ReserveLabel(element.Terminal1?.TerminalLabel, prefix, used);
            ReserveLabel(element.Terminal2?.TerminalLabel, prefix, used);
        }

        foreach (CircuitElement element in elements)
        {
            if (!element.HasValidTerminals)
                continue;

            if (element.Kind == CircuitElementKind.Battery)
            {
                element.AssignTerminalLabels("VCC", "0");
                continue;
            }

            bool alreadyAssigned = TryParseLabel(element.Terminal1.TerminalLabel, out string firstPrefix, out _) &&
                TryParseLabel(element.Terminal2.TerminalLabel, out string secondPrefix, out _) &&
                firstPrefix == element.Prefix && secondPrefix == element.Prefix;
            if (alreadyAssigned)
                continue;

            HashSet<int> used = usedByPrefix[element.Prefix];
            int firstNumber = 1;
            while (used.Contains(firstNumber) || used.Contains(firstNumber + 1))
                firstNumber += 2;

            used.Add(firstNumber);
            used.Add(firstNumber + 1);
            element.AssignTerminalLabels(
                $"{element.Prefix}{firstNumber}",
                $"{element.Prefix}{firstNumber + 1}");
        }
    }

    private static void AssignEntityNames(IReadOnlyList<CircuitElement> elements)
    {
        Dictionary<CircuitElementKind, int> counts = new();
        foreach (CircuitElement element in elements)
        {
            counts.TryGetValue(element.Kind, out int count);
            count++;
            counts[element.Kind] = count;
            element.EntityName = $"{element.Kind}{count}";
        }
    }

    private static void ReserveLabel(string label, string expectedPrefix, ISet<int> used)
    {
        if (TryParseLabel(label, out string prefix, out int number) && prefix == expectedPrefix)
            used.Add(number);
    }

    private static bool TryParseLabel(string label, out string prefix, out int number)
    {
        prefix = string.Empty;
        number = int.MaxValue;
        if (string.IsNullOrWhiteSpace(label))
            return false;

        int split = label.Length;
        while (split > 0 && char.IsDigit(label[split - 1]))
            split--;
        if (split == label.Length || !int.TryParse(label.Substring(split), out number))
            return false;

        prefix = label.Substring(0, split).ToUpperInvariant();
        return prefix.Length > 0;
    }

    private static bool LabelEquals(string first, string second)
        => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);

    private static bool IsGroundLabel(string value)
        => LabelEquals(value, "GND") || value == "0";

    private sealed class TerminalLabelComparer : IComparer<string>
    {
        public static readonly TerminalLabelComparer Instance = new();

        public int Compare(string first, string second)
        {
            bool firstValid = TryParseLabel(first, out string firstPrefix, out int firstNumber);
            bool secondValid = TryParseLabel(second, out string secondPrefix, out int secondNumber);
            if (firstValid && secondValid)
            {
                int numberComparison = firstNumber.CompareTo(secondNumber);
                if (numberComparison != 0)
                    return numberComparison;

                int prefixComparison = string.Compare(firstPrefix, secondPrefix, StringComparison.Ordinal);
                if (prefixComparison != 0)
                    return prefixComparison;
            }

            if (firstValid != secondValid)
                return firstValid ? -1 : 1;
            return string.Compare(first, second, StringComparison.Ordinal);
        }
    }

    private sealed class DisjointSet<T> where T : class
    {
        private readonly Dictionary<T, T> parents = new();
        private readonly Dictionary<T, int> ranks = new();

        public DisjointSet(IEnumerable<T> values)
        {
            foreach (T value in values)
            {
                parents[value] = value;
                ranks[value] = 0;
            }
        }

        public bool Contains(T value) => value != null && parents.ContainsKey(value);

        public T Find(T value)
        {
            T parent = parents[value];
            if (!ReferenceEquals(parent, value))
                parents[value] = Find(parent);
            return parents[value];
        }

        public void Union(T first, T second)
        {
            T firstRoot = Find(first);
            T secondRoot = Find(second);
            if (ReferenceEquals(firstRoot, secondRoot))
                return;

            int firstRank = ranks[firstRoot];
            int secondRank = ranks[secondRoot];
            if (firstRank < secondRank)
                parents[firstRoot] = secondRoot;
            else if (firstRank > secondRank)
                parents[secondRoot] = firstRoot;
            else
            {
                parents[secondRoot] = firstRoot;
                ranks[firstRoot] = firstRank + 1;
            }
        }
    }
}
