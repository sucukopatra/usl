public interface IInteractable
{
    bool CanInteract();
    void Interact();
    void OnFocusGained();
    void OnFocusLost();
}
