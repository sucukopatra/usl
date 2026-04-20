using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using UnityEngine.Serialization;

public class InteractionPrompt : MonoBehaviour
{
    [Header("References")]
    [FormerlySerializedAs("sphereInteractor")]
    [SerializeField] private Interactor interactorSource;
    [SerializeField] private GameObject promptPanel;
    [SerializeField] private TextMeshProUGUI actionText;
    [FormerlySerializedAs("buttonIcon")]
    [SerializeField] private InputIconImage buttonIconImage;
    [SerializeField] private Camera targetCamera;

    [Header("Animation")]
    [SerializeField] private float animDuration = 0.2f;
    [SerializeField] private Ease showEase = Ease.OutBack;
    [SerializeField] private Ease hideEase = Ease.InBack;

    private RectTransform _rect;
    private Canvas _canvas;
    private Vector2 _defaultAnchoredPosition;
    private Tween _currentTween;
    private InputActionReference _currentAction;
    private InteractionPromptDisplayMode _displayMode;
    private Transform _worldAnchor;
    private Vector3 _worldOffset;

    private void Awake()
    {
        _rect = promptPanel.GetComponent<RectTransform>();
        _canvas = _rect.GetComponentInParent<Canvas>();
        _defaultAnchoredPosition = _rect.anchoredPosition;
        promptPanel.SetActive(false);
    }

    private void OnEnable()
    {
        if (buttonIconImage != null)
            buttonIconImage.ClearAction();

        if (interactorSource != null)
        {
            interactorSource.FocusedInteractableChanged += HandleFocusedInteractableChanged;
            HandleFocusedInteractableChanged(interactorSource.FocusedInteractable);
        }
    }

    private void OnDisable()
    {
        if (interactorSource != null)
            interactorSource.FocusedInteractableChanged -= HandleFocusedInteractableChanged;
    }

    private void Show(InteractionPromptData interactionPromptData)
    {
        _currentAction = interactionPromptData.InputAction;
        _displayMode = interactionPromptData.DisplayMode;
        _worldAnchor = interactionPromptData.Anchor;
        _worldOffset = interactionPromptData.WorldOffset;
        actionText.text = $"{interactionPromptData.ActionName} {interactionPromptData.DisplayName}";
        actionText.ForceMeshUpdate();
        LayoutRebuilder.ForceRebuildLayoutImmediate(_rect);
        UpdatePromptPosition(forceDefaultPosition: true);
        if (buttonIconImage != null)
            buttonIconImage.SetAction(_currentAction);

        _currentTween?.Kill();
        promptPanel.SetActive(true);
        _rect.localScale = Vector3.zero;
        _currentTween = _rect.DOScale(Vector3.one, animDuration).SetEase(showEase);
    }

    public void Hide()
    {
        _currentAction = null;
        _worldAnchor = null;
        _displayMode = InteractionPromptDisplayMode.ScreenSpace;
        if (buttonIconImage != null)
            buttonIconImage.ClearAction();

        if (!promptPanel.activeSelf) return;
        _currentTween?.Kill();
        _currentTween = _rect.DOScale(Vector3.zero, animDuration)
            .SetEase(hideEase)
            .OnComplete(() => promptPanel.SetActive(false));
    }

    private void LateUpdate()
    {
        if (_currentAction == null) return;
        UpdatePromptPosition(forceDefaultPosition: false);
    }

    private void HandleFocusedInteractableChanged(IInteractable focusedInteractable)
    {
        if (focusedInteractable is IHasInteractionPromptData hasInteractionPromptData)
        {
            InteractionPromptData interactionPromptData = hasInteractionPromptData.GetInteractionPromptData();
            if (interactionPromptData != null)
            {
                Show(interactionPromptData);
                return;
            }
        }

        Hide();
    }

    private void UpdatePromptPosition(bool forceDefaultPosition)
    {
        if (_displayMode == InteractionPromptDisplayMode.ScreenSpace || _worldAnchor == null)
        {
            if (forceDefaultPosition)
                _rect.anchoredPosition = _defaultAnchoredPosition;
            return;
        }

        Camera worldCamera = ResolveCamera();
        if (worldCamera == null)
        {
            if (forceDefaultPosition)
                _rect.anchoredPosition = _defaultAnchoredPosition;
            return;
        }

        Vector3 screenPosition = worldCamera.WorldToScreenPoint(_worldAnchor.position + _worldOffset);
        if (screenPosition.z <= 0f)
        {
            if (promptPanel.activeSelf)
                promptPanel.SetActive(false);
            return;
        }

        if (!promptPanel.activeSelf)
            promptPanel.SetActive(true);

        _rect.position = screenPosition;
    }

    private Camera ResolveCamera()
    {
        if (targetCamera != null)
            return targetCamera;

        if (_canvas != null && _canvas.renderMode == RenderMode.ScreenSpaceCamera && _canvas.worldCamera != null)
            return _canvas.worldCamera;

        return Camera.main;
    }
}
