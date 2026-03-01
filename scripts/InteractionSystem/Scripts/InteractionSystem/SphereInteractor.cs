using UnityEngine;
using System.Collections.Generic;

public class SphereInteractor : Interactor
{
    [SerializeField] private float radius = 2f;
    [SerializeField] private LayerMask interactableLayers = 1;
    [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Collide;
    [SerializeField] private float scanInterval = 0f;

    private readonly Collider[] _buffer = new Collider[32];
    private readonly HashSet<IInteractable> _seen = new(32);
    protected override float ScanInterval => scanInterval;

    protected override IInteractable FindNextInteractable()
    {
        _seen.Clear();

        int count = Physics.OverlapSphereNonAlloc(transform.position, radius, _buffer,
                        interactableLayers, triggerInteraction);

#if UNITY_EDITOR
        if (count == _buffer.Length)
            Debug.LogWarning($"[SphereInteractor] Buffer full on {name} — results may be truncated.", this);
#endif

        IInteractable nearest = null;
        float best = float.MaxValue;
        Vector3 origin = transform.position;

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
