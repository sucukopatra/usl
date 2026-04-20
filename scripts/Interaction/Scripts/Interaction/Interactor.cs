using System;
using UnityEngine;

public abstract class Interactor : MonoBehaviour
{
    private IInteractable _focused;
    private float _scanTimer;

    public event Action<IInteractable> FocusedInteractableChanged;
    public IInteractable FocusedInteractable => _focused;

    protected IInteractable CurrentFocused => _focused;
    protected abstract float ScanInterval { get; }
    protected abstract IInteractable FindNextInteractable();

    protected virtual void OnEnable()
    {
        if (InputManager.Instance != null)
            InputManager.Instance.OnInteract += StartInteraction;
    }

    protected virtual void OnDisable()
    {
        if (InputManager.Instance != null)
            InputManager.Instance.OnInteract -= StartInteraction;

        if (_focused != null)
        {
            _focused.OnFocusLost();
            _focused = null;
            FocusedInteractableChanged?.Invoke(null);
        }
    }

    protected virtual void Update()
    {
        if (ScanInterval > 0f)
        {
            _scanTimer -= Time.deltaTime;
            if (_scanTimer > 0f) return;
            _scanTimer = ScanInterval;
        }

        UpdateFocus(FindNextInteractable());
    }

    protected void UpdateFocus(IInteractable nextFocused)
    {
        bool focusedIsDestroyed = _focused is UnityEngine.Object unityObj && unityObj == null;
        if (!focusedIsDestroyed && ReferenceEquals(_focused, nextFocused)) return;

        _focused?.OnFocusLost();
        _focused = nextFocused;

        if (_focused != null)
            _focused.OnFocusGained();

        FocusedInteractableChanged?.Invoke(_focused);
    }

    private void StartInteraction()
    {
        if (_focused != null && _focused.CanInteract())
            _focused.Interact();
    }
}
