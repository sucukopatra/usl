using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

[DefaultExecutionOrder(1)] // Run after InputDeviceDetector
public class InputIconTester : MonoBehaviour
{
    [Header("Test Action")]
    [SerializeField] private InputActionReference testAction;

    [Header("Optional UI")]
    [SerializeField] private Image previewImage;
    [SerializeField] private TextMeshProUGUI deviceLabel;

    private void Start()
    {
        if (InputDeviceDetector.Instance == null)
        {
            Debug.LogError("[InputIconTester] InputDeviceDetector.Instance is null. Make sure it is in the scene.");
            return;
        }

        InputDeviceDetector.Instance.OnDeviceChanged += Refresh;
        Refresh(InputDeviceDetector.Instance.CurrentDevice);
    }

    private void OnDestroy()
    {
        if (InputDeviceDetector.Instance != null)
            InputDeviceDetector.Instance.OnDeviceChanged -= Refresh;
    }

    private void Refresh(InputDevice device)
    {
        var sprite = InputDeviceDetector.Instance.GetSprite(testAction, device);

        Debug.Log($"[InputIconTester] Device: {device} | Sprite: {(sprite != null ? sprite.name : "NULL - not mapped")}");

        if (deviceLabel != null)
            deviceLabel.text = device.ToString();

        if (previewImage != null)
        {
            previewImage.sprite = sprite;
            previewImage.enabled = sprite != null;
        }
    }

#if UNITY_EDITOR
    [ContextMenu("Simulate Keyboard & Mouse")]
    private void SimKB() => Refresh(InputDevice.KeyboardMouse);

    [ContextMenu("Simulate Xbox")]
    private void SimXbox() => Refresh(InputDevice.Xbox);

    [ContextMenu("Simulate PlayStation")]
    private void SimPS() => Refresh(InputDevice.PlayStation);

    [ContextMenu("Simulate Switch")]
    private void SimSwitch() => Refresh(InputDevice.Switch);
#endif
}
