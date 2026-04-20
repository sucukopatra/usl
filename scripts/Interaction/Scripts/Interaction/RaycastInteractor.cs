using UnityEngine;
public class RaycastInteractor : Interactor
{
    [SerializeField] private Transform rayOrigin;
    [SerializeField] private float distance = 3f;
    [SerializeField] private LayerMask interactableLayers = 1;
    [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Collide;
    [SerializeField] private float scanInterval = 0f;

    private Transform Origin => rayOrigin != null ? rayOrigin : transform;
    protected override float ScanInterval => scanInterval;

    protected override IInteractable FindNextInteractable()
    {
        Transform origin = Origin;
        if (!Physics.Raycast(origin.position, origin.forward, out RaycastHit hit, distance, interactableLayers, triggerInteraction))
            return null;

        IInteractable interactable = hit.collider.GetComponentInParent<IInteractable>();
        if (interactable == null || !interactable.CanInteract())
            return null;

        return interactable;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Transform origin = Origin;
        Vector3 start = origin.position;
        Vector3 end = start + (origin.forward * distance);

        Gizmos.color = CurrentFocused != null ? Color.green : Color.yellow;
        Gizmos.DrawLine(start, end);
        Gizmos.DrawWireSphere(end, 0.05f);
    }
#endif
}
