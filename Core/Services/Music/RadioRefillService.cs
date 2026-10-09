using PlexBot.Core.Models.Media;
using PlexBot.Core.Services.LavaLink;
using PlexBot.Utils;

namespace PlexBot.Core.Services.Music;

/// <summary>Tops up an infinite radio queue in the background when it runs low. Called as each track starts.</summary>
public sealed class RadioRefillService(RadioSessionManager sessions, IPlayerService players)
{
    /// <summary>Queues the next radio batch if the session is infinite and the queue is below the threshold</summary>
    /// <param name="guildId">The Discord guild</param>
    /// <param name="queueCount">Tracks still waiting in the queue</param>
    /// <param name="cancellationToken">Cancels the refill</param>
    public async Task RefillIfNeededAsync(ulong guildId, int queueCount, CancellationToken cancellationToken = default)
    {
        RadioSession? session = sessions.GetSession(guildId);
        if (session is null)
            return;

        int threshold = BotConfig.GetInt("plex.radio.refillThreshold", 5);
        if (!RadioRefillPolicy.ShouldRefill(session.IsInfinite, queueCount, threshold, session.Refilling))
            return;
        if (!session.TryBeginRefill())
            return;

        // The slot stays held until the tracks are queued, so a second refill cannot fetch the same batch in the gap
        try
        {
            // Read the generation before fetching: a clear, stop or replace during the fetch makes the append drop the batch
            long generation = PlayerService.CurrentGeneration(guildId);
            Logs.Info($"Radio auto-refill for guild {guildId}: queue={queueCount}, threshold={threshold}");

            List<Track> fresh = await sessions.FetchRefillAsync(session, cancellationToken);
            if (fresh.Count == 0)
                return;

            int added = await players.AppendRadioTracksAsync(guildId, fresh, generation, cancellationToken);
            if (added > 0)
            {
                session.Remember(fresh);
                Logs.Info($"Radio auto-refill queued {added} tracks for guild {guildId}");
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down or the session ended: nothing to report
        }
        catch (Exception ex)
        {
            Logs.Error($"Radio auto-refill failed for guild {guildId}: {ex.Message}");
        }
        finally
        {
            session.EndRefill();
        }
    }
}
