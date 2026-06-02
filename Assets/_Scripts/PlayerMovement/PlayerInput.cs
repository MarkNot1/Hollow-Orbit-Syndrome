using System;
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
    private bool gameplayInputBlocked;

    private void Awake()
    {
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
