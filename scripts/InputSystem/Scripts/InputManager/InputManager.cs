using UnityEngine;
using UnityEngine.InputSystem;
using System;

public class InputManager : MonoBehaviour
{
    public static InputManager Instance;

    // Polling inputs
    public Vector2 Move { get; private set; }
    public Vector2 Look { get; private set; }
    public bool JumpPressed { get; private set; }
    public bool JumpHeld { get; private set; }
    public bool JumpReleased { get; private set; }
    public bool SprintHeld { get; private set; }
    public bool CrouchHeld { get; private set; }

    // Event-driven inputs
    public event Action OnInteract;
    public event Action OnAdvance;
    public event Action OnPause;
    public event Action OnAttack;
    public event Action OnDash;

    private PlayerInput _playerInput;
    private InputAction _moveAction;
    private InputAction _lookAction;
    private InputAction _jumpAction;
    private InputAction _sprintAction;
    private InputAction _crouchAction;
    private InputAction _attackAction;
    private InputAction _dashAction;
    private InputAction _interactAction;
    private InputAction _advanceAction;
    private InputAction _pauseAction;

    private void Awake()
    {
        Instance = this;

        _playerInput = GetComponent<PlayerInput>();
        SetupInputActions();
    }

    private void OnEnable()
    {
        RegisterEventInputs();
    }

    private void OnDisable()
    {
        UnregisterEventInputs();
    }

    private void Update()
    {
        UpdatePollingInputs();
    }

    private void SetupInputActions()
    {
        // Gameplay
        _moveAction = _playerInput.actions["Move"];
        _lookAction = _playerInput.actions["Look"];
        _jumpAction = _playerInput.actions["Jump"];
        _sprintAction = _playerInput.actions["Sprint"];
        _crouchAction = _playerInput.actions["Crouch"];
        _attackAction = _playerInput.actions["Attack"];
        _dashAction = _playerInput.actions["Dash"];
        _interactAction = _playerInput.actions["Interact"];
        _pauseAction = _playerInput.actions["Pause"];

        // UI
        _advanceAction = _playerInput.actions["Advance"];
    }


    private void UpdatePollingInputs()
    {
        Move = _moveAction.ReadValue<Vector2>();
        Look = _lookAction.ReadValue<Vector2>();

        JumpPressed = _jumpAction.WasPressedThisFrame();
        JumpHeld = _jumpAction.IsPressed();
        JumpReleased = _jumpAction.WasReleasedThisFrame();
        SprintHeld = _sprintAction.IsPressed();
        CrouchHeld = _crouchAction.IsPressed();
    }

    private void RegisterEventInputs()
    {
        _interactAction.performed += HandleInteract;
        _advanceAction.performed += HandleAdvance;
        _pauseAction.performed += HandlePause;
        _attackAction.performed += HandleAttack;
        _dashAction.performed += HandleDash;
    }

    private void UnregisterEventInputs()
    {
        _interactAction.performed -= HandleInteract;
        _advanceAction.performed -= HandleAdvance;
        _pauseAction.performed -= HandlePause;
        _attackAction.performed -= HandleAttack;
        _dashAction.performed -= HandleDash;
    }

    private void HandleInteract(InputAction.CallbackContext ctx) => OnInteract?.Invoke();
    private void HandleAdvance(InputAction.CallbackContext ctx) => OnAdvance?.Invoke();
    private void HandlePause(InputAction.CallbackContext ctx) => OnPause?.Invoke();
    private void HandleAttack(InputAction.CallbackContext ctx) => OnAttack?.Invoke();
    private void HandleDash(InputAction.CallbackContext ctx) => OnDash?.Invoke();

    public void SwitchToGameplay()
    {
        UnregisterEventInputs();
        _playerInput.SwitchCurrentActionMap("Gameplay");
        SetupInputActions();
        RegisterEventInputs();
    }

    public void SwitchToUI()
    {
        UnregisterEventInputs();
        _playerInput.SwitchCurrentActionMap("UI");
        SetupInputActions();
        RegisterEventInputs();
    }
}
