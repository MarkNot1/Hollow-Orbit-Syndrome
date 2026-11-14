using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public sealed partial class StructureSystem
{
    public StructureData SelectedShip { get; private set; }
    private Bounds[] attachmentBounds = Array.Empty<Bounds>();
    private Vector3Int shipMin, shipMax;

    public List<StructureData> ReadSavedShips(string path = null)
    {
        path ??= SavePath;
        Dictionary<string, StructureData> ships = new(StringComparer.Ordinal);
        foreach (string file in new[] { path, path + ".library" })
        {
            if (!File.Exists(file)) continue;
            StructureSaveData saved = JsonUtility.FromJson<StructureSaveData>(File.ReadAllText(file));
            if (saved == null || saved.version != 1 || saved.structures == null)
                throw new InvalidDataException("Unsupported ship library format.");
            HashSet<string> ids = new(StringComparer.Ordinal);
            foreach (StructureData ship in saved.structures)
            {
                Validate(ship, prefabDatabase);
                if (string.IsNullOrWhiteSpace(ship.id) || !ids.Add(ship.id))
                    throw new InvalidDataException("Ship IDs must be present and unique.");
                if (ship.hasMasterFrame) ships[ship.id] = ship;
            }
        }
        List<StructureData> result = new(ships.Values);
        result.Sort((a, b) => { int name = string.CompareOrdinal(a.name, b.name); return name != 0 ? name : string.CompareOrdinal(a.id, b.id); });
        return result;
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
