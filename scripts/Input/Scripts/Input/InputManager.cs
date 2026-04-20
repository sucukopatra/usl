using UnityEngine;
using UnityEngine.InputSystem;
using System;

[DefaultExecutionOrder(-1)]
public class InputManager : MonoBehaviour
{
    public static InputManager Instance;

    // Gameplay - polling
    public Vector2 Move { get; private set; }
    public Vector2 Look { get; private set; }
    public bool JumpPressed { get; private set; }
    public bool JumpHeld { get; private set; }
    public bool JumpReleased { get; private set; }
    public bool SprintHeld { get; private set; }
    public bool CrouchHeld { get; private set; }

    // Gameplay - events
    public event Action OnInteract;
    public event Action OnAttack;
    public event Action OnDash;
    public event Action OnPause;
    public event Action OnPrevious;
    public event Action OnNext;

    // UI - events
    public event Action OnNavigate;
    public event Action OnAdvance;
    public event Action OnSubmit;
    public event Action OnCancel;

    public PlayerInput PlayerInput => _playerInput;
    private PlayerInput _playerInput;

    // Gameplay actions
    private InputAction _moveAction;
    private InputAction _lookAction;
    private InputAction _jumpAction;
    private InputAction _sprintAction;
    private InputAction _crouchAction;
    private InputAction _attackAction;
    private InputAction _dashAction;
    private InputAction _interactAction;
    private InputAction _pauseAction;
    private InputAction _previousAction;
    private InputAction _nextAction;

    // UI actions
    private InputAction _navigateAction;
    private InputAction _submitAction;
    private InputAction _cancelAction;
    private InputAction _advanceAction;

    private void Awake()
    {
        Instance = this;
        _playerInput = GetComponent<PlayerInput>();
        SetupInputActions();
    }

    private void OnEnable() => RegisterEventInputs();
    private void OnDisable() => UnregisterEventInputs();
    private void Update() => UpdatePollingInputs();

    private void SetupInputActions()
    {
        // Gameplay
        _moveAction       = _playerInput.actions["Move"];
        _lookAction       = _playerInput.actions["Look"];
        _jumpAction       = _playerInput.actions["Jump"];
        _sprintAction     = _playerInput.actions["Sprint"];
        _crouchAction     = _playerInput.actions["Crouch"];
        _attackAction     = _playerInput.actions["Attack"];
        _dashAction       = _playerInput.actions["Dash"];
        _interactAction   = _playerInput.actions["Interact"];
        _pauseAction      = _playerInput.actions["Pause"];
        _previousAction   = _playerInput.actions["Previous"];
        _nextAction       = _playerInput.actions["Next"];

        // UI
        _navigateAction     = _playerInput.actions["Navigate"];
        _submitAction       = _playerInput.actions["Submit"];
        _cancelAction       = _playerInput.actions["Cancel"];
        _advanceAction      = _playerInput.actions["Advance"];
    }

    private void UpdatePollingInputs()
    {
        // Gameplay
        Move         = _moveAction.ReadValue<Vector2>();
        Look         = _lookAction.ReadValue<Vector2>();
        JumpPressed  = _jumpAction.WasPressedThisFrame();
        JumpHeld     = _jumpAction.IsPressed();
        JumpReleased = _jumpAction.WasReleasedThisFrame();
        SprintHeld   = _sprintAction.IsPressed();
        CrouchHeld   = _crouchAction.IsPressed();
    }

    private void RegisterEventInputs()
    {
        _interactAction.performed     += HandleInteract;
        _attackAction.performed       += HandleAttack;
        _dashAction.performed         += HandleDash;
        _pauseAction.performed        += HandlePause;
        _previousAction.performed     += HandlePrevious;
        _nextAction.performed         += HandleNext;

        _navigateAction.performed     += HandleNavigate;
        _advanceAction.performed      += HandleAdvance;
        _submitAction.performed       += HandleSubmit;
        _cancelAction.performed       += HandleCancel;
    }

    private void UnregisterEventInputs()
    {
        _interactAction.performed     -= HandleInteract;
        _attackAction.performed       -= HandleAttack;
        _dashAction.performed         -= HandleDash;
        _pauseAction.performed        -= HandlePause;
        _previousAction.performed     -= HandlePrevious;
        _nextAction.performed         -= HandleNext;

        _navigateAction.performed     -= HandleNavigate;
        _advanceAction.performed      -= HandleAdvance;
        _submitAction.performed       -= HandleSubmit;
        _cancelAction.performed       -= HandleCancel;
    }

    // Gameplay handlers
    private void HandleInteract(InputAction.CallbackContext ctx)     => OnInteract?.Invoke();
    private void HandleAttack(InputAction.CallbackContext ctx)       => OnAttack?.Invoke();
    private void HandleDash(InputAction.CallbackContext ctx)         => OnDash?.Invoke();
    private void HandlePause(InputAction.CallbackContext ctx)        => OnPause?.Invoke();
    private void HandlePrevious(InputAction.CallbackContext ctx)     => OnPrevious?.Invoke();
    private void HandleNext(InputAction.CallbackContext ctx)         => OnNext?.Invoke();

    // UI handlers
    private void HandleNavigate(InputAction.CallbackContext ctx)     => OnNavigate?.Invoke();
    private void HandleAdvance(InputAction.CallbackContext ctx)      => OnAdvance?.Invoke();
    private void HandleSubmit(InputAction.CallbackContext ctx)       => OnSubmit?.Invoke();
    private void HandleCancel(InputAction.CallbackContext ctx)       => OnCancel?.Invoke();

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
