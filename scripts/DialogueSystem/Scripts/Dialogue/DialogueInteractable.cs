using UnityEngine;

public class DialogueInteractable : Interactable
{
    [Header("Dialogue")]
    [SerializeField] private DialogueManager dialogueManager;

    [SerializeField, Tooltip("Use '|' inside dialogue lines to add a short pause. TMP tags like <i> and <b> are supported.")]
    private Dialogue[] dialogue;

    private bool hasPlayed = false;

    [Header("Optional Symbol")]
    [SerializeField] private GameObject interactionSymbol;
    [SerializeField] private Sprite hasNotInteractedIcon;
    [SerializeField] private Sprite hasInteractedIcon;

    private SpriteRenderer iconRenderer;

    private void Awake()
    {
        if (dialogueManager == null)
            dialogueManager = FindAnyObjectByType<DialogueManager>();

        if (interactionSymbol != null)
        {
            iconRenderer = interactionSymbol.GetComponent<SpriteRenderer>();
            RefreshIcon();
        }
    }

    public override bool CanInteract()
    {
        if (!base.CanInteract())
            return false;

        if (dialogueManager != null && dialogueManager.IsDialogueActive)
            return false;

        return true;
    }

    protected override bool HandleInteract()
    {
        if (DisableAfterInteract && hasPlayed)
            return false;

        if (dialogueManager == null)
        {
            Debug.LogWarning($"No DialogueManager assigned on {name}.", this);
            return false;
        }

        dialogueManager.StartDialogue(dialogue);
        hasPlayed = true;
        RefreshIcon();
        return true;
    }

    private void RefreshIcon()
    {
        if (iconRenderer == null)
            return;

        iconRenderer.sprite = hasPlayed ? hasInteractedIcon : hasNotInteractedIcon;

        if (interactionSymbol != null)
            interactionSymbol.SetActive(iconRenderer.sprite != null);
    }
}
