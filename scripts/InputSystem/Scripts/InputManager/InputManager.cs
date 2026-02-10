using UnityEngine;
using UnityEngine.InputSystem;
using System;

public class InputManager : MonoBehaviour
{
    public static InputManager Instance;

    // Polling inputs
    public Vector2 MoveInput { get; private set; }
    public Vector2 LookInput { get; private set; }
    public bool JumpJustPressed { get; private set; }
    public bool JumpBeingHeld { get; private set; }
    public bool JumpReleased { get; private set; }
    public bool SprintBeingHeld { get; private set; }
    public bool CrouchBeingHeld { get; private set; }
    public bool AttackPressed { get; private set; }
    public bool DashPressed { get; private set; }

    // Event-driven inputs
    public event Action OnInteract;
    public event Action OnAdvance;
    public event Action OnPause;

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
        MoveInput = _moveAction.ReadValue<Vector2>();
        LookInput = _lookAction.ReadValue<Vector2>();

        JumpJustPressed = _jumpAction.WasPressedThisFrame();
        JumpBeingHeld = _jumpAction.IsPressed();
        JumpReleased = _jumpAction.WasReleasedThisFrame();
        SprintBeingHeld = _sprintAction.IsPressed();
        CrouchBeingHeld = _crouchAction.IsPressed();

        AttackPressed = _attackAction.WasPressedThisFrame();
        DashPressed = _dashAction.WasPressedThisFrame();
    }

    private void RegisterEventInputs()
    {
        _interactAction.performed += HandleInteract;
        _advanceAction.performed += HandleAdvance;
        _pauseAction.performed += HandlePause;
    }

    private void UnregisterEventInputs()
    {
        _interactAction.performed -= HandleInteract;
        _advanceAction.performed -= HandleAdvance;
        _pauseAction.performed -= HandlePause;
    }

    private void HandleInteract(InputAction.CallbackContext ctx) => OnInteract?.Invoke();

    private void HandleAdvance(InputAction.CallbackContext ctx) => OnAdvance?.Invoke();

    private void HandlePause(InputAction.CallbackContext ctx) => OnPause?.Invoke();

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
