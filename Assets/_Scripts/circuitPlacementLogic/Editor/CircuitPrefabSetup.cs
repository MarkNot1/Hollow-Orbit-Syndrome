using System;
using UnityEditor;
using UnityEngine;

public static class CircuitPrefabSetup
{
    private static readonly (string path, int componentId)[] ComponentPrefabs =
    {
        ("Assets/_Prefabs/SMD components/Resistor/Resistor.prefab", 0),
        ("Assets/_Prefabs/SMD components/Capacitor/Capacitor.prefab", 1),
        ("Assets/_Prefabs/SMD components/LED/LED.prefab", 2),
        ("Assets/_Prefabs/SMD components/Diode/Diode.prefab", 3),
        ("Assets/_Prefabs/SMD components/Battery/Battery.prefab", 4)
    };

    [MenuItem("Tools/Circuit/Configure Electrical Prefabs")]
    public static void ConfigureAll()
    {
        foreach ((string path, int componentId) in ComponentPrefabs)
            ConfigureComponentPrefab(path, componentId);

        // Replace the old camera-facing LineRenderer with the routed solid mesh.
        ConfigureWirePrefab("Assets/_Scripts/circuitPlacementLogic/CircuitWire.prefab");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Configured circuit electrical prefabs and cleaned CircuitWire serialization.");
    }

    private static void ConfigureComponentPrefab(string path, int componentId)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            CircuitElement element = root.GetComponent<CircuitElement>();
            if (element == null)
                element = root.AddComponent<CircuitElement>();

            element.ConfigureFromComponentId(componentId);
            element.Initialize(null);
            if (!element.HasValidTerminals)
                throw new InvalidOperationException($"{path} does not contain two CircuitNode children.");

            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ConfigureWirePrefab(string path)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            LineRenderer line = root.GetComponent<LineRenderer>();
            if (line != null)
                UnityEngine.Object.DestroyImmediate(line);

            MeshFilter filter = root.GetComponent<MeshFilter>();
            if (filter == null)
                filter = root.AddComponent<MeshFilter>();
            filter.sharedMesh = null;

            MeshRenderer renderer = root.GetComponent<MeshRenderer>();
            if (renderer == null)
                renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/_Scripts/circuitPlacementLogic/CircuitWireMaterial.mat");
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
