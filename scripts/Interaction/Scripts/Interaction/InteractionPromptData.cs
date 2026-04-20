using UnityEngine;
using UnityEngine.InputSystem;

public enum InteractionPromptDisplayMode
{
    ScreenSpace,
    AboveTarget
}

public class InteractionPromptData
{
    public string DisplayName { get; }
    public string ActionName { get; }
    public InputActionReference InputAction { get; }
    public InteractionPromptDisplayMode DisplayMode { get; }
    public Transform Anchor { get; }
    public Vector3 WorldOffset { get; }

    public InteractionPromptData(
        string displayName,
        string actionName,
        InputActionReference inputAction,
        InteractionPromptDisplayMode displayMode,
        Transform anchor,
        Vector3 worldOffset)
    {
        DisplayName = displayName;
        ActionName = actionName;
        InputAction = inputAction;
        DisplayMode = displayMode;
        Anchor = anchor;
        WorldOffset = worldOffset;
    }
}
