namespace PlexBot.Core.Services.LavaLink;

/// <summary>Teardown shared by kill and the inactivity timeout. The destroyed announcement replaces the live player card,
/// so it runs in a finally: a stop that throws must not leave a card whose buttons still work against a dead player.</summary>
internal static class PlayerTeardown
{
    /// <summary>Runs the teardown, then announces the player is destroyed. The exception from <paramref name="work"/>, if
    /// any, propagates unchanged after the announcement.</summary>
    public static async Task RunAsync(Func<Task> work, Action announceDestroyed)
    {
        try
        {
            await work();
        }
        finally
        {
            announceDestroyed();
        }
    }
}
