using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Reads player input from Unity's new Input System (InputSystem_Actions "Player" map)
/// and exposes the same API for Character, PlayerCamera, etc.
/// </summary>
public class PlayerInput : MonoBehaviour
{
    [Header("Input Actions")]
    [SerializeField]
    [Tooltip("Assign the InputSystem_Actions asset. Must contain a 'Player' action map with: Move, Look, Jump, Sprint, Fly, DestroyBlock (optional).")]
    private InputActionAsset inputActions;

    [Header("Reticle")]
    [SerializeField] private Camera reticleCamera;
    [SerializeField, Min(0f)] private float reticleRayLength = 5f;

    public event Action OnFly;
    /// <summary>Fired when <c>DestroyBlock</c> is performed (e.g. right mouse).</summary>
    public event Action OnDestroyBlock;
    /// <summary>Fired when <c>PlaceBlock</c> is performed (e.g. left mouse).</summary>
    public event Action OnPlaceBlock;
    public event Action OnInteract;
    public event Action OnCancel;

    public bool RunningPressed { get; private set; }
    public Vector3 MovementInput { get; private set; }
    /// <summary>Mouse/look delta this frame (pixels). With new Input System this is pointer delta.</summary>
    public Vector2 MousePosition { get; private set; }
    public bool IsJumping { get; private set; }

    private InputActionMap _playerMap;
    private InputAction _moveAction;
    private InputAction _lookAction;
    private InputAction _jumpAction;
    private InputAction _sprintAction;
    private InputAction _flyAction;
    private InputAction _destroyBlockAction;
    private InputAction _interactAction;
    private InputAction _circuitDetailsAction;
    private bool gameplayInputBlocked;
    private readonly RaycastHit[] reticleHits = new RaycastHit[32];
    private readonly List<MonoBehaviour> reticleBehaviours = new List<MonoBehaviour>();
    private int reticleHitCount;
    private int reticleRaycastFrame = -1;

    private void Awake()
    {
        if (reticleCamera == null)
            reticleCamera = GetComponentInChildren<Camera>();

        if (inputActions == null)
        {
            Debug.LogError("PlayerInput: Assign the InputSystem_Actions asset in the Inspector.", this);
            return;
        }

        _playerMap = inputActions.FindActionMap("Player");
        if (_playerMap == null)
        {
            Debug.LogError("PlayerInput: InputActionAsset has no 'Player' action map. Check the asset.", this);
            return;
        }

        _moveAction = _playerMap.FindAction("Move");
        _lookAction = _playerMap.FindAction("Look");
        _jumpAction = _playerMap.FindAction("Jump");
        _sprintAction = _playerMap.FindAction("Sprint");
        _flyAction = _playerMap.FindAction("Fly");
        _destroyBlockAction = _playerMap.FindAction("DestroyBlock");
        _interactAction = _playerMap.FindAction("Interact");
        _circuitDetailsAction = _playerMap.FindAction("CircuitDetails");

        if (_flyAction != null)
            _flyAction.performed += OnFlyPerformed;
        if (_destroyBlockAction != null)
            _destroyBlockAction.performed += OnDestroyBlockPerformed;
        if (_interactAction != null)
            _interactAction.started += OnInteractStarted;
        // PlaceBlock is polled directly via Mouse.current in Update(), no asset action needed.
    }

    private void OnEnable()
    {
        _playerMap?.Enable();
    }

    private void OnDisable()
    {
        _playerMap?.Disable();
    }

    private void OnDestroy()
    {
        if (_flyAction != null)
            _flyAction.performed -= OnFlyPerformed;
        if (_destroyBlockAction != null)
            _destroyBlockAction.performed -= OnDestroyBlockPerformed;
        if (_interactAction != null)
            _interactAction.started -= OnInteractStarted;
    }

    private void OnDestroyBlockPerformed(InputAction.CallbackContext _)
    {
        if (gameplayInputBlocked)
            return;

        OnDestroyBlock?.Invoke();
    }

    private void OnPlaceBlockPerformed(InputAction.CallbackContext _)
    {
        if (gameplayInputBlocked)
            return;

        OnPlaceBlock?.Invoke();
    }

    private void OnFlyPerformed(InputAction.CallbackContext _)
    {
        if (gameplayInputBlocked)
            return;

        OnFly?.Invoke();
    }

    private void OnInteractStarted(InputAction.CallbackContext _)
    {
        OnInteract?.Invoke();
    }

    private void Update()
    {
        // Poll left mouse button first, independent of action map state.
        if (!gameplayInputBlocked && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            OnPlaceBlock?.Invoke();

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            OnCancel?.Invoke();

        if (_playerMap == null || !_playerMap.enabled)
            return;

        if (gameplayInputBlocked)
        {
            MovementInput = Vector3.zero;
            MousePosition = Vector2.zero;
            IsJumping = false;
            RunningPressed = false;
            return;
        }

        if (_moveAction != null)
        {
            Vector2 move = _moveAction.ReadValue<Vector2>();
            MovementInput = new Vector3(move.x, 0f, move.y);
        }
        else
        {
            MovementInput = Vector3.zero;
        }

        if (_lookAction != null)
            MousePosition = _lookAction.ReadValue<Vector2>();
        else
            MousePosition = Vector2.zero;

        if (_jumpAction != null)
            IsJumping = _jumpAction.IsPressed();
        else
            IsJumping = false;

        if (_sprintAction != null)
            RunningPressed = _sprintAction.IsPressed();
        else
            RunningPressed = false;
    }

    public void SetGameplayInputBlocked(bool blocked)
    {
        gameplayInputBlocked = blocked;
        if (!blocked)
            return;

        MovementInput = Vector3.zero;
        MousePosition = Vector2.zero;
        IsJumping = false;
        RunningPressed = false;
    }

    public bool IsCircuitDetailsPressed()
        => _circuitDetailsAction != null && _circuitDetailsAction.IsPressed();

    public bool TryGetReticleTarget<T>(out T target) where T : class
    {
        RefreshReticleHits();
        target = null;
        float nearestDistance = float.MaxValue;

        for (int i = 0; i < reticleHitCount; i++)
        {
            RaycastHit hit = reticleHits[i];
            if (hit.collider == null || hit.distance >= nearestDistance)
                continue;

            reticleBehaviours.Clear();
            hit.collider.GetComponentsInParent(false, reticleBehaviours);
            for (int j = 0; j < reticleBehaviours.Count; j++)
            {
                if (reticleBehaviours[j] is not T candidate)
                    continue;

                target = candidate;
                nearestDistance = hit.distance;
                break;
            }
        }

        return target != null;
    }

    private void RefreshReticleHits()
    {
        if (reticleRaycastFrame == Time.frameCount)
            return;

        reticleRaycastFrame = Time.frameCount;
        if (gameplayInputBlocked)
        {
            reticleHitCount = 0;
            return;
        }

        if (reticleCamera == null)
            reticleCamera = Camera.main;

        reticleHitCount = reticleCamera == null ? 0 : Physics.RaycastNonAlloc(
            new Ray(reticleCamera.transform.position, reticleCamera.transform.forward),
            reticleHits,
            reticleRayLength,
            ~0,
            QueryTriggerInteraction.Ignore);
    }

    public string GetCircuitDetailsBindingDisplayName()
    {
        if (_circuitDetailsAction == null)
            return "CTRL";

        for (int i = 0; i < _circuitDetailsAction.bindings.Count; i++)
        {
            string displayName = _circuitDetailsAction.GetBindingDisplayString(
                i,
                InputBinding.DisplayStringOptions.DontIncludeInteractions);
            if (!string.IsNullOrWhiteSpace(displayName))
                return displayName.Contains("Ctrl", StringComparison.OrdinalIgnoreCase) ? "CTRL" : displayName;
        }

        return "CTRL";
    }

    public string GetInteractBindingDisplayName()
    {
        if (_interactAction == null)
            return "E";

        for (int i = 0; i < _interactAction.bindings.Count; i++)
        {
            InputBinding binding = _interactAction.bindings[i];
            if (binding.isComposite || binding.isPartOfComposite)
                continue;
            if (!string.IsNullOrEmpty(binding.groups) && !binding.groups.Contains("Keyboard&Mouse"))
                continue;

            string displayName = _interactAction.GetBindingDisplayString(i, InputBinding.DisplayStringOptions.DontIncludeInteractions);
            if (!string.IsNullOrWhiteSpace(displayName))
                return displayName;
        }

        string fallback = _interactAction.GetBindingDisplayString(InputBinding.DisplayStringOptions.DontIncludeInteractions);
        return string.IsNullOrWhiteSpace(fallback) ? "E" : fallback;
    }
}
