using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

public class Interactable : MonoBehaviour, IInteractable, IHasInteractionPromptData
{
    [Header("Prompt UI")]
    [SerializeField] private string promptVerb = "Interact";
    [SerializeField] private InteractionPromptDisplayMode promptDisplayMode = InteractionPromptDisplayMode.ScreenSpace;
    [SerializeField] private Transform promptAnchor;
    [SerializeField] private Vector3 promptWorldOffset = new(0f, 0.8f, 0f);

    [Header("Input")]
    [SerializeField] private InputActionReference inputAction;

    [SerializeField] private bool isEnabled = true;
    [SerializeField] private bool disableAfterInteract = false;

    [SerializeField] private UnityEvent onInteract;
    [SerializeField] private UnityEvent onFocusGained;
    [SerializeField] private UnityEvent onFocusLost;

    public bool CanInteract() => isEnabled && gameObject.activeInHierarchy;

    public void Interact()
    {
        if (!CanInteract()) return;
        onInteract?.Invoke();

        if (disableAfterInteract)
            isEnabled = false;
    }

    public void OnFocusGained() => onFocusGained?.Invoke();
    public void OnFocusLost() => onFocusLost?.Invoke();

    public void EnableInteraction()
    {
        isEnabled = true;
    }

    public void DisableInteraction()
    {
        isEnabled = false;
    }

    public InteractionPromptData GetInteractionPromptData()
    {
        return new InteractionPromptData(
            gameObject.name,
            promptVerb,
            inputAction,
            promptDisplayMode,
            promptAnchor != null ? promptAnchor : transform,
            promptWorldOffset);
    }
}
