using UnityEngine;

public class PreviewSystem : MonoBehaviour
{
    [SerializeField]
    private GameObject previewObject;

    [SerializeField]
    private Material previewMaterialsPrefab;

    private Material previewMaterialInstance;

    public GameObject PreviewObject => previewObject;

    private void Awake()
    {
        EnsurePreviewMaterial();
    }

    private void OnDestroy()
    {
        if (previewMaterialInstance != null)
            Destroy(previewMaterialInstance);
    }

    public GameObject StartShowingPlacementPreview(GameObject prefab)
    {
        return StartShowingPreview(prefab, true);
    }

    public GameObject StartShowingRemovalPreview(GameObject prefab)
    {
        return StartShowingPreview(prefab, false);
    }

    public void StopShowingPreview()
    {
        if (previewObject == null)
            return;

        Destroy(previewObject);
        previewObject = null;
    }

    public void SetPreviewVisible(bool visible)
    {
        if (previewObject != null && previewObject.activeSelf != visible)
            previewObject.SetActive(visible);
    }

    public void UpdatePosition(Vector3 position, bool validity)
    {
        if (previewObject == null)
            return;

        previewObject.transform.position = position;
        ApplyFeedback(validity);
    }

    private GameObject StartShowingPreview(GameObject prefab, bool validity)
    {
        StopShowingPreview();
        if (prefab == null || !EnsurePreviewMaterial())
            return null;

        previewObject = Instantiate(prefab);
        PreparePreview(previewObject);
        ApplyFeedback(validity);
        return previewObject;
    }

    private void PreparePreview(GameObject instance)
    {
        foreach (ParticleSystem particles in instance.GetComponentsInChildren<ParticleSystem>(true))
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
        foreach (Renderer renderer in renderers)
        {
            if (renderer is ParticleSystemRenderer)
            {
                renderer.enabled = false;
                continue;
            }

            Material[] materials = renderer.materials;
            for (int i = 0; i < materials.Length; i++)
                materials[i] = previewMaterialInstance;

            renderer.materials = materials;
        }

        Collider[] colliders = instance.GetComponentsInChildren<Collider>();
        foreach (Collider previewCollider in colliders)
            previewCollider.enabled = false;
    }

    private void ApplyFeedback(bool validity)
    {
        if (previewMaterialInstance == null)
            return;

        Color color = validity ? Color.white : Color.red;
        color.a = 0.2f;
        previewMaterialInstance.color = color;
    }

    private bool EnsurePreviewMaterial()
    {
        if (previewMaterialInstance != null)
            return true;

        if (previewMaterialsPrefab == null)
        {
            Debug.LogError("Preview material prefab is not assigned.", this);
            return false;
        }

        previewMaterialInstance = new Material(previewMaterialsPrefab);
        return true;
    }
}
