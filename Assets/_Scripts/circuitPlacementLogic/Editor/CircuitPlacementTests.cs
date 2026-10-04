using System.Collections.Generic;
using System.Reflection;
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
            blockedCells.Contains, null, null, out List<Vector2Int> path);

        Assert.That(found, Is.True);
        Assert.That(path.Exists(blockedCells.Contains), Is.False);
    }

    [Test]
    public void WireRouterAvoidsOccupiedWireCellsWhenAnotherRouteExists()
    {
        HashSet<Vector2Int> occupiedWireCells = new()
        {
            new(2, 1), new(3, 1)
        };

        bool found = CircuitWireRouter.TryFindPath(
            6, 3, new Vector2Int(1, 1), new Vector2Int(4, 1),
            _ => false,
            null,
            cell => occupiedWireCells.Contains(cell) ? 1000 : 0,
            out List<Vector2Int> path);

        Assert.That(found, Is.True);
        Assert.That(path.Exists(occupiedWireCells.Contains), Is.False);
    }

    [Test]
    public void WireRouterAllowsOverlapWhenNoAlternativeExists()
    {
        Vector2Int unavoidableWireCell = new(2, 0);

        bool found = CircuitWireRouter.TryFindPath(
            4, 1, new Vector2Int(1, 0), new Vector2Int(3, 0),
            _ => false,
            null,
            cell => cell == unavoidableWireCell ? 1000 : 0,
            out List<Vector2Int> path);

        Assert.That(found, Is.True);
        Assert.That(path, Does.Contain(unavoidableWireCell));
    }

    [Test]
    public void InlineWireRequiresStraightCardinalPassThrough()
    {
        static bool Matches(params Vector2Int[] route)
        {
            return CircuitWireRouter.TryFindStraightPassThrough(
                route, new Vector3Int(2, 2, 0), Vector2Int.one, out _, out _, out _);
        }

        Assert.That(Matches(new(1, 2), new(2, 2), new(3, 2)), Is.True);
        Assert.That(Matches(new(1, 2), new(2, 2), new(2, 3)), Is.False);
        Assert.That(Matches(new(1, 1), new(2, 2), new(3, 3)), Is.False);
        Assert.That(Matches(new(1, 2), new(2, 2)), Is.False);
    }

    [Test]
    public void CircuitBoundsStayAnchoredToTileGrid()
    {
        GameObject circuitObject = new("Circuit Bounds Test");
        createdObjects.Add(circuitObject);
        SpecialPrefabInteractable circuit =
            circuitObject.AddComponent<SpecialPrefabInteractable>();

        GameObject tileGrid = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tileGrid.name = "TileGrid";
        tileGrid.transform.SetParent(circuitObject.transform, false);

        GameObject distantVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        distantVisual.name = "Transient Circuit Visual";
        distantVisual.transform.SetParent(circuitObject.transform, false);
        distantVisual.transform.localPosition = Vector3.one * 1000f;

        Bounds bounds = circuit.GetWorldBounds();

        Assert.That(bounds.center, Is.EqualTo(tileGrid.GetComponent<Renderer>().bounds.center));
        Assert.That(bounds.size, Is.EqualTo(tileGrid.GetComponent<Renderer>().bounds.size));
    }

    [Test]
    public void ClockwiseRotationSwapsRectangularFootprint()
    {
        MethodInfo rotateFootprint = typeof(ObjectPlacer).GetMethod(
            "RotateFootprintClockwise",
            BindingFlags.Static | BindingFlags.NonPublic);

        Assert.That(rotateFootprint, Is.Not.Null);
        Vector2Int original = new(1, 2);
        Vector2Int once = (Vector2Int)rotateFootprint.Invoke(null, new object[] { original });
        Vector2Int twice = (Vector2Int)rotateFootprint.Invoke(null, new object[] { once });

        Assert.That(once, Is.EqualTo(new Vector2Int(2, 1)));
        Assert.That(twice, Is.EqualTo(original));
    }

    [TestCase(0f, 1f, 0f, 0f, 0f, 1f)]
    [TestCase(0f, -1f, 0f, 0f, 0f, 1f)]
    [TestCase(0f, 0f, 1f, 0f, 1f, 0f)]
    [TestCase(1f, 0f, 0f, 0f, 1f, 0f)]
    public void ClockwiseRotationUsesBoardNormal(
        float normalX,
        float normalY,
        float normalZ,
        float forwardX,
        float forwardY,
        float forwardZ)
    {
        Vector3 normal = new(normalX, normalY, normalZ);
        Vector3 forward = new(forwardX, forwardY, forwardZ);
        Vector3 right = Vector3.Cross(normal, forward).normalized;
        CircuitBoardContext board = new(
            null,
            null,
            default,
            default,
            right,
            forward,
            normal,
            1f,
            1f,
            10,
            10,
            0f);

        GameObject systems = new("Object Placer Rotation Test");
        createdObjects.Add(systems);
        ObjectPlacer placer = systems.AddComponent<ObjectPlacer>();
        SetPrivateField(placer, "activeBoard", board);
        SetPrivateField(placer, "rotationQuarterTurns", 1);

        Quaternion rotation = InvokePrivate<Quaternion>(placer, "GetPlacementRotation");

        Assert.That(Vector3.Angle(rotation * Vector3.up, normal), Is.LessThan(0.001f));
        Assert.That(Vector3.Angle(rotation * Vector3.forward, right), Is.LessThan(0.001f));

        SetPrivateField(placer, "rotationQuarterTurns", 4);
        Quaternion wrappedRotation = InvokePrivate<Quaternion>(placer, "GetPlacementRotation");
        Quaternion boardRotation = Quaternion.LookRotation(forward, normal);
        Assert.That(Quaternion.Angle(wrappedRotation, boardRotation), Is.LessThan(0.001f));
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
            Assert.That(placement.ActiveBoard.GridWidth, Is.EqualTo(10));
            Assert.That(placement.ActiveBoard.GridHeight, Is.EqualTo(10));
            Assert.That(placement.ActiveBoard.CellWorldWidth, Is.EqualTo(0.1f).Within(0.0001f));
            Assert.That(placement.ActiveBoard.CellWorldHeight, Is.EqualTo(0.1f).Within(0.0001f));
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

    [Test]
    public void PortableSnapshotValidationUsesGridStateNotWorldTransform()
    {
        GameObject systems = new("Puzzle Validation Systems");
        createdObjects.Add(systems);
        ObjectPlacer placer = systems.AddComponent<ObjectPlacer>();
        componentDatabaseSO database = AssetDatabase.LoadAssetAtPath<componentDatabaseSO>(
            "Assets/_Scripts/circuitPlacementLogic/ComponentDatabase.asset");
        AssignReference(placer, "componentDatabase", database);

        CircuitPuzzleSnapshotSO snapshot =
            ScriptableObject.CreateInstance<CircuitPuzzleSnapshotSO>();
        try
        {
            snapshot.ReplaceData(
                "Portable",
                10,
                10,
                new List<PuzzleComponentRecord>
                {
                    new()
                    {
                        recordId = 1,
                        componentId = 0,
                        gridPosition = new Vector3Int(2, 3, 0),
                        footprint = Vector2Int.one,
                        electricalValue = 1000d,
                        state = CircuitComponentState.Broken,
                        failureReason = "Puzzle fault"
                    }
                },
                new List<PuzzleOpenSlotRecord>(),
                new List<PuzzleWireRecord>());

            CircuitBoardContext firstBoard = CreateLogicalBoard(Vector3.zero);
            CircuitBoardContext movedBoard = CreateLogicalBoard(new Vector3(100f, 50f, -25f));

            Assert.That(
                CircuitPuzzleSystem.Validate(snapshot, firstBoard, placer, out string firstError),
                Is.True,
                firstError);
            Assert.That(
                CircuitPuzzleSystem.Validate(snapshot, movedBoard, placer, out string movedError),
                Is.True,
                movedError);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(snapshot);
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

    private CircuitBoardContext CreateLogicalBoard(Vector3 position)
    {
        SpecialPrefabInteractable circuit = CreateCircuitBoard();
        circuit.transform.position = position;
        Transform tileGrid = circuit.transform.Find("TileGrid");
        return new CircuitBoardContext(
            circuit,
            tileGrid,
            tileGrid.GetComponent<MeshFilter>().sharedMesh.bounds,
            new Plane(tileGrid.up, tileGrid.position),
            tileGrid.right,
            tileGrid.forward,
            tileGrid.up,
            0.1f,
            0.1f,
            10,
            10,
            0.002f);
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
            BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(target, null);

    private static T InvokePrivate<T>(object target, string methodName)
        => (T)target.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(target, null);

    private static void SetPrivateField(object target, string fieldName, object value)
        => target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(target, value);

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
