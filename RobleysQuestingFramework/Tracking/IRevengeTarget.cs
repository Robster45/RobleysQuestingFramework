namespace RobleysQuestingFramework
{
    /// <summary>
    /// Implement on a quest to request one persistent enemy promotion per quest instance.
    /// The framework handles owner routing, retries and level synchronization.
    /// </summary>
    public interface IRevengeTarget
    {
        ZDOID TargetID { get; }
    }
}
