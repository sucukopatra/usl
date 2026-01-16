using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;
using UnityEngine.UI;
using System.Linq;

public class DialogueManager : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject dialogueUI;
    [SerializeField] private Animator dialogueAnimator;

    private TextMeshProUGUI speakerText;
    private TextMeshProUGUI dialogueText;
    private Image portraitImage;

    [Header("Characters")]
    [SerializeField] private Character[] characters;

    [Header("Audio")]
    [SerializeField] private float blipCooldown = 0.04f;

    private AudioSource voiceSource;
    private float lastBlipTime;

    [Header("Settings")]
    [SerializeField] private float textSpeed = 0.03f;

    [Header("Input")]
    [SerializeField] private InputAction advanceAction;

    private Dialogue[] lines;
    private int index;
    private Coroutine typingRoutine;
    private bool isTyping;

    public bool IsDialogueActive { get; private set; }

    private Character currentCharacter;

    private void Awake()
    {
        voiceSource = GetComponent<AudioSource>();

        speakerText  = dialogueUI.transform.Find("SpeakerText").GetComponent<TextMeshProUGUI>();
        dialogueText = dialogueUI.transform.Find("DialogueText").GetComponent<TextMeshProUGUI>();
        portraitImage = dialogueUI.transform.Find("PortraitImage").GetComponent<Image>();

        dialogueUI.SetActive(false);
    }

    private void OnEnable()
    {
        advanceAction.performed += OnAdvance;
        advanceAction.Enable();
    }

    private void OnDisable()
    {
        advanceAction.performed -= OnAdvance;
        advanceAction.Disable();
    }

    public void StartDialogue(Dialogue[] newLines)
    {
        if (IsDialogueActive || newLines == null || newLines.Length == 0)
            return;

        speakerText.text = "";
        dialogueText.text = "";
        portraitImage.sprite = null;

        IsDialogueActive = true;
        lines = newLines;
        index = 0;

        dialogueUI.SetActive(true);
        dialogueAnimator.SetBool("IsOpen", true);

        ShowLine();
    }

    private void OnAdvance(InputAction.CallbackContext _)
    {
        if (!IsDialogueActive)
            return;

        if (isTyping)
        {
            StopTyping();
        }
        else
        {
            index++;
            ShowLine();
        }
    }

    private void ShowLine()
    {
        if (index >= lines.Length)
        {
            EndDialogue();
            return;
        }

        Dialogue line = lines[index];
        speakerText.text = line.speaker.ToString();

        currentCharacter = GetCharacter(line.speaker);
        UpdatePortrait(line);

        typingRoutine = StartCoroutine(TypeText(line.text));
    }

    private void UpdatePortrait(Dialogue line)
    {
        if (!line.showPortrait || currentCharacter == null)
        {
            portraitImage.enabled = false;
            return;
        }

        Sprite sprite = GetPortrait(currentCharacter, line.expression);
        portraitImage.sprite = sprite;
        portraitImage.enabled = sprite != null;
    }

    private IEnumerator TypeText(string text)
    {
        dialogueText.text = "";
        isTyping = true;

        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];

            // Handle pause marker '|'
            if (c == '|')
            {
                yield return new WaitForSeconds(textSpeed * 10f);
                i++;
                continue;
            }

            // Handle TMP tags
            if (c == '<')
            {
                int tagEnd = text.IndexOf('>', i);
                if (tagEnd != -1)
                {
                    // Append the whole tag at once
                    dialogueText.text += text.Substring(i, tagEnd - i + 1);
                    i = tagEnd + 1;
                    continue;
                }
            }

            // Add visible character
            dialogueText.text += c;

            // Play blip for letters/numbers
            if (char.IsLetterOrDigit(c))
                TryPlayBlip();

            // Calculate delay
            float delay = textSpeed;

            // Extra delay for punctuation
            switch (c)
            {
                case ',':
                    delay += textSpeed * 3f;
                    break;
                case '.':
                case '!':
                case '?':
                    // Check for ellipsis
                    if (i + 2 < text.Length && text[i + 1] == '.' && text[i + 2] == '.')
                    {
                        delay += textSpeed * 10f; // longer pause
                        dialogueText.text += "..";
                        i += 2;
                    }
                    else
                    {
                        delay += textSpeed * 6f;
                    }
                    break;
                case ':':
                    delay += textSpeed * 4f;
                    break;
            }

            i++;
            yield return new WaitForSeconds(delay);
        }

        isTyping = false;
        typingRoutine = null;
    }

    private void StopTyping()
    {
        if (typingRoutine != null)
            StopCoroutine(typingRoutine);

        // Show the full line without the pause characters
        dialogueText.text = lines[index].text.Replace("|", "");

        isTyping = false;
        typingRoutine = null;
    }

    private void TryPlayBlip()
    {
        if (currentCharacter == null)
            return;

        if (currentCharacter.voiceBlips == null || currentCharacter.voiceBlips.Length == 0)
            return;

        if (Time.time - lastBlipTime < blipCooldown)
            return;

        voiceSource.pitch = Random.Range(
            currentCharacter.minPitch,
            currentCharacter.maxPitch
        );

        voiceSource.PlayOneShot(currentCharacter.voiceBlips[Random.Range(0,currentCharacter.voiceBlips.Length)]);
        lastBlipTime = Time.time;
    }

    private void EndDialogue()
    {
        IsDialogueActive = false;
        lines = null;

        dialogueAnimator.SetBool("IsOpen", false);

    }

    private Character GetCharacter(Speaker speaker)
    {
        foreach (var c in characters)
            if (c.speaker == speaker)
                return c;

        return null;
    }

    private Sprite GetPortrait(Character character, Expression expression)
    {
        return expression switch
        {
            Expression.Happy => character.happy,
            Expression.Angry => character.angry,
            Expression.Sad   => character.sad,
            _                => character.neutral
        };
    }
}

[System.Serializable]
public class Character
{
    public Speaker speaker;

    [Header("Portraits")]
    public Sprite neutral;
    public Sprite happy;
    public Sprite angry;
    public Sprite sad;

    [Header("Voice")]
    public AudioClip[] voiceBlips;
    [Range(0.8f, 1.2f)] public float minPitch = 0.95f;
    [Range(0.8f, 1.2f)] public float maxPitch = 1.05f;
}
