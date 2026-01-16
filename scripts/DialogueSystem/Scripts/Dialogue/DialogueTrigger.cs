using UnityEngine;
using UnityEngine.InputSystem;

public class DialogueTrigger : MonoBehaviour
{
    [SerializeField] private DialogueManager dialogueManager;
    [SerializeField] private GameObject interactionSymbol;
    [SerializeField] private Sprite hasNotInteractedIcon;
    [SerializeField] private Sprite hasInteractedIcon;

    [SerializeField] private bool playOnlyOnce = false;
    private bool hasPlayed;

    [SerializeField] private InputAction interactAction;
    [SerializeField, Tooltip( "Use '|' inside dialogue lines to add a short pause. TMP tags like <i> and <b> are supported for formatting.")]
    private Dialogue[] dialogue;

    private bool isPlayerNearby;
    private SpriteRenderer iconRenderer;

    private void Awake()
    {
        if (dialogueManager == null)
            dialogueManager = FindAnyObjectByType<DialogueManager>();

        if (interactionSymbol == null)
            return;

        iconRenderer = interactionSymbol.GetComponent<SpriteRenderer>();
        iconRenderer.sprite = hasNotInteractedIcon;

        interactionSymbol.SetActive(iconRenderer.sprite != null);
    }

    private void OnEnable()
    {
        interactAction.performed += OnInteract;
        interactAction.Enable();
    }

    private void OnDisable()
    {
        interactAction.performed -= OnInteract;
        interactAction.Disable();
    }

    private void OnInteract(InputAction.CallbackContext _)
    {
        if (!isPlayerNearby)
            return;

        if (playOnlyOnce && hasPlayed)
            return;

        dialogueManager.StartDialogue(dialogue);
        hasPlayed = true;

        if (iconRenderer == null)
            return;

        iconRenderer.sprite = hasInteractedIcon;
        interactionSymbol.SetActive(iconRenderer.sprite != null);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
            isPlayerNearby = true;
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
            isPlayerNearby = false;
    }
}
