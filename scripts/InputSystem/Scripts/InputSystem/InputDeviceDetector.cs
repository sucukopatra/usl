using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XInput;
using UnityEngine.InputSystem.DualShock;

[DefaultExecutionOrder(-1)]
public class InputDeviceDetector : MonoBehaviour
{
    public static InputDeviceDetector Instance { get; private set; }

    public InputDevice CurrentDevice { get; private set; } = InputDevice.KeyboardMouse;

    public event Action<InputDevice> OnDeviceChanged;

    [Header("Icon Sets — assign one per device type")]
    [SerializeField] private List<InputIconSet> iconSets = new();

    private PlayerInput _playerInput;
    private Dictionary<InputDevice, InputIconSet> _setLookup;

#if UNITY_EDITOR
    private bool _simulating;
#endif

    private void Awake()
    {
        Instance = this;
        _playerInput = GetComponent<PlayerInput>();

        _setLookup = new Dictionary<InputDevice, InputIconSet>();
        foreach (var set in iconSets)
            if (set != null)
                _setLookup[set.device] = set;
    }

    private void Update()
    {
#if UNITY_EDITOR
        if (_simulating) return;
#endif
        var detected = ResolveDevice();
        if (detected == CurrentDevice) return;

        CurrentDevice = detected;
        OnDeviceChanged?.Invoke(CurrentDevice);
    }

#if UNITY_EDITOR
    public void SimulateDevice(InputDevice device)
    {
        _simulating = true;
        CurrentDevice = device;
        OnDeviceChanged?.Invoke(CurrentDevice);
    }

    [ContextMenu("Stop Simulating")]
    public void StopSimulating() => _simulating = false;
#endif

    private InputDevice ResolveDevice()
    {
        foreach (var device in _playerInput.devices)
        {
            if (device is Gamepad)
                return ClassifyGamepad(device);
        }

        return InputDevice.KeyboardMouse;
    }

    private static InputDevice ClassifyGamepad(UnityEngine.InputSystem.InputDevice device)
    {
        // Check by type first (most reliable)
        if (device is DualShockGamepad) return InputDevice.PlayStation; // covers PS4 + PS5 if HID driver loaded

        if (device is XInputController) return InputDevice.Xbox;

        // Fall back to name/manufacturer string matching for DualSense and others
        // that may not map to a known class depending on Input System version or platform
        var name         = device.name?.ToLower()         ?? "";
        var manufacturer = device.description.manufacturer?.ToLower() ?? "";
        var product      = device.description.product?.ToLower()      ?? "";

        if (name.Contains("dualsense") || product.Contains("dualsense"))
            return InputDevice.PlayStation;

        if (name.Contains("dualshock") || product.Contains("dualshock"))
            return InputDevice.PlayStation;

        if (manufacturer.Contains("sony"))
            return InputDevice.PlayStation;

        if (name.Contains("xbox") || product.Contains("xbox") || manufacturer.Contains("microsoft"))
            return InputDevice.Xbox;

        if (name.Contains("switch") || product.Contains("switch") || manufacturer.Contains("nintendo"))
            return InputDevice.Switch;

        // Unknown gamepad — log so we can identify it
        return InputDevice.Xbox;
    }

    public Sprite GetSprite(InputActionReference actionRef) => GetSprite(actionRef, CurrentDevice);
    public Sprite GetSprite(InputAction action)             => GetSprite(action,     CurrentDevice);

    public Sprite GetSprite(InputActionReference actionRef, InputDevice device)
    {
        if (_setLookup.TryGetValue(device, out var set)) return set.GetSprite(actionRef);
        return null;
    }

    public Sprite GetSprite(InputAction action, InputDevice device)
    {
        if (_setLookup.TryGetValue(device, out var set)) return set.GetSprite(action);
        return null;
    }
}
