using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

public class debug_Menu : MonoBehaviour
{
    [SerializeField]
    private World world;

    [SerializeField]
    private GameManager gameManager;

    [SerializeField]
    private Key toggleKey = Key.F3;

    [SerializeField]
    private Color chunkBorderColor = new Color(0.2f, 1f, 0.2f, 1f);

    [SerializeField]
    private Color collisionBorderColor = new Color(1f, 0.35f, 1f, 1f);

    [SerializeField]
    [Tooltip("Collision outline is drawn slightly larger so it is not hidden by the chunk outline where they match.")]
    private float collisionOutlineInflate = 0.04f;

    [SerializeField]
    [Tooltip("Line width is not supported on all platforms; thickness is visual via bright color.")]
    private int overlayFontSize = 14;

    private bool overlayVisible;
    private float fpsSmoothed = 60f;
    private float lowestFpsLast10s = 60f;
    private const float FpsHistoryDuration = 10f;
    private readonly Queue<(float unscaledTime, float fps)> fpsHistory = new Queue<(float, float)>();
    private readonly List<Vector3Int> colliderKeyScratch = new List<Vector3Int>();
    private readonly List<Vector3> lineVertsScratch = new List<Vector3>(512);
    private readonly List<int> lineIndicesScratch = new List<int>(512);

    private Mesh chunkLinesMesh;
    private Mesh collisionLinesMesh;
    private Material chunkLineMaterial;
    private Material collisionLineMaterial;
    private Camera mainCam;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private void Awake()
    {
        SanitizeToggleKeyAfterOldKeyCodeSerialization();
        if (world == null)
            world = FindFirstObjectByType<World>();
        if (gameManager == null)
            gameManager = FindFirstObjectByType<GameManager>();
        mainCam = Camera.main;

        chunkLinesMesh = new Mesh { name = "DebugChunkWire" };
        chunkLinesMesh.indexFormat = IndexFormat.UInt32;
        collisionLinesMesh = new Mesh { name = "DebugCollisionWire" };
        collisionLinesMesh.indexFormat = IndexFormat.UInt32;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        SanitizeToggleKeyAfterOldKeyCodeSerialization();
    }
#endif

    private void SanitizeToggleKeyAfterOldKeyCodeSerialization()
    {
        if (!Enum.IsDefined(typeof(Key), toggleKey))
            toggleKey = Key.F3;
    }

    private void Update()
    {
        if (Keyboard.current != null && Enum.IsDefined(typeof(Key), toggleKey) &&
            Keyboard.current[toggleKey].wasPressedThisFrame)
            overlayVisible = !overlayVisible;

        float dt = Time.unscaledDeltaTime;
        if (dt > 0f)
        {
            float instantFps = 1f / dt;
            fpsSmoothed = Mathf.Lerp(fpsSmoothed, instantFps, 1f - Mathf.Exp(-dt * 8f));

            float now = Time.unscaledTime;
            fpsHistory.Enqueue((now, instantFps));
            while (fpsHistory.Count > 0 && now - fpsHistory.Peek().unscaledTime > FpsHistoryDuration)
                fpsHistory.Dequeue();

            float min = float.MaxValue;
            foreach (var sample in fpsHistory)
            {
                if (sample.fps < min)
                    min = sample.fps;
            }

            lowestFpsLast10s = min < float.MaxValue ? min : instantFps;
        }
    }

    private void LateUpdate()
    {
        if (!overlayVisible || world == null)
            return;

        if (mainCam == null)
            mainCam = Camera.main;
        if (mainCam == null)
            return;

        if (!EnsureLineMaterials())
            return;

        int sz = world.chunkSize;
        int ch = world.chunkHeight;

        BuildChunkLineMesh(lineVertsScratch, lineIndicesScratch, sz, ch);
        ApplyLineMesh(chunkLinesMesh, lineVertsScratch, lineIndicesScratch);

        BuildCollisionLineMesh(lineVertsScratch, lineIndicesScratch, sz, ch);
        ApplyLineMesh(collisionLinesMesh, lineVertsScratch, lineIndicesScratch);

        if (lineVertsScratch.Count > 0)
            Graphics.DrawMesh(chunkLinesMesh, Matrix4x4.identity, chunkLineMaterial, 0, mainCam, 0, null, ShadowCastingMode.Off, false, null, LightProbeUsage.Off, null);
        if (lineVertsScratch.Count > 0)
            Graphics.DrawMesh(collisionLinesMesh, Matrix4x4.identity, collisionLineMaterial, 0, mainCam, 0, null, ShadowCastingMode.Off, false, null, LightProbeUsage.Off, null);
    }

    private void BuildChunkLineMesh(List<Vector3> verts, List<int> indices, int size, int height)
    {
        verts.Clear();
        indices.Clear();

        foreach (var kv in world.worldData.chunkDictionary)
            AppendWireAlignedBox(verts, indices, kv.Key, size, height, 0f);
    }

    private void BuildCollisionLineMesh(List<Vector3> verts, List<int> indices, int size, int height)
    {
        verts.Clear();
        indices.Clear();

        colliderKeyScratch.Clear();
        if (gameManager != null)
            gameManager.CopyActiveColliderChunkKeysTo(colliderKeyScratch);

        if (colliderKeyScratch.Count == 0 && world != null && gameManager != null && gameManager.PlayerObject != null)
            AppendLocalColliderGrid3x3(colliderKeyScratch, world, gameManager.PlayerObject.transform.position);

        float e = Mathf.Max(0f, collisionOutlineInflate);
        foreach (Vector3Int key in colliderKeyScratch)
            AppendWireAlignedBox(verts, indices, key, size, height, e);
    }

    private static void AppendLocalColliderGrid3x3(List<Vector3Int> dst, World worldRef, Vector3 playerWorldPos)
    {
        Vector3Int center = WorldDataHelper.ChunkPositionFromVoxelCoords(worldRef, VoxelMetrics.WorldToVoxelCoord(playerWorldPos));
        int cs = worldRef.chunkSize;
        int ch = worldRef.chunkHeight;
        for (int oy = -1; oy <= 1; oy++)
        for (int ox = -1; ox <= 1; ox++)
        for (int oz = -1; oz <= 1; oz++)
            dst.Add(center + new Vector3Int(ox * cs, oy * ch, oz * cs));
    }

    private static void AppendWireAlignedBox(List<Vector3> verts, List<int> indices, Vector3Int origin, int size, int height, float inflate)
    {
        float s = VoxelMetrics.Size;
        float hpad = inflate;
        Vector3 o = new Vector3(origin.x - 0.5f, origin.y - 0.5f, origin.z - 0.5f) * s;
        Vector3 sx = new Vector3(size * s + inflate, 0f, 0f);
        Vector3 sy = new Vector3(0f, height * s + hpad, 0f);
        Vector3 sz = new Vector3(0f, 0f, size * s + inflate);

        Vector3 c000 = o;
        Vector3 c100 = o + sx;
        Vector3 c001 = o + sz;
        Vector3 c101 = o + sx + sz;
        Vector3 c010 = o + sy;
        Vector3 c110 = o + sx + sy;
        Vector3 c011 = o + sy + sz;
        Vector3 c111 = o + sx + sy + sz;

        AddLine(verts, indices, c000, c100);
        AddLine(verts, indices, c100, c101);
        AddLine(verts, indices, c101, c001);
        AddLine(verts, indices, c001, c000);

        AddLine(verts, indices, c010, c110);
        AddLine(verts, indices, c110, c111);
        AddLine(verts, indices, c111, c011);
        AddLine(verts, indices, c011, c010);

        AddLine(verts, indices, c000, c010);
        AddLine(verts, indices, c100, c110);
        AddLine(verts, indices, c101, c111);
        AddLine(verts, indices, c001, c011);
    }

    private static void AddLine(List<Vector3> verts, List<int> indices, Vector3 a, Vector3 b)
    {
        int i = verts.Count;
        verts.Add(a);
        verts.Add(b);
        indices.Add(i);
        indices.Add(i + 1);
    }

    private static void ApplyLineMesh(Mesh mesh, List<Vector3> verts, List<int> indices)
    {
        mesh.Clear();
        if (verts.Count == 0)
            return;

        mesh.SetVertices(verts);
        mesh.SetIndices(indices.ToArray(), MeshTopology.Lines, 0, true, 0);
        mesh.bounds = ComputeBounds(verts);
    }

    private static Bounds ComputeBounds(List<Vector3> verts)
    {
        if (verts.Count == 0)
            return new Bounds(Vector3.zero, Vector3.one);

        Vector3 min = verts[0];
        Vector3 max = verts[0];
        for (int i = 1; i < verts.Count; i++)
        {
            Vector3 v = verts[i];
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
        }

        Vector3 c = (min + max) * 0.5f;
        Vector3 e = max - min;
        return new Bounds(c, new Vector3(Mathf.Max(e.x, 0.1f), Mathf.Max(e.y, 0.1f), Mathf.Max(e.z, 0.1f)));
    }

    private bool EnsureLineMaterials()
    {
        if (chunkLineMaterial != null && collisionLineMaterial != null)
            return true;

        Shader shader =
            Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Unlit/Color")
            ?? Shader.Find("Sprites/Default");

        if (shader == null)
            return false;

        chunkLineMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        collisionLineMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };

        ApplyDebugLineMaterialState(chunkLineMaterial, chunkBorderColor);
        ApplyDebugLineMaterialState(collisionLineMaterial, collisionBorderColor);
        return true;
    }

    private static void ApplyDebugLineMaterialState(Material mat, Color color)
    {
        if (mat == null)
            return;

        color.a = 1f;
        if (mat.HasProperty(BaseColorId))
            mat.SetColor(BaseColorId, color);
        if (mat.HasProperty(ColorId))
            mat.SetColor(ColorId, color);
        mat.color = color;

        mat.renderQueue = (int)RenderQueue.Geometry;
    }

    private void OnGUI()
    {
        if (!overlayVisible)
            return;

        var sb = new StringBuilder(256);
        sb.AppendLine("Debug (").Append(toggleKey).AppendLine(")");
        sb.Append("FPS: ").AppendLine(Mathf.RoundToInt(fpsSmoothed).ToString());
        sb.Append("Lowest FPS (10s): ").AppendLine(Mathf.RoundToInt(lowestFpsLast10s).ToString());

        Transform player = gameManager != null && gameManager.PlayerObject != null
            ? gameManager.PlayerObject.transform
            : null;
        if (player != null && world != null)
        {
            Vector3Int voxel = VoxelMetrics.WorldToVoxelCoord(player.position);
            Vector3Int chunkPos = WorldDataHelper.ChunkPositionFromVoxelCoords(world, voxel);
            sb.Append("XYZ: ").Append(voxel.x).Append(" / ").Append(voxel.y).Append(" / ").AppendLine(voxel.z.ToString());
            sb.Append("Chunk: ").Append(chunkPos.x).Append(" ").Append(chunkPos.y).Append(" ").AppendLine(chunkPos.z.ToString());
            sb.AppendLine("Green: loaded chunk borders (depth-tested)");
            sb.AppendLine("Magenta: collision 3×3×3 (slightly inflated)");
        }
        else
        {
            sb.AppendLine("(Assign World + GameManager, spawn player for coords)");
        }

        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = overlayFontSize,
            alignment = TextAnchor.UpperLeft,
            richText = false
        };
        style.normal.textColor = Color.white;

        const float pad = 6f;
        const float maxW = 420f;
        GUIContent content = new GUIContent(sb.ToString());
        float h = style.CalcHeight(content, maxW);
        GUI.Box(new Rect(pad, pad, maxW, h + pad * 2f), GUIContent.none);
        GUI.Label(new Rect(pad * 2f, pad * 2f, maxW - pad, h), content, style);
    }

    private void OnDestroy()
    {
        if (chunkLinesMesh != null)
            Destroy(chunkLinesMesh);
        if (collisionLinesMesh != null)
            Destroy(collisionLinesMesh);
        if (chunkLineMaterial != null)
            Destroy(chunkLineMaterial);
        if (collisionLineMaterial != null)
            Destroy(collisionLineMaterial);
    }
}
