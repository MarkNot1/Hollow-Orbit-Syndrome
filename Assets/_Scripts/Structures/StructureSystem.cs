using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public sealed partial class StructureSystem : MonoBehaviour
{
    [SerializeField] private Material voxelMaterial;
    [SerializeField] private SpecialPrefabDatabaseSO prefabDatabase;
    [SerializeField] private Material framePreviewMaterial;
    [SerializeField] private World world;
    private readonly List<(StructureData data, Bounds bounds)> unloaded = new();
    private readonly List<VoxelStructure> loaded = new();
    private static readonly Collider[] Overlaps = new Collider[64];
    private TextAsset cachedBlueprint;
    private StructureData blueprintData;
    private FramePlacementPreview preview;
    private string status;
    private float statusUntil;
    public string SavePath => Path.Combine(Application.persistentDataPath, "Structures", "structures.json");

    private void OnEnable() => SetStreamingWorld(world != null ? world : FindFirstObjectByType<World>());

    public void SetStreamingWorld(World value)
    {
        if (world != null) world.OnChunkVisibilityChanged -= RefreshStreaming;
        world = value;
        if (world != null) world.OnChunkVisibilityChanged += RefreshStreaming;
        RefreshStreaming();
    }

    public void RefreshStreaming()
    {
        if (world == null || world.worldData.chunkDictionary == null) return;
        GetComponentsInChildren(false, loaded);
        foreach (VoxelStructure ship in loaded)
        {
            if (world.IsAreaRendered(ship.WorldBounds())) continue;
            try
            {
                StructureData data = ship.Capture(false);
                unloaded.Add((data, data.WorldBounds()));
                ship.Unload();
            }
            catch (Exception error) { ShowStatus("Ship unload failed: " + error.Message); }
        }
        loaded.Clear();
        for (int i = unloaded.Count - 1; i >= 0; i--)
        {
            var record = unloaded[i];
            if (!world.IsAreaRendered(record.bounds)) continue;
            GameObject root = new(record.data.name);
            root.SetActive(false);
            root.transform.SetParent(transform, false);
            VoxelStructure ship = root.AddComponent<VoxelStructure>();
            try
            {
                ship.Configure(voxelMaterial, prefabDatabase);
                ship.Restore(record.data);
                root.SetActive(true);
                unloaded.RemoveAt(i);
            }
            catch (Exception error)
            {
                ship.Unload();
                ShowStatus("Ship reload failed: " + error.Message);
            }
        }
    }

    public VoxelStructure CreateStructure(string structureName, Vector3 position, Quaternion rotation)
    {
        GameObject root = new(structureName);
        root.transform.SetParent(transform, false);
        root.transform.SetPositionAndRotation(position, rotation);
        VoxelStructure structure = root.AddComponent<VoxelStructure>();
        structure.Configure(voxelMaterial, prefabDatabase);
        return structure;
    }

    public bool TryPlaceBlueprint(RaycastHit hit, TextAsset blueprint)
    {
        if (!TryGetBlueprintPlacement(hit, blueprint, out BlueprintPlacement placement)) return false;
        if (!placement.canPlace) { ShowStatus("The frame needs clear space and a compatible surface."); return false; }
        StructureData data = blueprintData;
        VoxelStructure structure = placement.structure;
        Vector3Int offset = placement.offset;
        if (structure == null)
        {
            structure = CreateStructure(data.name, placement.position, placement.rotation);
            offset = Vector3Int.zero;
        }
        foreach (var voxel in data.voxels) structure.SetVoxel(voxel.position + offset, voxel.type);
        if (data.hasMasterFrame) structure.ProtectMasterFrame();
        structure.RebuildMeshes();
        return true;
    }

    public bool TryGetBlueprintPlacement(RaycastHit hit, TextAsset blueprint, out BlueprintPlacement placement)
    {
        placement = default;
        if (blueprint == null || hit.collider == null) return false;
        if (cachedBlueprint != blueprint)
        {
            cachedBlueprint = blueprint;
            blueprintData = null;
            try
            {
                StructureData data = JsonUtility.FromJson<StructureData>(blueprint.text);
                Validate(data, prefabDatabase);
                if (data.voxels.Count == 0 || data.prefabs.Count != 0)
                    throw new InvalidDataException("This voxel blueprint must contain voxels only.");
                blueprintData = data;
            }
            catch (Exception error) { ShowStatus(error.Message); }
        }
        if (blueprintData == null) return false;
        return TryGetPlacement(hit, blueprintData, false, out placement);
    }

    private bool TryGetPlacement(RaycastHit hit, StructureData data, bool copy, out BlueprintPlacement placement)
    {
        placement = default;
        if (hit.collider == null || data == null || data.voxels.Count == 0) return false;
        VoxelStructure structure = hit.collider.GetComponent<StructureChunk>()?.Structure;
        Vector3Int surface, normal, destination;
        bool destinationValid = true;
        Physics.SyncTransforms();
        if (structure == null)
        {
            ChunkRenderer chunk = hit.collider.GetComponent<ChunkRenderer>();
            if (chunk == null || chunk.ChunkData?.worldReference == null) return false;
            destinationValid = chunk.ChunkData.worldReference.TryGetVoxelPlacementCell(hit, out _);
            destination = VoxelMetrics.WorldHitToAdjacentVoxelIndex(hit);
            normal = VoxelMetrics.FaceNormal(hit.normal);
        }
        else
        {
            if (!structure.TryGetHitCell(hit, out surface, out normal)) return false;
            destination = surface + normal;
        }

        Vector3Int min = copy ? shipMin : data.voxels[0].position, max = copy ? shipMax : min;
        if (!copy) foreach (var voxel in data.voxels)
        {
            min = Vector3Int.Min(min, voxel.position);
            max = Vector3Int.Max(max, voxel.position);
        }
        Vector3Int offset = destination;
        for (int axis = 0; axis < 3; axis++)
            offset[axis] -= normal[axis] < 0 ? max[axis] : min[axis];
        Vector3 position = (Vector3)offset * VoxelMetrics.Size;
        Quaternion rotation = Quaternion.identity;
        if (structure != null)
        {
            position = structure.transform.TransformPoint(position);
            rotation = structure.transform.rotation;
        }
        bool valid = destinationValid && (copy || (structure == null) == data.hasMasterFrame);
        bool clear = copy && valid && IsSpaceFree(position + rotation * ((Vector3)(min + max) * (VoxelMetrics.Size * .5f)),
            rotation, (Vector3)(max - min + Vector3Int.one) * (VoxelMetrics.Size * .5f) - Vector3.one * .005f);
        foreach (var voxel in data.voxels)
        {
            if (!valid || clear) break;
            valid = structure != null && !copy ? structure.CanOccupy(voxel.position + offset) :
                IsSpaceFree(position + rotation * ((Vector3)voxel.position * VoxelMetrics.Size), rotation,
                    Vector3.one * (VoxelMetrics.Size * 0.5f - 0.005f));
        }
        placement = new BlueprintPlacement { structure = structure, offset = offset,
            position = position, rotation = rotation, canPlace = valid };
        return true;
    }

    public void ShowBlueprintPreview(RaycastHit hit, TextAsset blueprint)
    {
        if (framePreviewMaterial == null || !TryGetBlueprintPlacement(hit, blueprint, out BlueprintPlacement placement))
        { HideBlueprintPreview(); return; }
        preview ??= new FramePlacementPreview(framePreviewMaterial);
        preview.Show(blueprintData, placement.position, placement.rotation, placement.canPlace);
    }

    public void HideBlueprintPreview() => preview?.Hide();
    private void OnDisable()
    {
        HideBlueprintPreview();
        if (world != null) world.OnChunkVisibilityChanged -= RefreshStreaming;
    }
    private void OnDestroy() => preview?.Dispose();

    public string CaptureJson()
    {
        StructureSaveData save = new();
        foreach (VoxelStructure structure in GetComponentsInChildren<VoxelStructure>())
            if (structure.HasMasterFrame) save.structures.Add(structure.Capture());
        foreach (var record in unloaded)
            if (record.data.hasMasterFrame) save.structures.Add(record.data.ConnectedCopy(out _));
        save.structures.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
        return JsonUtility.ToJson(save, true);
    }

    public void RestoreJson(string json)
    {
        StructureSaveData save = JsonUtility.FromJson<StructureSaveData>(json);
        if (save == null || save.version != 1 || save.structures == null)
            throw new InvalidDataException("Unsupported structure save format.");
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (StructureData data in save.structures)
        {
            Validate(data, prefabDatabase);
            if (string.IsNullOrWhiteSpace(data.id) || !ids.Add(data.id))
                throw new InvalidDataException("Structure IDs must be present and unique.");
        }
        VoxelStructure[] previous = GetComponentsInChildren<VoxelStructure>();
        var distant = new List<(StructureData data, Bounds bounds)>();
        GameObject staging = new("Loading structures");
        staging.transform.SetParent(transform, false);
        staging.SetActive(false);
        try
        {
            foreach (StructureData data in save.structures)
            {
                Bounds bounds = data.WorldBounds();
                if (world != null && !world.IsAreaRendered(bounds))
                {
                    distant.Add((data, bounds));
                    continue;
                }
                GameObject root = new(data.name);
                root.transform.SetParent(staging.transform, false);
                VoxelStructure structure = root.AddComponent<VoxelStructure>();
                structure.Configure(voxelMaterial, prefabDatabase);
                structure.Restore(data);
            }
            // Commit after nearby structures load successfully; distant ships remain data only.
            unloaded.Clear();
            unloaded.AddRange(distant);
            foreach (VoxelStructure old in previous)
                old.Unload();
            while (staging.transform.childCount > 0)
                staging.transform.GetChild(0).SetParent(transform, true);
        }
        finally
        {
            foreach (VoxelStructure ship in staging.GetComponentsInChildren<VoxelStructure>(true)) ship.Unload();
            VoxelStructure.Release(staging);
        }
    }

    public void SaveToFile(string path)
    {
        string json = CaptureJson();
        var ships = ReadSavedShips(path);
        foreach (StructureData ship in JsonUtility.FromJson<StructureSaveData>(json).structures)
        {
            int index = ships.FindIndex(s => s.id == ship.id);
            if (index < 0) ships.Add(ship); else ships[index] = ship;
        }
        WriteSnapshot(path + ".library", JsonUtility.ToJson(new StructureSaveData { structures = ships }, true));
        WriteSnapshot(path, json);
        if (SelectedShip != null) SelectSavedShip(ships.Find(s => s.id == SelectedShip.id));
    }

    private static void WriteSnapshot(string path, string json)
    {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
        string temporary = fullPath + ".tmp";
        try
        {
            File.WriteAllText(temporary, json);
            if (File.Exists(fullPath)) File.Replace(temporary, fullPath, fullPath + ".bak");
            else File.Move(temporary, fullPath);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void LoadFromFile(string path) => RestoreJson(File.ReadAllText(path));

    public void SaveStructures()
    {
        try { SaveToFile(SavePath); ShowStatus("Structures saved.  F9 to load"); }
        catch (Exception error) { ShowStatus("Save failed: " + error.Message); }
    }

    public void LoadStructures()
    {
        try { LoadFromFile(SavePath); ShowStatus("Structures loaded."); }
        catch (Exception error) { ShowStatus("Load failed: " + error.Message); }
    }

    private void ShowStatus(string message)
    {
        status = message;
        statusUntil = Time.unscaledTime + 5f;
    }

    private void OnGUI()
    {
        if (Time.unscaledTime < statusUntil)
            GUI.Box(new Rect(Screen.width * 0.5f - 240f, 24f, 480f, 48f), status);
    }

    internal static bool IsSpaceFree(Vector3 center, Quaternion rotation, Vector3 halfSize,
        VoxelStructure ignoreVoxels = null, Transform ignorePrefab = null)
    {
        int count = Physics.OverlapBoxNonAlloc(center, halfSize, Overlaps, rotation,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        if (count == Overlaps.Length) return false;
        for (int i = 0; i < count; i++)
        {
            if (ignorePrefab != null && Overlaps[i].transform.IsChildOf(ignorePrefab)) continue;
            StructureChunk chunk = Overlaps[i].GetComponent<StructureChunk>();
            if (ignoreVoxels != null && chunk != null && chunk.Structure == ignoreVoxels) continue;
            return false;
        }
        return true;
    }

    public static void Validate(StructureData data, SpecialPrefabDatabaseSO prefabs)
    {
        if (data == null || data.version != 1 || data.voxels == null || data.prefabs == null)
            throw new InvalidDataException("Unsupported structure format.");
        if (!Finite(data.position) || !ValidRotation(data.rotation))
            throw new InvalidDataException("Invalid structure transform.");
        HashSet<Vector3Int> occupied = new();
        foreach (var voxel in data.voxels)
            if (!VoxelRuntimeData.IsSolid(voxel.type) || !occupied.Add(voxel.position) ||
                Math.Abs((long)voxel.position.x) > 1000000 || Math.Abs((long)voxel.position.y) > 1000000 ||
                Math.Abs((long)voxel.position.z) > 1000000)
                throw new InvalidDataException("Invalid or duplicate voxel.");
        if (data.hasMasterFrame)
        {
            int frameVoxels = 0;
            foreach (var voxel in data.voxels)
            {
                if (!FrameBlueprint.ContainsEdge(voxel.position)) continue;
                if (voxel.type != FrameBlueprint.MasterVoxelType(voxel.position))
                    throw new InvalidDataException("Master Frame materials do not match the blueprint.");
                frameVoxels++;
            }
            if (frameVoxels != 56) throw new InvalidDataException("The Master Frame border is incomplete.");
        }
        HashSet<string> ids = new(StringComparer.Ordinal);
        HashSet<(Vector3Int, Vector3Int)> faces = new();
        foreach (StructurePrefabData prefab in data.prefabs)
        {
            if (prefab == null || string.IsNullOrWhiteSpace(prefab.id) || !ids.Add(prefab.id) ||
                prefabs == null || !prefabs.TryGetPrefab(prefab.prefabId, out SpecialPrefabData definition) || definition.Prefab == null)
                throw new InvalidDataException("Missing, duplicate, or unknown special prefab.");
            if (!occupied.Contains(prefab.surfaceVoxel) || !Array.Exists(VoxelStructure.FaceNormals, n => n == prefab.normal) ||
                !faces.Add((prefab.surfaceVoxel, prefab.normal)) || !Finite(prefab.position) || !ValidRotation(prefab.rotation) ||
                !Finite(prefab.scale) || prefab.scale.x <= 0 || prefab.scale.y <= 0 || prefab.scale.z <= 0)
                throw new InvalidDataException("Invalid special prefab attachment or transform.");
        }
    }

    private static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    private static bool ValidRotation(Quaternion q) => float.IsFinite(q.x) && float.IsFinite(q.y) &&
        float.IsFinite(q.z) && float.IsFinite(q.w) && Mathf.Abs(Quaternion.Dot(q, q) - 1f) < 0.01f;
}
