using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Attach to a UI Image. The action reference can be assigned in the Inspector or at runtime.
/// The sprite automatically updates whenever the player switches devices.
/// </summary>
[RequireComponent(typeof(Image))]
public class InputIconImage : MonoBehaviour
{
    [Tooltip("Used for standalone icons only. Leave empty if this image is driven externally via SetAction.")]
    [SerializeField] private InputActionReference action;

    private Image _image;
    private bool _isSubscribed;

    private void Awake() => _image = GetComponent<Image>();

    private void OnEnable()
    {
        TrySubscribeAndRefresh();
    }

    private void OnDisable()
    {
        if (_isSubscribed && InputDeviceDetector.Instance != null)
        {
            InputDeviceDetector.Instance.OnDeviceChanged -= Refresh;
            _isSubscribed = false;
        }
    }

    private void Update()
    {
        if (!_isSubscribed)
            TrySubscribeAndRefresh();
    }

    public void SetAction(InputActionReference actionReference)
    {
        action = actionReference;
        RefreshCurrent();
    }

    public void ClearAction()
    {
        action = null;
        HideImage();
    }

    private void Refresh(InputDevice device)
    {
        var detector = InputDeviceDetector.Instance;
        if (detector == null || action == null)
        {
            HideImage();
            return;
        }

        var sprite = detector.GetSprite(action, device);
        if (sprite != null)
        {
            _image.sprite = sprite;
            _image.enabled = true;
        }
        else
        {
            HideImage();
        }
    }

    private void HideImage()
    {
        _image.sprite = null;
        _image.enabled = false;
    }

    private void RefreshCurrent()
    {
        if (InputDeviceDetector.Instance == null)
        {
            HideImage();
            return;
        }

        Refresh(InputDeviceDetector.Instance.CurrentDevice);
    }

    private void TrySubscribeAndRefresh()
    {
        if (_isSubscribed)
        {
            RefreshCurrent();
            return;
        }

        if (InputDeviceDetector.Instance == null)
        {
            HideImage();
            return;
        }

        InputDeviceDetector.Instance.OnDeviceChanged += Refresh;
        _isSubscribed = true;
        Refresh(InputDeviceDetector.Instance.CurrentDevice);
    }
}
