using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public enum InputDevice
{
    KeyboardMouse,
    Xbox,
    PlayStation,
    Switch
}

[CreateAssetMenu(fileName = "InputIconSet", menuName = "Input/Icon Set")]
public class InputIconSet : ScriptableObject
{
    [Serializable]
    public class ActionIcon
    {
        public InputActionReference action;
        public Sprite sprite;
    }

    [Header("Device Type")]
    public InputDevice device;

    [Header("Icons")]
    public List<ActionIcon> icons = new();

    private Dictionary<Guid, Sprite> _lookup;

    public Sprite GetSprite(InputActionReference actionRef)
    {
        if (actionRef == null || actionRef.action == null) return null;
        return GetSprite(actionRef.action.id);
    }

    public Sprite GetSprite(InputAction action)
    {
        if (action == null) return null;
        return GetSprite(action.id);
    }

    private Sprite GetSprite(Guid id)
    {
        if (_lookup == null) BuildLookup();
        return _lookup.TryGetValue(id, out var sprite) ? sprite : null;
    }

    private void BuildLookup()
    {
        _lookup = new Dictionary<Guid, Sprite>(icons.Count);
        foreach (var entry in icons)
            if (entry.action != null && entry.sprite != null)
                _lookup[entry.action.action.id] = entry.sprite;
    }

    // Cache is rebuilt automatically; call this if you modify icons at runtime
    public void InvalidateCache() => _lookup = null;

    private void OnValidate() => InvalidateCache();
}
