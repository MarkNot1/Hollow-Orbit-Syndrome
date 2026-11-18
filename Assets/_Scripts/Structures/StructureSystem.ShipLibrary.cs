using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public sealed partial class StructureSystem
{
    public const int MaxShipNameLength = 48;
    [SerializeField] private TextAsset builtInShipLibrary;
    private List<StructureData> builtInShips;
    public StructureData SelectedShip { get; private set; }
    private Bounds[] attachmentBounds = Array.Empty<Bounds>();
    private Vector3Int shipMin, shipMax;

    public IReadOnlyList<StructureData> ReadBuiltInShips() => builtInShips ??=
        builtInShipLibrary == null ? new List<StructureData>() : ParseShipLibrary(builtInShipLibrary.text);

    public List<StructureData> ReadSavedShips(string path = null)
    {
        path ??= SavePath;
        string library = path + ".library";
        string file = File.Exists(library) ? library : path;
        return File.Exists(file) ? ParseShipLibrary(File.ReadAllText(file)) : new List<StructureData>();
    }

    private List<StructureData> ParseShipLibrary(string json)
    {
        StructureSaveData saved = JsonUtility.FromJson<StructureSaveData>(json);
        if (saved == null || saved.version != 1 || saved.structures == null)
            throw new InvalidDataException("Unsupported ship library format.");
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (StructureData ship in saved.structures)
        {
            Validate(ship, prefabDatabase);
            if (string.IsNullOrWhiteSpace(ship.id) || !ids.Add(ship.id))
                throw new InvalidDataException("Ship IDs must be present and unique.");
        }
        saved.structures.RemoveAll(ship => !ship.hasMasterFrame);
        saved.structures.Sort((a, b) =>
        {
            int name = string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase);
            return name != 0 ? name : string.CompareOrdinal(a.id, b.id);
        });
        return saved.structures;
    }

    public void SaveShip(VoxelStructure ship, string name, string path = null)
    {
        if (ship == null || !ship.HasMasterFrame || !ship.transform.IsChildOf(transform))
            throw new InvalidOperationException("Aim at a master-frame ship to save it.");
        name = ValidateShipName(name);
        StructureData snapshot = ship.Capture();
        snapshot.name = name;
        snapshot.position = Vector3.zero;
        snapshot.rotation = Quaternion.identity;
        Validate(snapshot, prefabDatabase);
        path ??= SavePath;
        List<StructureData> ships = ReadSavedShips(path);
        int index = ships.FindIndex(s => s.id == snapshot.id);
        if (index < 0) ships.Add(snapshot); else ships[index] = snapshot;
        WriteShipLibrary(path, ships);
        ship.name = name;
        UpdateSelectedSave(snapshot.id, snapshot);
    }

    public bool RenameSavedShip(string id, string name, string path = null)
    {
        name = ValidateShipName(name);
        path ??= SavePath;
        List<StructureData> ships = ReadSavedShips(path);
        StructureData ship = ships.Find(s => s.id == id);
        if (ship == null) return false;
        ship.name = name;
        WriteShipLibrary(path, ships);
        UpdateSelectedSave(id, ship);
        return true;
    }

    public bool DeleteSavedShip(string id, string path = null)
    {
        path ??= SavePath;
        List<StructureData> ships = ReadSavedShips(path);
        int index = ships.FindIndex(s => s.id == id);
        if (index < 0) return false;
        ships.RemoveAt(index);
        WriteShipLibrary(path, ships);
        UpdateSelectedSave(id, null);
        return true;
    }

    private static string ValidateShipName(string name)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxShipNameLength)
            throw new ArgumentException($"Use a ship name between 1 and {MaxShipNameLength} characters.");
        foreach (char character in name)
            if (char.IsControl(character)) throw new ArgumentException("Use a single-line ship name.");
        return name;
    }

    private static void WriteShipLibrary(string path, List<StructureData> ships) =>
        WriteSnapshot(path + ".library", JsonUtility.ToJson(new StructureSaveData { structures = ships }, true));

    private void UpdateSelectedSave(string id, StructureData replacement)
    {
        if (SelectedShip?.id == id && builtInShips?.Contains(SelectedShip) != true) SelectSavedShip(replacement);
    }

    public void SelectSavedShip(StructureData ship)
    {
        if (ship != null) Validate(ship, prefabDatabase);
        if (ship != null && !ship.hasMasterFrame) throw new InvalidDataException("Only master-frame ships can be copied.");
        SelectedShip = ship;
        HideBlueprintPreview();
        if (ship != null)
        {
            shipMin = shipMax = ship.voxels[0].position;
            foreach (var voxel in ship.voxels)
            {
                shipMin = Vector3Int.Min(shipMin, voxel.position);
                shipMax = Vector3Int.Max(shipMax, voxel.position);
            }
        }
        attachmentBounds = ship == null ? Array.Empty<Bounds>() : new Bounds[ship.prefabs.Count];
        for (int i = 0; i < attachmentBounds.Length; i++)
        {
            prefabDatabase.TryGetPrefab(ship.prefabs[i].prefabId, out SpecialPrefabData prefab);
            attachmentBounds[i] = SpecialVoxelManager.GetLocalRenderBounds(prefab.Prefab);
        }
    }

    public bool TryGetSavedShipPlacement(RaycastHit hit, out BlueprintPlacement placement)
    {
        if (!TryGetPlacement(hit, SelectedShip, true, out placement)) return false;
        for (int i = 0; placement.canPlace && i < attachmentBounds.Length; i++)
        {
            var prefab = SelectedShip.prefabs[i];
            Bounds bounds = attachmentBounds[i];
            Vector3 center = prefab.position + prefab.rotation * Vector3.Scale(bounds.center, prefab.scale);
            placement.canPlace = IsSpaceFree(placement.position + placement.rotation * center,
                placement.rotation * prefab.rotation, Vector3.Max(Vector3.one * .001f,
                    Vector3.Scale(bounds.extents, prefab.scale) - Vector3.one * .005f));
        }
        return true;
    }

    public void ShowSavedShipPreview(RaycastHit hit)
    {
        if (framePreviewMaterial == null || !TryGetSavedShipPlacement(hit, out BlueprintPlacement placement))
        { HideBlueprintPreview(); return; }
        preview ??= new FramePlacementPreview(framePreviewMaterial);
        preview.Show(SelectedShip, placement.position, placement.rotation, placement.canPlace, prefabDatabase);
    }

    public bool TryPlaceSavedShip(RaycastHit hit)
    {
        if (!TryGetSavedShipPlacement(hit, out BlueprintPlacement placement) || !placement.canPlace) return false;
        // Deep-copy records before assigning identity; the saved template is immutable during placement.
        StructureData copy = JsonUtility.FromJson<StructureData>(JsonUtility.ToJson(SelectedShip));
        copy.id = Guid.NewGuid().ToString("N");
        foreach (var prefab in copy.prefabs) prefab.id = Guid.NewGuid().ToString("N");
        copy.position = placement.position;
        copy.rotation = placement.rotation;
        GameObject root = new(copy.name);
        root.SetActive(false);
        root.transform.SetParent(transform, false);
        VoxelStructure ship = root.AddComponent<VoxelStructure>();
        try
        {
            ship.Configure(voxelMaterial, prefabDatabase);
            ship.Restore(copy);
            root.SetActive(true);
            return true;
        }
        catch (Exception error)
        {
            ship.Unload();
            ShowStatus("Ship placement failed: " + error.Message);
            return false;
        }
    }
}
