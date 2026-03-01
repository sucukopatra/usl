using UnityEngine;
using System.Collections.Generic;

public class CircleInteractor : Interactor
{
    [SerializeField] private float radius = 2f;
    [SerializeField] private LayerMask interactableLayers;
    [SerializeField] private float scanInterval = 0f;

    private readonly Collider2D[] _buffer = new Collider2D[32];
    private readonly HashSet<IInteractable> _seen = new(32);
    protected override float ScanInterval => scanInterval;

    protected override IInteractable FindNextInteractable()
    {
        _seen.Clear();

        Vector2 origin = transform.position;
        ContactFilter2D contactFilter = new ContactFilter2D();
        contactFilter.useLayerMask = true;
        contactFilter.layerMask = interactableLayers;
        int count = Physics2D.OverlapCircle(origin, radius, contactFilter, _buffer);

#if UNITY_EDITOR
        if (count == _buffer.Length)
            Debug.LogWarning($"[CircleInteractor] Buffer full on {name}; results may be truncated.", this);
#endif

        IInteractable nearest = null;
        float best = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            var col = _buffer[i];
            if (!col) continue;

            var interactable = col.GetComponentInParent<IInteractable>();
            if (interactable == null || !interactable.CanInteract()) continue;
            if (!_seen.Add(interactable)) continue;

            float distSq = (col.ClosestPoint(origin) - origin).sqrMagnitude;
            if (distSq < best)
            {
                best = distSq;
                nearest = interactable;
            }
        }

        return nearest;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = CurrentFocused != null ? Color.green : Color.yellow;
        Gizmos.DrawWireSphere(transform.position, radius);
    }
#endif
}
