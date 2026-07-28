using System.Collections.Generic;
using UnityEngine;

public sealed class BrokenComponentVisual : MonoBehaviour
{
    private const float BaseSparkMinSize = 0.003f;
    private const float BaseSparkMaxSize = 0.006f;
    private const float BaseSparkShapeAngle = 38f;
    private const float BaseSparkShapeRadius = 0.012f;
    private static Mesh sparkCubeMesh;

    [SerializeField]
    private Material brokenMaterialTemplate;

    [SerializeField]
    private ParticleSystem sparks;

    [SerializeField, Min(1)]
    private int failureBurstCount = 14;

    [SerializeField, Min(1)]
    private int batteryShortBurstCount = 18;

    [SerializeField, Min(1)]
    private int intermittentBurstCount = 5;

    [SerializeField, Min(0.1f)]
    private float minimumSparkInterval = 1f;

    [SerializeField, Min(0.1f)]
    private float maximumSparkInterval = 5f;

    [SerializeField, Min(1f)]
    private float inspectedSizeMultiplier = 2f;

    [SerializeField, Min(1f)]
    private float worldSizeMultiplier = 3f;

    [SerializeField, Min(1f)]
    private float inspectedSpreadMultiplier = 2f;

    [SerializeField]
    private Color sparkLightColor = new(0.68f, 0.86f, 1f, 1f);

    [SerializeField, Min(0f)]
    private float sparkLightIntensity = 0.08f;

    [SerializeField, Min(0.01f)]
    private float sparkLightRange = 0.08f;

    [SerializeField, Range(1, 6)]
    private int maximumSparkLights = 3;

    private readonly Dictionary<Renderer, Material[]> normalMaterials = new();
    private readonly List<Material> runtimeBrokenMaterials = new();
    private readonly List<Light> sparkLights = new();
    private readonly ParticleSystem.Particle[] sparkParticles = new ParticleSystem.Particle[64];
    private placementSystem placement;
    private bool damaged;
    private bool circuitFocused;
    private bool intermittentSparks;
    private float nextSparkTime;

    private void Awake()
        => ConfigureSparks();

    private void OnEnable()
        => BindPlacementSystem();

    private void Start()
    {
        if (placement == null)
            BindPlacementSystem();
    }

    private void LateUpdate()
    {
        if (sparks == null)
            return;

        if (intermittentSparks && Time.time >= nextSparkTime)
        {
            sparks.Play();
            sparks.Emit(intermittentBurstCount);
            ScheduleNextSpark();
        }

        UpdateSparkLights(sparks.GetParticles(sparkParticles));
    }

    private void OnDisable()
    {
        if (placement != null)
            placement.ActiveBoardChanged -= HandleActiveBoardChanged;
        placement = null;
        intermittentSparks = false;
        DisableSparkLights();
    }

    private void OnDestroy()
    {
        foreach (Material material in runtimeBrokenMaterials)
        {
            if (material != null)
                Destroy(material);
        }
    }

    public void PlayFailure()
    {
        ApplyDamageMaterial();
        intermittentSparks = true;
        ScheduleNextSpark();
        if (sparks == null)
            return;

        sparks.Play();
        sparks.Emit(failureBurstCount);
    }

    public void PlayBatteryShortBurst()
    {
        if (sparks != null)
        {
            sparks.Play();
            sparks.Emit(batteryShortBurstCount);
        }
    }

    public void ApplyLoadedState(bool broken)
    {
        if (broken)
        {
            ApplyDamageMaterial();
            intermittentSparks = true;
            ScheduleNextSpark();
            sparks?.Play();
        }
        else
        {
            intermittentSparks = false;
            RestoreNormalMaterial();
            sparks?.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    private void ConfigureSparks()
    {
        if (sparks == null)
            return;

        ParticleSystem.MainModule main = sparks.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.12f, 0.28f);
        main.startColor = new Color(0.82f, 0.93f, 1f, 1f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0.18f;

        ParticleSystem.EmissionModule emission = sparks.emission;
        emission.enabled = false;

        ParticleSystem.CollisionModule collision = sparks.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World;
        collision.mode = ParticleSystemCollisionMode.Collision3D;
        collision.collidesWith = ~0;
        collision.enableDynamicColliders = true;
        collision.quality = ParticleSystemCollisionQuality.High;
        collision.dampen = new ParticleSystem.MinMaxCurve(0.2f, 0.35f);
        collision.bounce = new ParticleSystem.MinMaxCurve(0.45f, 0.7f);
        collision.lifetimeLoss = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
        collision.radiusScale = 0.7f;

        ParticleSystem.ShapeModule shape = sparks.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.position = Vector3.up * 0.01f;
        shape.rotation = new Vector3(-90f, 0f, 0f);

        ParticleSystem.ColorOverLifetimeModule colors = sparks.colorOverLifetime;
        colors.enabled = true;
        Gradient gradient = new();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.92f, 0.98f, 1f), 0f),
                new GradientColorKey(new Color(0.58f, 0.8f, 1f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0.8f, 0.65f),
                new GradientAlphaKey(0f, 1f)
            });
        colors.color = gradient;

        ParticleSystemRenderer renderer = sparks.GetComponent<ParticleSystemRenderer>();
        if (renderer == null)
            return;

        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = GetSparkCubeMesh();
        renderer.velocityScale = 0f;
        renderer.lengthScale = 1f;

        ApplySparkPresentation(false);
    }

    private void ScheduleNextSpark()
    {
        float minimum = Mathf.Max(0.1f, minimumSparkInterval);
        float maximum = Mathf.Max(minimum, maximumSparkInterval);
        nextSparkTime = Time.time + Random.Range(minimum, maximum);
    }

    private void BindPlacementSystem()
    {
        placementSystem foundPlacement = FindFirstObjectByType<placementSystem>();
        if (foundPlacement == placement)
        {
            RefreshCircuitFocus();
            return;
        }

        if (placement != null)
            placement.ActiveBoardChanged -= HandleActiveBoardChanged;

        placement = foundPlacement;
        if (placement != null)
            placement.ActiveBoardChanged += HandleActiveBoardChanged;
        RefreshCircuitFocus();
    }

    private void HandleActiveBoardChanged(CircuitBoardContext board)
        => SetCircuitFocused(
            board?.Circuit != null && transform.IsChildOf(board.Circuit.transform));

    private void RefreshCircuitFocus()
        => HandleActiveBoardChanged(placement != null ? placement.ActiveBoard : null);

    private void SetCircuitFocused(bool focused)
    {
        if (circuitFocused == focused && sparks != null)
        {
            ApplySparkPresentation(focused);
            return;
        }

        circuitFocused = focused;
        ApplySparkPresentation(focused);
    }

    private void ApplySparkPresentation(bool focused)
    {
        if (sparks == null)
            return;

        float sizeMultiplier = focused ? inspectedSizeMultiplier : worldSizeMultiplier;
        ParticleSystem.MainModule main = sparks.main;
        main.startSize = new ParticleSystem.MinMaxCurve(
            BaseSparkMinSize * sizeMultiplier,
            BaseSparkMaxSize * sizeMultiplier);

        ParticleSystem.ShapeModule shape = sparks.shape;
        float spreadMultiplier = focused ? inspectedSpreadMultiplier : 1f;
        shape.angle = BaseSparkShapeAngle * spreadMultiplier;
        shape.radius = BaseSparkShapeRadius * spreadMultiplier;

    }

    private void UpdateSparkLights(int particleCount)
    {
        int lightCount = Mathf.Min(particleCount, maximumSparkLights);
        EnsureSparkLightCount(lightCount);
        float presentationScale = circuitFocused
            ? Mathf.Sqrt(inspectedSizeMultiplier)
            : Mathf.Sqrt(worldSizeMultiplier);

        for (int i = 0; i < sparkLights.Count; i++)
        {
            Light light = sparkLights[i];
            bool active = i < lightCount;
            light.enabled = active;
            if (!active)
                continue;

            int particleIndex = Mathf.Min(
                particleCount - 1,
                Mathf.FloorToInt((i + 0.5f) * particleCount / lightCount));
            light.transform.position = sparkParticles[particleIndex].position;
            float flicker = Mathf.Lerp(
                0.65f,
                1f,
                Mathf.PerlinNoise(
                    Time.unscaledTime * 40f,
                    (GetInstanceID() + i * 31) * 0.001f));
            light.intensity = sparkLightIntensity * flicker;
            light.range = sparkLightRange * presentationScale;
        }
    }

    private void EnsureSparkLightCount(int count)
    {
        while (sparkLights.Count < count)
        {
            GameObject lightObject = new($"Spark Particle Light {sparkLights.Count + 1}");
            lightObject.transform.SetParent(transform, false);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = sparkLightColor;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.enabled = false;
            sparkLights.Add(light);
        }
    }

    private void DisableSparkLights()
    {
        foreach (Light light in sparkLights)
        {
            if (light != null)
                light.enabled = false;
        }
    }

    private static Mesh GetSparkCubeMesh()
    {
        if (sparkCubeMesh != null)
            return sparkCubeMesh;

        Vector3[] vertices =
        {
            new(-1f, -1f, -1f), new(1f, -1f, -1f),
            new(1f, 1f, -1f), new(-1f, 1f, -1f),
            new(-1f, -1f, 1f), new(1f, -1f, 1f),
            new(1f, 1f, 1f), new(-1f, 1f, 1f)
        };
        int[] triangles =
        {
            0, 2, 1, 0, 3, 2,
            1, 2, 6, 1, 6, 5,
            5, 6, 7, 5, 7, 4,
            4, 7, 3, 4, 3, 0,
            3, 7, 6, 3, 6, 2,
            4, 0, 1, 4, 1, 5
        };
        Vector2[] uvs = new Vector2[vertices.Length];
        for (int i = 0; i < uvs.Length; i++)
            uvs[i] = new Vector2(0.5f, 0.5f);

        sparkCubeMesh = new Mesh
        {
            name = "Runtime Spark Cube",
            hideFlags = HideFlags.HideAndDontSave,
            vertices = vertices,
            triangles = triangles,
            uv = uvs
        };
        sparkCubeMesh.RecalculateNormals();
        sparkCubeMesh.RecalculateBounds();
        return sparkCubeMesh;
    }

    private void ApplyDamageMaterial()
    {
        if (damaged)
            return;

        damaged = true;
        if (brokenMaterialTemplate == null)
            return;

        foreach (Renderer target in GetComponentsInChildren<Renderer>(true))
        {
            if (target is ParticleSystemRenderer)
                continue;

            Material[] originals = target.sharedMaterials;
            normalMaterials[target] = originals;
            Material[] damagedMaterials = new Material[originals.Length];
            for (int i = 0; i < originals.Length; i++)
            {
                Material instance = new(brokenMaterialTemplate)
                {
                    name = $"{brokenMaterialTemplate.name} ({target.name})"
                };
                CopyBaseProperties(originals[i], instance);
                runtimeBrokenMaterials.Add(instance);
                damagedMaterials[i] = instance;
            }

            target.sharedMaterials = damagedMaterials;
        }
    }

    private void RestoreNormalMaterial()
    {
        if (!damaged)
            return;

        foreach (KeyValuePair<Renderer, Material[]> pair in normalMaterials)
        {
            if (pair.Key != null)
                pair.Key.sharedMaterials = pair.Value;
        }
        normalMaterials.Clear();
        damaged = false;
    }

    private static void CopyBaseProperties(Material source, Material destination)
    {
        if (source == null || destination == null)
            return;

        if (source.HasProperty("_BaseMap") && destination.HasProperty("_BaseMap"))
            destination.SetTexture("_BaseMap", source.GetTexture("_BaseMap"));
        if (source.HasProperty("_BaseColor") && destination.HasProperty("_BaseColor"))
            destination.SetColor("_BaseColor", source.GetColor("_BaseColor"));
        else if (source.HasProperty("_Color") && destination.HasProperty("_BaseColor"))
            destination.SetColor("_BaseColor", source.GetColor("_Color"));
    }
}
