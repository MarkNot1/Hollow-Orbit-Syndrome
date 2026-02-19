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
    [Tooltip("Assign the InputSystem_Actions asset. Must contain a 'Player' action map with: Move, Look, Jump, Sprint, Attack, Fly.")]
    private InputActionAsset inputActions;

    public event Action OnMouseClick, OnFly;
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
        InputAction attackAction = _playerMap.FindAction("Attack");
        InputAction flyAction = _playerMap.FindAction("Fly");

        if (attackAction != null)
            attackAction.performed += OnAttackPerformed;
        if (flyAction != null)
            flyAction.performed += OnFlyPerformed;
    }

    private void OnEnable()
    {
        _playerMap?.Enable();
    }

    private void OnDisable()
    {
        _playerMap?.Disable();
        if (_playerMap != null)
        {
            InputAction attackAction = _playerMap.FindAction("Attack");
            InputAction flyAction = _playerMap.FindAction("Fly");
            if (attackAction != null)
                attackAction.performed -= OnAttackPerformed;
            if (flyAction != null)
                flyAction.performed -= OnFlyPerformed;
        }
    }

    private void OnAttackPerformed(InputAction.CallbackContext _)
    {
        OnMouseClick?.Invoke();
    }

    private void OnFlyPerformed(InputAction.CallbackContext _)
    {
        OnFly?.Invoke();
    }

    private void Update()
    {
        if (_playerMap == null || !_playerMap.enabled)
            return;

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
}
