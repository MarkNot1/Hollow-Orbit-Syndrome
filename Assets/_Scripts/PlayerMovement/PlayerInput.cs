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
    [Tooltip("Assign the InputSystem_Actions asset. Must contain a 'Player' action map with: Move, Look, Jump, Sprint, Fly, PlaceVoxel, DestroyVoxel.")]
    private InputActionAsset inputActions;

    [Header("Reticle")]
    [SerializeField] private Camera reticleCamera;
    [SerializeField, Min(0f)] private float interactionReach = 15f;
    public float InteractionReach => interactionReach;
    public bool IsGameplayInputBlocked => gameplayInputBlocked;

    public event Action OnFly;
    /// <summary>Fired when <c>DestroyVoxel</c> is performed (left mouse).</summary>
    public event Action OnDestroyVoxel;
    /// <summary>Fired when <c>PlaceVoxel</c> is performed (right mouse).</summary>
    public event Action OnPlaceVoxel;
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
    private InputAction _destroyVoxelAction;
    private InputAction _placeVoxelAction;
    private InputAction _interactAction;
    private InputAction _circuitDetailsAction;
    private InputAction saveStructuresAction, loadStructuresAction;
    private StructureSystem structures;
    private bool gameplayInputBlocked;
    private readonly HashSet<object> inputBlockers = new();
    private readonly RaycastHit[] reticleHits = new RaycastHit[32];
    private readonly List<MonoBehaviour> reticleBehaviours = new List<MonoBehaviour>();

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
        _destroyVoxelAction = _playerMap.FindAction("DestroyVoxel");
        _placeVoxelAction = _playerMap.FindAction("PlaceVoxel");
        _interactAction = _playerMap.FindAction("Interact");
        _circuitDetailsAction = _playerMap.FindAction("CircuitDetails");
        saveStructuresAction = _playerMap.FindAction("SaveStructures");
        loadStructuresAction = _playerMap.FindAction("LoadStructures");
        structures = FindFirstObjectByType<StructureSystem>();

        if (_flyAction != null)
            _flyAction.performed += OnFlyPerformed;
        if (_destroyVoxelAction != null)
            _destroyVoxelAction.performed += OnDestroyVoxelPerformed;
        if (_interactAction != null)
            _interactAction.started += OnInteractStarted;
        if (_placeVoxelAction != null)
            _placeVoxelAction.performed += OnPlaceVoxelPerformed;
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
        if (_placeVoxelAction != null)
            _placeVoxelAction.performed -= OnPlaceVoxelPerformed;
        if (_flyAction != null)
            _flyAction.performed -= OnFlyPerformed;
        if (_destroyVoxelAction != null)
            _destroyVoxelAction.performed -= OnDestroyVoxelPerformed;
        if (_interactAction != null)
            _interactAction.started -= OnInteractStarted;
    }

    private void OnDestroyVoxelPerformed(InputAction.CallbackContext _)
    {
        if (gameplayInputBlocked)
            return;

        OnDestroyVoxel?.Invoke();
    }

    private void OnPlaceVoxelPerformed(InputAction.CallbackContext _)
    {
        if (gameplayInputBlocked)
            return;

        OnPlaceVoxel?.Invoke();
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

        if (saveStructuresAction != null && saveStructuresAction.WasPressedThisFrame()) structures?.SaveStructures();
        if (loadStructuresAction != null && loadStructuresAction.WasPressedThisFrame()) structures?.LoadStructures();

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

    public void SetGameplayInputBlocked(bool blocked, object owner = null)
    {
        owner ??= this;
        if (blocked) inputBlockers.Add(owner);
        else inputBlockers.Remove(owner);
        gameplayInputBlocked = inputBlockers.Count > 0;
        if (!gameplayInputBlocked)
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
        target = null;
        if (!TryGetReticleHit(out RaycastHit hit)) return false;
        reticleBehaviours.Clear();
        hit.collider.GetComponentsInParent(false, reticleBehaviours);
        foreach (MonoBehaviour behaviour in reticleBehaviours)
        {
            if (behaviour is not T candidate) continue;
            target = candidate;
            return true;
        }
        return false;
    }

    // All world actions use the nearest unobstructed hit and the same reach.
    public bool TryGetReticleHit(out RaycastHit nearestHit)
    {
        nearestHit = default;
        if (gameplayInputBlocked) return false;
        if (reticleCamera == null) reticleCamera = Camera.main;
        if (reticleCamera == null) return false;
        Ray ray = new(reticleCamera.transform.position, reticleCamera.transform.forward);
        int count = Physics.RaycastNonAlloc(ray, reticleHits, interactionReach,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        // A full buffer is unordered; never silently skip a nearer obstruction.
        RaycastHit[] hits = count == reticleHits.Length
            ? Physics.RaycastAll(ray, interactionReach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            : reticleHits;
        if (hits != reticleHits) count = hits.Length;
        float distance = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || hit.collider.transform.IsChildOf(transform) || hit.distance >= distance) continue;
            nearestHit = hit;
            distance = hit.distance;
        }
        return distance < float.PositiveInfinity;
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
