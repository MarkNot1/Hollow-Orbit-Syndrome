public interface IPlacementState
{
    bool Enter();
    void UpdateState();
    void HandlePrimaryAction();
    void Exit();
}
