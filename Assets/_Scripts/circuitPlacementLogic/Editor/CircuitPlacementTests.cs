using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class CircuitPlacementTests
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
    public void RouterNeverCrossesBlockedComponentFootprint()
    {
        HashSet<Vector2Int> blockedCells = new()
        {
            new(2, 1), new(2, 2), new(3, 1), new(3, 2)
        };

        bool found = CircuitWireRouter.TryFindPath(
            6, 4, new Vector2Int(1, 1), new Vector2Int(4, 1),
            blockedCells.Contains, null, out List<Vector2Int> path);

        Assert.That(found, Is.True);
        Assert.That(path.Exists(blockedCells.Contains), Is.False);
    }

    [Test]
    public void PlacementPublishesChangesAndSimulationCoalescesRebuilds()
    {
        GameObject systems = new("Circuit Systems");
        createdObjects.Add(systems);
        systems.SetActive(false);

        placementInputManager input = systems.AddComponent<placementInputManager>();
        ObjectPlacer placer = systems.AddComponent<ObjectPlacer>();
        CircuitWireSystem wires = systems.AddComponent<CircuitWireSystem>();
        placementSystem placement = systems.AddComponent<placementSystem>();
        CircuitSimulationSystem simulation = systems.AddComponent<CircuitSimulationSystem>();
        AssignReference(placement, "inputManager", input);
        AssignReference(placement, "objectPlacer", placer);
        AssignReference(placement, "wireSystem", wires);
        AssignReference(simulation, "placementSystem", placement);

        SpecialPrefabInteractable circuit = CreateCircuitBoard();
        PlacedCircuitComponent battery = CreateComponent(4, Vector3Int.zero);
        battery.transform.SetParent(circuit.transform, false);

        int boardChanges = 0;
        int circuitChanges = 0;
        int simulationUpdates = 0;
        placement.ActiveBoardChanged += _ => boardChanges++;
        placement.CircuitChanged += () => circuitChanges++;
        simulation.SimulationUpdated += _ => simulationUpdates++;

        InvokePrivate(placement, "OnEnable");
        InvokePrivate(simulation, "OnEnable");
        try
        {
            int initialUpdates = simulationUpdates;
            placement.SetActiveCircuit(circuit);
            battery.Element.TrySetValue(6d);
            battery.Element.TrySetValue(7d);
            InvokePrivate(simulation, "LateUpdate");

            Assert.That(placement.ActiveBoard, Is.Not.Null);
            Assert.That(boardChanges, Is.EqualTo(1));
            Assert.That(circuitChanges, Is.EqualTo(2));
            Assert.That(simulationUpdates - initialUpdates, Is.EqualTo(1));
            Assert.That(systems.GetComponents<CircuitSimulationSystem>(), Has.Length.EqualTo(1));
            Assert.That(systems.GetComponent<CircuitComponentHoverDisplay>(), Is.Null);

            placement.ClearActiveCircuit();
            Assert.That(placement.ActiveBoard, Is.Null);
            Assert.That(boardChanges, Is.EqualTo(2));
            Assert.That(simulation.CurrentResult.Success, Is.False);
        }
        finally
        {
            InvokePrivate(simulation, "OnDisable");
            InvokePrivate(placement, "OnDisable");
        }
    }

    private SpecialPrefabInteractable CreateCircuitBoard()
    {
        GameObject circuitObject = new("Circuit");
        createdObjects.Add(circuitObject);
        SpecialPrefabInteractable circuit = circuitObject.AddComponent<SpecialPrefabInteractable>();
        circuit.Configure("Circuit", "Circuit", Vector3Int.zero, Vector3Int.up,
            Vector3Int.one, SpecialPrefabInteractionType.Inspect);

        GameObject tileGrid = new("TileGrid", typeof(MeshFilter), typeof(MeshRenderer));
        tileGrid.transform.SetParent(circuitObject.transform, false);
        Mesh mesh = new()
        {
            vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),
                new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f)
            }
        };
        mesh.RecalculateBounds();
        tileGrid.GetComponent<MeshFilter>().sharedMesh = mesh;
        return circuit;
    }

    private static void AssignReference(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
    {
        SerializedObject serializedObject = new(target);
        serializedObject.FindProperty(propertyName).objectReferenceValue = value;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void InvokePrivate(object target, string methodName)
        => target.GetType().GetMethod(
            methodName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Invoke(target, null);

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

    private static void CreateNode(Transform parent, string nodeName)
    {
        GameObject node = new(nodeName);
        node.transform.SetParent(parent, false);
        node.AddComponent<CircuitNode>();
    }

}
