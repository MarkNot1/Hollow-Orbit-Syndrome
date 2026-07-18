#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CircuitWireSetupEditor
{
    private const string SessionSetupKey = "HollowOrbit.CircuitWireSetup";
    private const string NodeLayerName = "CircuitNode";
    private const string WireLayerName = "CircuitWire";
    private const string NodePrefabPath = "Assets/_Prefabs/SMD components/Node_1.prefab";
    private const string MaterialPath = "Assets/_Scripts/circuitPlacementLogic/CircuitWireMaterial.mat";
    private const string WirePrefabPath = "Assets/_Scripts/circuitPlacementLogic/CircuitWire.prefab";

    [InitializeOnLoadMethod]
    private static void ScheduleFirstSetup()
    {
        EditorApplication.delayCall += () =>
        {
            if (SessionState.GetBool(SessionSetupKey, false) ||
                EditorApplication.isPlayingOrWillChangePlaymode || !NeedsSetup())
                return;

            SessionState.SetBool(SessionSetupKey, true);
            SetupProject();
        };
    }

    [MenuItem("Tools/Circuit Wiring/Setup Project")]
    public static void SetupProject()
    {
        int nodeLayer = EnsureLayer(NodeLayerName);
        int wireLayer = EnsureLayer(WireLayerName);
        Material material = EnsureMaterial();
        GameObject wirePrefab = EnsureWirePrefab(material, wireLayer);
        ConfigureNodePrefab(nodeLayer);
        ConfigureOpenScene(wirePrefab, material, nodeLayer, wireLayer);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Circuit wiring setup complete. Save the open scene to keep its CircuitWireSystem assignment.");
    }

    private static bool NeedsSetup()
    {
        if (LayerMask.NameToLayer(NodeLayerName) < 0 || LayerMask.NameToLayer(WireLayerName) < 0 ||
            AssetDatabase.LoadAssetAtPath<Material>(MaterialPath) == null ||
            AssetDatabase.LoadAssetAtPath<GameObject>(WirePrefabPath) == null)
            return true;

        GameObject nodePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(NodePrefabPath);
        if (nodePrefab != null && nodePrefab.GetComponent<CircuitNode>() == null)
            return true;

        placementSystem placement = Object.FindFirstObjectByType<placementSystem>(FindObjectsInactive.Include);
        return placement != null && placement.GetComponent<CircuitWireSystem>() == null;
    }

    private static int EnsureLayer(string layerName)
    {
        int existing = LayerMask.NameToLayer(layerName);
        if (existing >= 0)
            return existing;

        SerializedObject tagManager = new(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");
        for (int i = 8; i < layers.arraySize; i++)
        {
            SerializedProperty layer = layers.GetArrayElementAtIndex(i);
            if (!string.IsNullOrEmpty(layer.stringValue))
                continue;

            layer.stringValue = layerName;
            tagManager.ApplyModifiedProperties();
            return i;
        }

        throw new System.InvalidOperationException($"No free Unity layer is available for '{layerName}'.");
    }

    private static Material EnsureMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material != null)
            return material;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        material = new Material(shader) { name = "CircuitWireMaterial" };
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", new Color(0.95f, 0.55f, 0.08f, 1f));
        AssetDatabase.CreateAsset(material, MaterialPath);
        return material;
    }

    private static GameObject EnsureWirePrefab(Material material, int wireLayer)
    {
        GameObject instance = new("CircuitWire", typeof(LineRenderer), typeof(WireConnection));
        instance.layer = wireLayer;
        LineRenderer line = instance.GetComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.useWorldSpace = true;
        line.alignment = LineAlignment.View;
        line.startWidth = 0.025f;
        line.endWidth = 0.025f;
        line.numCornerVertices = 0;
        line.numCapVertices = 0;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(instance, WirePrefabPath);
        Object.DestroyImmediate(instance);
        return prefab;
    }

    private static void ConfigureNodePrefab(int nodeLayer)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(NodePrefabPath);
        if (root == null)
        {
            Debug.LogWarning($"Node prefab was not found at {NodePrefabPath}. Nodes will be configured at runtime instead.");
            return;
        }

        root.layer = nodeLayer;
        if (root.GetComponent<CircuitNode>() == null)
            root.AddComponent<CircuitNode>();
        PrefabUtility.SaveAsPrefabAsset(root, NodePrefabPath);
        PrefabUtility.UnloadPrefabContents(root);
    }

    private static void ConfigureOpenScene(
        GameObject wirePrefab,
        Material material,
        int nodeLayer,
        int wireLayer)
    {
        placementSystem placement = Object.FindFirstObjectByType<placementSystem>(FindObjectsInactive.Include);
        ObjectPlacer objectPlacer = Object.FindFirstObjectByType<ObjectPlacer>(FindObjectsInactive.Include);
        if (placement == null)
        {
            Debug.LogWarning("No placementSystem exists in the open scene. Asset setup completed without scene assignment.");
            return;
        }

        CircuitWireSystem wireSystem = placement.GetComponent<CircuitWireSystem>();
        if (wireSystem == null)
            wireSystem = Undo.AddComponent<CircuitWireSystem>(placement.gameObject);

        SerializedObject wireSettings = new(wireSystem);
        wireSettings.FindProperty("wirePrefab").objectReferenceValue = wirePrefab;
        wireSettings.FindProperty("wireMaterial").objectReferenceValue = material;
        wireSettings.FindProperty("nodeLayerMask").intValue = 1 << nodeLayer;
        wireSettings.FindProperty("wireLayerMask").intValue = 1 << wireLayer;
        wireSettings.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject placementSettings = new(placement);
        placementSettings.FindProperty("wireSystem").objectReferenceValue = wireSystem;
        placementSettings.ApplyModifiedPropertiesWithoutUndo();

        if (objectPlacer != null)
        {
            SerializedObject placerSettings = new(objectPlacer);
            placerSettings.FindProperty("wireSystem").objectReferenceValue = wireSystem;
            placerSettings.ApplyModifiedPropertiesWithoutUndo();
        }

        EditorUtility.SetDirty(wireSystem);
        EditorUtility.SetDirty(placement);
        if (objectPlacer != null)
            EditorUtility.SetDirty(objectPlacer);
        EditorSceneManager.MarkSceneDirty(placement.gameObject.scene);
    }
}
#endif
