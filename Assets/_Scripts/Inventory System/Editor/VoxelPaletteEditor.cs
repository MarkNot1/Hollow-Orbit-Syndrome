using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>Rebuilds display assets from the same atlas/meshes used by the world.</summary>
public static class VoxelPaletteEditor
{
    public const string AssetFolder = "Assets/_Prefabs/InventoryPrefabs/Voxels";
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private static readonly VoxelType[] Types =
        { VoxelType.Dark_Ship_Metal, VoxelType.Infected_Growth, VoxelType.Ship_Metal, VoxelType.Metal,
            VoxelType.Striped_Ship_Metal, VoxelType.Window };

    [MenuItem("Tools/Voxels/Regenerate Inventory Visuals")]
    public static void Regenerate()
    {
        Directory.CreateDirectory(AssetFolder);
        AssetDatabase.Refresh();
        VoxelDataSO database = AssetDatabase.LoadAssetAtPath<VoxelDataSO>("Assets/_Scripts/World Generation/VoxelData.asset");
        Material material = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Textures/MainMaterial.mat");
        foreach (VoxelType type in Types)
        {
            string id = type.ToString();
            Mesh mesh = BuildCube(database, type);
            string meshPath = $"{AssetFolder}/{id}.asset";
            Mesh savedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (savedMesh == null) { savedMesh = mesh; AssetDatabase.CreateAsset(mesh, meshPath); }
            else
            {
                EditorUtility.CopySerialized(mesh, savedMesh);
                EditorUtility.SetDirty(savedMesh);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
            GameObject visual = new(id);
            visual.AddComponent<MeshFilter>().sharedMesh = savedMesh;
            visual.AddComponent<MeshRenderer>().sharedMaterial = material;
            try { SaveItem(id, id.Replace('_', ' '), visual, ItemPlacementKind.TerrainVoxel, type); }
            finally { UnityEngine.Object.DestroyImmediate(visual); }
        }

        SpecialPrefabDatabaseSO specials = AssetDatabase.LoadAssetAtPath<SpecialPrefabDatabaseSO>(
            "Assets/_Scripts/World Generation/SpecialPrefabDatabase.asset");
        if (!specials.TryGetPrefab("Circuit", out SpecialPrefabData circuit))
            throw new InvalidOperationException("Circuit is missing from the special prefab database.");
        GameObject circuitVisual = new("Circuit");
        try
        {
            CopyVisuals(circuit.Prefab.transform, circuitVisual.transform, true);
            NormalizeVisual(circuitVisual);
            SaveItem("Circuit", "Circuit", circuitVisual, ItemPlacementKind.SpecialPrefab, VoxelType.Nothing);
        }
        finally { UnityEngine.Object.DestroyImmediate(circuitVisual); }
        GenerateFrame(database, material, false);
        GenerateFrame(database, material, true);
        AssetDatabase.SaveAssets();
        Debug.Log("Regenerated voxel inventory visuals and frame blueprints.");
    }

    private static void GenerateFrame(VoxelDataSO database, Material material, bool master)
    {
        StructureData frame = FrameBlueprint.Create(master);
        string id = master ? "MasterFrame" : "MetalFrame";
        var cubes = frame.voxels.Select(v => v.type).Distinct().ToDictionary(type => type, type => BuildCube(database, type));
        List<CombineInstance> pieces = new();
        foreach (StructureVoxelData voxel in frame.voxels)
        {
            pieces.Add(new CombineInstance { mesh = cubes[voxel.type],
                transform = Matrix4x4.TRS(((Vector3)voxel.position - Vector3.one * 2.5f) / 6f, Quaternion.identity, Vector3.one / 6f) });
        }
        string blueprintPath = $"{AssetFolder}/{id}.json";
        File.WriteAllText(blueprintPath, JsonUtility.ToJson(frame, true));
        AssetDatabase.ImportAsset(blueprintPath, ImportAssetOptions.ForceSynchronousImport);
        Mesh mesh = new() { name = frame.name + " Display" };
        mesh.CombineMeshes(pieces.ToArray());
        foreach (Mesh cube in cubes.Values) UnityEngine.Object.DestroyImmediate(cube);
        string meshPath = $"{AssetFolder}/{id}.asset";
        Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (saved == null) { saved = mesh; AssetDatabase.CreateAsset(saved, meshPath); }
        else
        {
            EditorUtility.CopySerialized(mesh, saved);
            EditorUtility.SetDirty(saved);
            UnityEngine.Object.DestroyImmediate(mesh);
        }
        GameObject visual = new(frame.name);
        visual.AddComponent<MeshFilter>().sharedMesh = saved;
        visual.AddComponent<MeshRenderer>().sharedMaterial = material;
        try { SaveItem(id, frame.name, visual, ItemPlacementKind.StructureBlueprint, VoxelType.Dark_Ship_Metal); }
        finally { UnityEngine.Object.DestroyImmediate(visual); }
        ItemSO item = AssetDatabase.LoadAssetAtPath<ItemSO>($"{AssetFolder}/{id}Item.asset");
        item.structureBlueprint = AssetDatabase.LoadAssetAtPath<TextAsset>(blueprintPath);
        item.description = master
            ? "Starts a structure at local (0, 0, 0). Its 6 x 6 x 6 metal border cannot be removed. Orange stripes mark the centre pair of each bottom edge. F5 saves; F9 loads."
            : "An editable 6 x 6 x 6 dark ship metal frame with open faces. Place on an existing structure to extend it. Use a Master Frame to start a structure.";
        EditorUtility.SetDirty(item);
    }

    public static Mesh BuildCube(VoxelDataSO database, VoxelType type)
    {
        TextureData texture = database.textureDataList.First(t => t.voxelType == type);
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tiledUvs = new List<Vector2>();
        var triangles = new List<int>();
        Vector3[] faces = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
        foreach (Vector3 normal in faces)
        {
            Vector3 u = Mathf.Abs(normal.y) > 0.5f ? Vector3.right : Vector3.Cross(Vector3.up, normal);
            Vector3 v = Vector3.Cross(normal, u);
            Vector2Int tile = normal.y > 0.5f ? texture.up : normal.y < -0.5f ? texture.down : texture.side;
            float minX = tile.x * database.textureSizeX + VoxelDataManager.textureOffset;
            float minY = tile.y * database.textureSizeY + VoxelDataManager.textureOffset;
            float maxX = (tile.x + 1) * database.textureSizeX - VoxelDataManager.textureOffset;
            float maxY = (tile.y + 1) * database.textureSizeY - VoxelDataManager.textureOffset;
            int start = vertices.Count;
            vertices.Add((normal - u - v) * 0.5f);
            vertices.Add((normal + u - v) * 0.5f);
            vertices.Add((normal + u + v) * 0.5f);
            vertices.Add((normal - u + v) * 0.5f);
            uvs.AddRange(new[] { new Vector2(minX, minY), new Vector2(maxX, minY), new Vector2(maxX, maxY), new Vector2(minX, maxY) });
            for (int i = 0; i < 4; i++) { normals.Add(normal); tiledUvs.Add(new Vector2(-1f, -1f)); }
            triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }
        Mesh mesh = new() { name = type + " Display Cube" };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, tiledUvs);
        mesh.SetColors(Enumerable.Repeat(Color.white, vertices.Count).ToArray());
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void CopyVisuals(Transform source, Transform parent, bool root = false)
    {
        GameObject copy = new(source.name);
        copy.transform.SetParent(parent, false);
        copy.transform.localPosition = root ? Vector3.zero : source.localPosition;
        copy.transform.localRotation = source.localRotation;
        copy.transform.localScale = source.localScale;
        MeshFilter filter = source.GetComponent<MeshFilter>();
        MeshRenderer renderer = source.GetComponent<MeshRenderer>();
        if (filter != null && renderer != null)
        {
            copy.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            copy.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
        }
        foreach (Transform child in source) CopyVisuals(child, copy.transform);
    }

    private static void NormalizeVisual(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) throw new InvalidOperationException("Voxel display has no renderers.");
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
        float scale = 1f / Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        foreach (Transform child in root.transform)
        {
            child.localPosition = (child.localPosition - bounds.center) * scale;
            child.localScale *= scale;
        }
    }

    private static void SaveItem(string id, string displayName, GameObject visual, ItemPlacementKind kind, VoxelType type)
    {
        string iconPath = $"{AssetFolder}/{id}.png";
        RenderIcon(visual, iconPath);
        var importer = (TextureImporter)AssetImporter.GetAtPath(iconPath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();

        // Preserve the existing hand anchor, which sits 0.5 units above the camera.
        GameObject held = new(id + " Held Voxel");
        visual.transform.SetParent(held.transform, false);
        visual.transform.localPosition = new Vector3(0.3f, -0.73f, 0.65f);
        visual.transform.localRotation = Quaternion.Euler(8f, -25f, 0f);
        visual.transform.localScale = Vector3.one * 0.27f;
        foreach (Renderer renderer in held.GetComponentsInChildren<Renderer>())
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
        GameObject prefab;
        try { prefab = PrefabUtility.SaveAsPrefabAsset(held, $"{AssetFolder}/{id}Hand.prefab"); }
        finally { visual.transform.SetParent(null); UnityEngine.Object.DestroyImmediate(held); }

        string path = $"{AssetFolder}/{id}Item.asset";
        ItemSO item = AssetDatabase.LoadAssetAtPath<ItemSO>(path);
        if (item == null) { item = ScriptableObject.CreateInstance<ItemSO>(); AssetDatabase.CreateAsset(item, path); }
        item.itemName = displayName;
        item.itemIcon = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
        item.handItemPrefab = prefab;
        item.itemPrefab = null;
        item.placementKind = kind;
        item.voxelType = type;
        item.specialPrefabId = kind == ItemPlacementKind.SpecialPrefab ? id : "";
        item.unlimited = true;
        item.description = kind == ItemPlacementKind.SpecialPrefab
            ? "A surface-mounted circuit. Place on a voxel face, then press F to inspect it."
            : "Unlimited building voxel. Equip it and right-click a surface to place it.";
        EditorUtility.SetDirty(item);
    }

    private static void RenderIcon(GameObject visual, string path)
    {
        var preview = new PreviewRenderUtility();
        Texture2D texture = null;
        RenderTexture previousTarget = RenderTexture.active;
        try
        {
            preview.AddSingleGO(UnityEngine.Object.Instantiate(visual));
            preview.camera.orthographic = true;
            preview.camera.allowHDR = false;
            preview.camera.orthographicSize = 0.92f;
            preview.camera.transform.rotation = Quaternion.Euler(30f, 45f, 0f);
            preview.camera.transform.position = -preview.camera.transform.forward * 4f;
            preview.camera.nearClipPlane = 0.01f;
            preview.camera.farClipPlane = 10f;
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.backgroundColor = Color.clear;
            preview.ambientColor = new Color(0.35f, 0.35f, 0.35f);
            preview.lights[0].transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            preview.lights[0].intensity = 1.2f;
            preview.lights[1].intensity = 0.35f;
            float size = 128f / EditorGUIUtility.pixelsPerPoint;
            preview.BeginPreview(new Rect(0, 0, size, size), GUIStyle.none);
            preview.Render(true);
            RenderTexture.active = (RenderTexture)preview.EndPreview();
            // EndStaticPreview returns RGB24 and discards alpha. Read RGBA instead.
            texture = new Texture2D(128, 128, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, 128, 128), 0, 0);
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
            {
                Color[] pixels = texture.GetPixels();
                for (int i = 0; i < pixels.Length; i++) pixels[i] = pixels[i].gamma;
                texture.SetPixels(pixels);
            }
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previousTarget;
            if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            preview.Cleanup();
        }
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
    }

    [MenuItem("Tools/Voxels/Configure Sample Scene Palette")]
    public static void ConfigureScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            Inventory inventory = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<Inventory>(true)).Single();
            var serialized = new SerializedObject(inventory);
            SerializedProperty entries = serialized.FindProperty("items");
            SerializedProperty slots = serialized.FindProperty("hotbarItems");
            string[] names = Types.Take(4).Select(t => t.ToString())
                .Concat(new[] { "Circuit", "MasterFrame", "MetalFrame" })
                .Concat(Types.Skip(4).Select(t => t.ToString())).ToArray();
            slots.arraySize = 6;
            for (int i = 0; i < names.Length; i++)
            {
                ItemSO item = AssetDatabase.LoadAssetAtPath<ItemSO>($"{AssetFolder}/{names[i]}Item.asset");
                if (item == null) throw new InvalidOperationException("Regenerate inventory visuals first.");
                bool exists = false;
                for (int j = 0; j < entries.arraySize; j++)
                    exists |= entries.GetArrayElementAtIndex(j).FindPropertyRelative("item").objectReferenceValue == item;
                if (!exists)
                {
                    entries.InsertArrayElementAtIndex(entries.arraySize);
                    SerializedProperty entry = entries.GetArrayElementAtIndex(entries.arraySize - 1);
                    entry.FindPropertyRelative("item").objectReferenceValue = item;
                    entry.FindPropertyRelative("amount").intValue = 1;
                }
                if (i < slots.arraySize) slots.GetArrayElementAtIndex(i).objectReferenceValue = item;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            StructureSystem structures = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<StructureSystem>(true)).FirstOrDefault();
            if (structures == null)
            {
                GameObject root = new("Structures");
                SceneManager.MoveGameObjectToScene(root, scene);
                structures = root.AddComponent<StructureSystem>();
            }
            SerializedObject structureSettings = new(structures);
            structureSettings.FindProperty("voxelMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Textures/MainMaterial.mat");
            structureSettings.FindProperty("prefabDatabase").objectReferenceValue = AssetDatabase.LoadAssetAtPath<SpecialPrefabDatabaseSO>("Assets/_Scripts/World Generation/SpecialPrefabDatabase.asset");
            structureSettings.FindProperty("framePreviewMaterial").objectReferenceValue = GetFramePreviewMaterial();
            structureSettings.ApplyModifiedPropertiesWithoutUndo();
            foreach (TMP_Text text in inventory.container.GetComponentsInChildren<TMP_Text>(true))
                if (text.text.StartsWith("TAB / ESC"))
                {
                    text.text = "TAB / ESC  Close     1-6  Assign     ENTER  Equip     Q  Drop\nPlaying: RMB  Place     LMB  Remove     F5  Save structures     F9  Load";
                    text.fontSize = 12f;
                    text.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 32f);
                }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
    }

    public static void GenerateAndConfigure()
    {
        Regenerate();
        ConfigureScene();
    }

    private static Material GetFramePreviewMaterial()
    {
        const string path = "Assets/_Textures/FramePreview.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        material.SetFloat("_Surface", 1);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)RenderQueue.Transparent;
        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}
