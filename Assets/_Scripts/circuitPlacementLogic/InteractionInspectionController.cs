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
    [SerializeField, Min(0f)]
    private float inspectionZoomSpeed = 0.02f;
    [SerializeField, Min(0f)]
    private float inspectionMaxZoomStep = 0.75f;
    [SerializeField]
    private float inspectionNearClipPlane = 0.01f;
    [SerializeField]
    private float inspectionMinZoomDistance = 0.08f;
    [SerializeField, Min(1f)]
    private float inspectionMaxZoomMultiplier = 6f;
    [SerializeField]
    private float inspectionTiltSpeed = 0.2f;
    [SerializeField]
    [Range(10f, 89f)]
    private float inspectionMaxTiltAngle = 89f;
    [SerializeField, Min(0f)]
    private float inspectionOrbitSmoothTime = 0.035f;

    private IPlayerInteractable targetedInteractable;
    private readonly RaycastHit[] exitRaycastHits = new RaycastHit[32];
    private IInspectableInteractable inspectedInteractable;
    private Transform inspectionAnchor;
    private Transform previousCameraFollow;
    private bool isInspecting;
    private Coroutine cameraSlideCoroutine;
    private Vector3 inspectionFocusPoint;
    private Vector3 inspectionBoundsExtents;
    private float inspectionBaseDistance;
    private float inspectionCurrentDistance;
    private float inspectionTargetDistance;
    private float inspectionCurrentYaw;
    private float inspectionTargetYaw;
    private float inspectionCurrentPitch;
    private float inspectionTargetPitch;
    private float inspectionYawVelocity;
    private float inspectionPitchVelocity;
    private float inspectionDistanceVelocity;
    private bool isOrbitDragging;
    private CursorLockMode cursorLockBeforeOrbit;
    private bool cursorVisibleBeforeOrbit;
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

        if (playerInput == null || playerInput.IsGameplayInputBlocked) return;
        IPlayerInteractable interactable = FindTargetedInteractable();
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
        return playerInput != null && playerInput.TryGetReticleTarget(out IPlayerInteractable target)
            ? target
            : null;
    }

    private static IPlayerInteractable GetInteractableInParent(Collider collider)
    {
        MonoBehaviour[] behaviours = collider.GetComponentsInParent<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
            if (behaviours[i] is IPlayerInteractable interactable)
                return interactable;

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
        EndOrbitDrag();

        if (virtualCamera != null && previousCameraFollow != null)
            virtualCamera.Follow = previousCameraFollow;

        StopCameraSlide();
        RestoreInspectionClipPlane();

        if (movePlayerNearInteractable && interactable != null &&
            ShouldMovePlayerAfterInspection(interactable))
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

    protected virtual bool ShouldMovePlayerAfterInspection(IInspectableInteractable interactable)
        => true;

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
        inspectionBoundsExtents = bounds.extents;
        inspectionBaseDistance = distance;
        inspectionCurrentDistance = distance;
        inspectionTargetDistance = distance;
        SetOrbitAngles(viewDirection.normalized);

        Vector3 cameraPosition = GetInspectionCameraPosition();
        Quaternion cameraRotation = GetInspectionCameraRotation(cameraPosition);
        return new Pose(cameraPosition, cameraRotation);
    }

    private void HandleInspectionCameraControls()
    {
        if (inspectionAnchor == null || cameraSlideCoroutine != null)
            return;

        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            Vector2 scroll = mouse.scroll.ReadValue();
            if (Mathf.Abs(scroll.y) > 0.01f)
            {
                float zoomExponent = Mathf.Clamp(
                    -scroll.y * inspectionZoomSpeed,
                    -inspectionMaxZoomStep,
                    inspectionMaxZoomStep);
                float zoomFactor = Mathf.Exp(zoomExponent);
                inspectionTargetDistance *= zoomFactor;
            }

            if (mouse.middleButton.isPressed)
            {
                BeginOrbitDrag();
                Vector2 delta = mouse.delta.ReadValue();
                inspectionTargetYaw += delta.x * inspectionTiltSpeed;
                inspectionTargetPitch = Mathf.Clamp(
                    inspectionTargetPitch + delta.y * inspectionTiltSpeed,
                    -Mathf.Abs(inspectionMaxTiltAngle),
                    Mathf.Abs(inspectionMaxTiltAngle));
            }
            else
            {
                EndOrbitDrag();
            }
        }
        else
        {
            EndOrbitDrag();
        }

        Vector3 targetDirection = GetOrbitDirection(
            inspectionTargetYaw,
            inspectionTargetPitch);
        float minimumDistance = GetMinimumOrbitDistance(targetDirection);
        inspectionTargetDistance = Mathf.Clamp(
            inspectionTargetDistance,
            minimumDistance,
            GetMaximumOrbitDistance(minimumDistance));

        SmoothOrbitState();

        Vector3 cameraPosition = GetInspectionCameraPosition();
        inspectionAnchor.SetPositionAndRotation(cameraPosition, GetInspectionCameraRotation(cameraPosition));
    }

    private Vector3 GetInspectionCameraPosition()
    {
        return inspectionFocusPoint + GetOrbitDirection(
            inspectionCurrentYaw,
            inspectionCurrentPitch) * inspectionCurrentDistance;
    }

    private Quaternion GetInspectionCameraRotation(Vector3 cameraPosition)
    {
        return Quaternion.LookRotation(inspectionFocusPoint - cameraPosition, Vector3.up);
    }

    private void SetOrbitAngles(Vector3 viewDirection)
    {
        inspectionCurrentYaw = Mathf.Atan2(viewDirection.x, viewDirection.z) * Mathf.Rad2Deg;
        inspectionTargetYaw = inspectionCurrentYaw;
        inspectionCurrentPitch = Mathf.Asin(Mathf.Clamp(viewDirection.y, -1f, 1f)) * Mathf.Rad2Deg;
        inspectionCurrentPitch = Mathf.Clamp(
            inspectionCurrentPitch,
            -Mathf.Abs(inspectionMaxTiltAngle),
            Mathf.Abs(inspectionMaxTiltAngle));
        inspectionTargetPitch = inspectionCurrentPitch;
        inspectionYawVelocity = 0f;
        inspectionPitchVelocity = 0f;
        inspectionDistanceVelocity = 0f;
    }

    private void SmoothOrbitState()
    {
        float smoothTime = Mathf.Max(0f, inspectionOrbitSmoothTime);
        if (smoothTime <= 0f)
        {
            inspectionCurrentYaw = inspectionTargetYaw;
            inspectionCurrentPitch = inspectionTargetPitch;
            inspectionCurrentDistance = inspectionTargetDistance;
            return;
        }

        float deltaTime = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
        inspectionCurrentYaw = Mathf.SmoothDampAngle(
            inspectionCurrentYaw,
            inspectionTargetYaw,
            ref inspectionYawVelocity,
            smoothTime,
            Mathf.Infinity,
            deltaTime);
        inspectionCurrentPitch = Mathf.SmoothDampAngle(
            inspectionCurrentPitch,
            inspectionTargetPitch,
            ref inspectionPitchVelocity,
            smoothTime,
            Mathf.Infinity,
            deltaTime);
        inspectionCurrentDistance = Mathf.SmoothDamp(
            inspectionCurrentDistance,
            inspectionTargetDistance,
            ref inspectionDistanceVelocity,
            smoothTime,
            Mathf.Infinity,
            deltaTime);
    }

    private float GetMinimumOrbitDistance(Vector3 direction)
    {
        float distanceToBounds = float.PositiveInfinity;
        distanceToBounds = GetAxisExitDistance(
            inspectionBoundsExtents.x,
            direction.x,
            distanceToBounds);
        distanceToBounds = GetAxisExitDistance(
            inspectionBoundsExtents.y,
            direction.y,
            distanceToBounds);
        distanceToBounds = GetAxisExitDistance(
            inspectionBoundsExtents.z,
            direction.z,
            distanceToBounds);

        if (!float.IsFinite(distanceToBounds))
            distanceToBounds = 0f;

        float surfacePadding = Mathf.Max(inspectionNearClipPlane * 2f, 0.01f);
        return Mathf.Max(inspectionMinZoomDistance, distanceToBounds + surfacePadding);
    }

    private float GetMaximumOrbitDistance(float minimumDistance)
    {
        float multiplier = Mathf.Max(inspectionMaxZoomMultiplier, 1f);
        return Mathf.Max(minimumDistance + 0.01f, inspectionBaseDistance * multiplier);
    }

    private static Vector3 GetOrbitDirection(float yaw, float pitch)
    {
        float yawRadians = yaw * Mathf.Deg2Rad;
        float pitchRadians = pitch * Mathf.Deg2Rad;
        float horizontal = Mathf.Cos(pitchRadians);
        return new Vector3(
            horizontal * Mathf.Sin(yawRadians),
            Mathf.Sin(pitchRadians),
            horizontal * Mathf.Cos(yawRadians));
    }

    private static float GetAxisExitDistance(
        float extent,
        float direction,
        float currentDistance)
    {
        float magnitude = Mathf.Abs(direction);
        return magnitude > 0.0001f
            ? Mathf.Min(currentDistance, Mathf.Max(0f, extent) / magnitude)
            : currentDistance;
    }

    private void BeginOrbitDrag()
    {
        if (isOrbitDragging)
            return;

        isOrbitDragging = true;
        cursorLockBeforeOrbit = Cursor.lockState;
        cursorVisibleBeforeOrbit = Cursor.visible;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void EndOrbitDrag()
    {
        if (!isOrbitDragging)
            return;

        isOrbitDragging = false;
        Cursor.lockState = cursorLockBeforeOrbit;
        Cursor.visible = cursorVisibleBeforeOrbit;
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
        int hitCount = Physics.RaycastNonAlloc(rayOrigin, Vector3.down, exitRaycastHits, exitRayDistance, ~0, QueryTriggerInteraction.Ignore);
        float bestDistance = float.MaxValue;
        bool found = false;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = exitRaycastHits[i];
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
