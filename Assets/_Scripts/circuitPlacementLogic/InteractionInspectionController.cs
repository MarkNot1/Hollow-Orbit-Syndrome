using TMPro;
using Unity.Cinemachine;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class InteractionInspectionController : MonoBehaviour
{
    [Header("References")]
    [SerializeField]
    private Camera mainCamera;
    [SerializeField]
    private PlayerInput playerInput;
    [SerializeField]
    private Character character;
    [SerializeField]
    private Rigidbody playerRigidbody;
    [SerializeField]
    private Collider playerCollider;
    [SerializeField]
    private CinemachineCamera virtualCamera;
    [SerializeField]
    private TextMeshProUGUI promptText;
    [SerializeField]
    private PlayerInteractionLock interactionLock;

    [Header("Inspection")]
    [SerializeField]
    private float fallbackRayLength = 5f;
    [SerializeField]
    private float framingPadding = 0.72f;
    [SerializeField]
    private float minCameraDistance = 0.42f;
    [SerializeField]
    private float exitDistance = 1.6f;
    [SerializeField]
    private float exitRayHeight = 6f;
    [SerializeField]
    private float exitRayDistance = 12f;
    [SerializeField]
    private float cameraSlideDuration = 0.28f;
    [SerializeField]
    private float inspectionZoomSpeed = 0.006f;
    [SerializeField]
    private float inspectionNearClipPlane = 0.01f;
    [SerializeField]
    private float inspectionMinZoomDistance = 0.08f;
    [SerializeField]
    private float inspectionMinZoomMultiplier = 0.45f;
    [SerializeField]
    private float inspectionMaxZoomMultiplier = 2f;
    [SerializeField]
    private float inspectionTiltSpeed = 0.15f;
    [SerializeField]
    private float inspectionMaxTiltAngle = 35f;

    private readonly RaycastHit[] raycastHits = new RaycastHit[32];
    private IPlayerInteractable targetedInteractable;
    private IInspectableInteractable inspectedInteractable;
    private Transform inspectionAnchor;
    private Transform previousCameraFollow;
    private bool isInspecting;
    private Coroutine cameraSlideCoroutine;
    private Vector3 inspectionFocusPoint;
    private Vector3 inspectionBaseViewDirection;
    private Vector3 inspectionViewDirection;
    private float inspectionBaseDistance;
    private float inspectionCurrentDistance;
    private float previousCameraNearClipPlane;
    private float previousVirtualCameraNearClipPlane;
    private bool hasStoredCameraNearClipPlane;
    private bool hasStoredVirtualCameraNearClipPlane;

    public bool IsInspecting => isInspecting;
    public IInspectableInteractable CurrentInspectedInteractable => inspectedInteractable;
    public event Action<IInspectableInteractable> OnInspectionEntered;
    public event Action<IInspectableInteractable> OnInspectionExited;

    private void Awake()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;
        if (playerInput == null)
            playerInput = GetComponent<PlayerInput>();
        if (character == null)
            character = GetComponent<Character>();
        if (playerRigidbody == null)
            playerRigidbody = GetComponent<Rigidbody>();
        if (playerCollider == null)
            playerCollider = GetComponent<Collider>();
        if (interactionLock == null)
            interactionLock = GetComponent<PlayerInteractionLock>();
        if (interactionLock == null)
            interactionLock = gameObject.AddComponent<PlayerInteractionLock>();
        if (virtualCamera == null)
        {
            GameManager gameManager = FindFirstObjectByType<GameManager>();
            if (gameManager != null)
                virtualCamera = gameManager.camera_VM;
        }

        EnsurePrompt();
        SetPromptVisible(false);
    }

    private void OnEnable()
    {
        if (playerInput == null)
            return;

        playerInput.OnInteract += HandleInteract;
        playerInput.OnCancel += HandleCancel;
    }

    private void OnDisable()
    {
        if (playerInput != null)
        {
            playerInput.OnInteract -= HandleInteract;
            playerInput.OnCancel -= HandleCancel;
        }

        if (isInspecting)
            ExitInspection(false);
    }

    private void Update()
    {
        if (isInspecting)
        {
            HandleInspectionCameraControls();
            return;
        }

        targetedInteractable = FindTargetedInteractable();
        if (targetedInteractable == null)
        {
            SetPromptVisible(false);
            return;
        }

        if (promptText != null)
        {
            string binding = playerInput != null ? playerInput.GetInteractBindingDisplayName() : "E";
            promptText.text = targetedInteractable.InteractionPrompt + " [" + binding + "]";
            SetPromptVisible(true);
        }
    }

    private void HandleInteract()
    {
        if (isInspecting)
        {
            ExitInspection(true);
            return;
        }

        IPlayerInteractable interactable = targetedInteractable ?? FindTargetedInteractable();
        if (interactable == null)
            return;

        if (interactable.InteractionType == SpecialPrefabInteractionType.Activate)
        {
            interactable.TryActivate(gameObject);
            return;
        }

        if (interactable is IInspectableInteractable inspectable)
            EnterInspection(inspectable);
    }

    private void HandleCancel()
    {
        if (isInspecting)
            ExitInspection(true);
    }

    private IPlayerInteractable FindTargetedInteractable()
    {
        if (mainCamera == null)
            return null;

        float rayLength = character != null ? character.interactionRayLength : fallbackRayLength;
        Ray ray = new Ray(mainCamera.transform.position, mainCamera.transform.forward);
        int hitCount = Physics.RaycastNonAlloc(ray, raycastHits, rayLength, ~0, QueryTriggerInteraction.Ignore);
        IPlayerInteractable bestInteractable = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = raycastHits[i];
            if (hit.collider == null || hit.distance >= bestDistance)
                continue;

            IPlayerInteractable interactable = GetInteractableInParent(hit.collider);
            if (interactable == null)
                continue;

            bestInteractable = interactable;
            bestDistance = hit.distance;
        }

        return bestInteractable;
    }

    private static IPlayerInteractable GetInteractableInParent(Collider collider)
    {
        MonoBehaviour[] behaviours = collider.GetComponentsInParent<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IPlayerInteractable interactable)
                return interactable;
        }

        return null;
    }

    private void EnterInspection(IInspectableInteractable interactable)
    {
        if (interactable == null || virtualCamera == null)
            return;

        inspectedInteractable = interactable;
        isInspecting = true;
        SetPromptVisible(false);

        previousCameraFollow = virtualCamera.Follow;
        EnsureInspectionAnchor();
        ApplyInspectionClipPlane();
        Pose targetPose = GetInspectionCameraPose(interactable);
        PositionInspectionAnchorAtCurrentCamera();
        virtualCamera.Follow = inspectionAnchor;
        StartCameraSlide(targetPose.position, targetPose.rotation);

        interactionLock.SetLocked(true);
        OnInspectionEntered?.Invoke(interactable);
        HandleInspectionEntered(interactable);
    }

    private void ExitInspection(bool movePlayerNearInteractable)
    {
        IInspectableInteractable interactable = inspectedInteractable;

        if (virtualCamera != null && previousCameraFollow != null)
            virtualCamera.Follow = previousCameraFollow;

        StopCameraSlide();
        RestoreInspectionClipPlane();

        if (movePlayerNearInteractable && interactable != null)
            TryMovePlayerNearInteractable(interactable);

        inspectedInteractable = null;
        previousCameraFollow = null;
        isInspecting = false;
        interactionLock.SetLocked(false);

        OnInspectionExited?.Invoke(interactable);
        HandleInspectionExited(interactable);
    }

    private void ApplyInspectionClipPlane()
    {
        float nearClip = Mathf.Max(0.001f, inspectionNearClipPlane);

        if (mainCamera != null)
        {
            previousCameraNearClipPlane = mainCamera.nearClipPlane;
            hasStoredCameraNearClipPlane = true;
            mainCamera.nearClipPlane = nearClip;
        }

        if (virtualCamera != null)
        {
            LensSettings lens = virtualCamera.Lens;
            previousVirtualCameraNearClipPlane = lens.NearClipPlane;
            hasStoredVirtualCameraNearClipPlane = true;
            lens.NearClipPlane = nearClip;
            virtualCamera.Lens = lens;
        }
    }

    private void RestoreInspectionClipPlane()
    {
        if (mainCamera != null && hasStoredCameraNearClipPlane)
            mainCamera.nearClipPlane = previousCameraNearClipPlane;

        if (virtualCamera != null && hasStoredVirtualCameraNearClipPlane)
        {
            LensSettings lens = virtualCamera.Lens;
            lens.NearClipPlane = previousVirtualCameraNearClipPlane;
            virtualCamera.Lens = lens;
        }

        hasStoredCameraNearClipPlane = false;
        hasStoredVirtualCameraNearClipPlane = false;
    }

    protected virtual void HandleInspectionEntered(IInspectableInteractable interactable)
    {
    }

    protected virtual void HandleInspectionExited(IInspectableInteractable interactable)
    {
    }

    private Pose GetInspectionCameraPose(IInspectableInteractable interactable)
    {
        Bounds bounds = interactable.GetWorldBounds();
        Vector3 viewDirection = interactable.GetPreferredViewDirection();
        if (viewDirection.sqrMagnitude < 0.01f)
            viewDirection = -transform.forward;

        float fov = mainCamera != null ? mainCamera.fieldOfView : 60f;
        float largestSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z, VoxelMetrics.Size);
        float distance = (largestSize * framingPadding) / (2f * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad));
        distance = Mathf.Max(distance, minCameraDistance);

        inspectionFocusPoint = bounds.center;
        inspectionBaseViewDirection = viewDirection.normalized;
        inspectionViewDirection = inspectionBaseViewDirection;
        inspectionBaseDistance = distance;
        inspectionCurrentDistance = distance;

        Vector3 cameraPosition = GetInspectionCameraPosition();
        Quaternion cameraRotation = GetInspectionCameraRotation(cameraPosition);
        return new Pose(cameraPosition, cameraRotation);
    }

    private void HandleInspectionCameraControls()
    {
        if (inspectionAnchor == null || Mouse.current == null || cameraSlideCoroutine != null)
            return;

        bool changed = false;
        Vector2 scroll = Mouse.current.scroll.ReadValue();
        if (Mathf.Abs(scroll.y) > 0.01f)
        {
            float minDistance = Mathf.Max(inspectionMinZoomDistance, inspectionBaseDistance * inspectionMinZoomMultiplier);
            float maxDistance = Mathf.Max(minDistance + 0.01f, inspectionBaseDistance * inspectionMaxZoomMultiplier);
            inspectionCurrentDistance = Mathf.Clamp(
                inspectionCurrentDistance - scroll.y * inspectionZoomSpeed,
                minDistance,
                maxDistance);
            changed = true;
        }

        if (Mouse.current.middleButton.isPressed)
        {
            Vector2 delta = Mouse.current.delta.ReadValue();
            if (delta.sqrMagnitude > 0.01f)
            {
                changed |= TryTiltInspectionView(delta);
            }
        }

        if (!changed)
            return;

        Vector3 cameraPosition = GetInspectionCameraPosition();
        inspectionAnchor.SetPositionAndRotation(cameraPosition, GetInspectionCameraRotation(cameraPosition));
    }

    private Vector3 GetInspectionCameraPosition()
    {
        return inspectionFocusPoint + inspectionViewDirection * inspectionCurrentDistance;
    }

    private Quaternion GetInspectionCameraRotation(Vector3 cameraPosition)
    {
        return Quaternion.LookRotation(inspectionFocusPoint - cameraPosition, Vector3.up);
    }

    private bool TryTiltInspectionView(Vector2 delta)
    {
        Vector3 candidateDirection = inspectionViewDirection;

        if (Mathf.Abs(delta.x) > 0.01f)
            candidateDirection = Quaternion.AngleAxis(delta.x * inspectionTiltSpeed, Vector3.up) * candidateDirection;

        if (Mathf.Abs(delta.y) > 0.01f)
        {
            Vector3 tiltRight = Vector3.Cross(Vector3.up, candidateDirection);
            if (tiltRight.sqrMagnitude < 0.001f)
                tiltRight = inspectionAnchor.right;

            candidateDirection = Quaternion.AngleAxis(-delta.y * inspectionTiltSpeed, tiltRight.normalized) * candidateDirection;
        }

        candidateDirection.Normalize();
        if (candidateDirection.sqrMagnitude < 0.001f)
            return false;

        if (Vector3.Angle(inspectionBaseViewDirection, candidateDirection) > inspectionMaxTiltAngle)
            return false;

        inspectionViewDirection = candidateDirection;
        return true;
    }

    private void PositionInspectionAnchorAtCurrentCamera()
    {
        if (mainCamera != null)
            inspectionAnchor.SetPositionAndRotation(mainCamera.transform.position, mainCamera.transform.rotation);
        else
            inspectionAnchor.SetPositionAndRotation(transform.position, transform.rotation);
    }

    private void StartCameraSlide(Vector3 targetPosition, Quaternion targetRotation)
    {
        StopCameraSlide();
        cameraSlideCoroutine = StartCoroutine(SlideInspectionAnchor(targetPosition, targetRotation));
    }

    private void StopCameraSlide()
    {
        if (cameraSlideCoroutine == null)
            return;

        StopCoroutine(cameraSlideCoroutine);
        cameraSlideCoroutine = null;
    }

    private IEnumerator SlideInspectionAnchor(Vector3 targetPosition, Quaternion targetRotation)
    {
        Vector3 startPosition = inspectionAnchor.position;
        Quaternion startRotation = inspectionAnchor.rotation;
        float elapsed = 0f;
        float duration = Mathf.Max(0.01f, cameraSlideDuration);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            inspectionAnchor.SetPositionAndRotation(
                Vector3.Lerp(startPosition, targetPosition, t),
                Quaternion.Slerp(startRotation, targetRotation, t));
            yield return null;
        }

        inspectionAnchor.SetPositionAndRotation(targetPosition, targetRotation);
        cameraSlideCoroutine = null;
    }

    private void TryMovePlayerNearInteractable(IInspectableInteractable interactable)
    {
        if (playerCollider == null)
            return;

        Bounds bounds = interactable.GetWorldBounds();
        Vector3 exitDirection = interactable.GetExitDirection(transform.position);
        Vector3 target = bounds.center + exitDirection * exitDistance;
        Vector3 rayOrigin = target + Vector3.up * exitRayHeight;

        if (!TryFindExitGround(rayOrigin, out RaycastHit hit))
            return;

        float feetOffset = transform.position.y - playerCollider.bounds.min.y;
        Vector3 safePosition = new Vector3(target.x, hit.point.y + feetOffset + 0.02f, target.z);

        if (playerRigidbody != null)
        {
            playerRigidbody.position = safePosition;
            playerRigidbody.rotation = GetPlayerFacingInteractableRotation(safePosition, bounds.center);
        }
        else
        {
            transform.position = safePosition;
            transform.rotation = GetPlayerFacingInteractableRotation(safePosition, bounds.center);
        }
    }

    private Quaternion GetPlayerFacingInteractableRotation(Vector3 playerPosition, Vector3 interactableCenter)
    {
        Vector3 lookDirection = Vector3.ProjectOnPlane(interactableCenter - playerPosition, Vector3.up);
        if (lookDirection.sqrMagnitude < 0.01f)
            return transform.rotation;

        return Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
    }

    private bool TryFindExitGround(Vector3 rayOrigin, out RaycastHit groundHit)
    {
        groundHit = default;
        int hitCount = Physics.RaycastNonAlloc(rayOrigin, Vector3.down, raycastHits, exitRayDistance, ~0, QueryTriggerInteraction.Ignore);
        float bestDistance = float.MaxValue;
        bool found = false;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = raycastHits[i];
            if (hit.collider == null || hit.distance >= bestDistance)
                continue;
            if (hit.collider == playerCollider || GetInteractableInParent(hit.collider) != null)
                continue;

            groundHit = hit;
            bestDistance = hit.distance;
            found = true;
        }

        return found;
    }

    private void EnsureInspectionAnchor()
    {
        if (inspectionAnchor != null)
            return;

        GameObject anchor = new GameObject("Interaction Inspection Camera Anchor");
        inspectionAnchor = anchor.transform;
    }

    private void EnsurePrompt()
    {
        if (promptText != null)
            return;

        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
            return;

        GameObject promptObject = new GameObject("Interaction Prompt");
        promptObject.transform.SetParent(canvas.transform, false);

        RectTransform rectTransform = promptObject.AddComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = new Vector2(0f, -54f);
        rectTransform.sizeDelta = new Vector2(320f, 42f);

        promptText = promptObject.AddComponent<TextMeshProUGUI>();
        promptText.alignment = TextAlignmentOptions.Center;
        promptText.fontSize = 22f;
        promptText.color = Color.white;
        promptText.raycastTarget = false;
    }

    private void SetPromptVisible(bool visible)
    {
        if (promptText != null)
            promptText.gameObject.SetActive(visible);
    }
}
