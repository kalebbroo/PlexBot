> **Status (2026-10-09): historical.** Written against the code before the playback refactor (#46). The `/ping` command
> it refers to has been removed. For the current pipeline, see [Plex-Playlist-Refactor-Plan.md](Plex-Playlist-Refactor-Plan.md).

# PlexBot Reliability Audit

Scope: the interaction pipeline (buttons, select menus, modals), player/queue service, radio and sonic services, the visual player progress loop, startup/reconnect, and the Lavalink/Plex plumbing. Evidence is from reading the code at the current `main` (3d39934). No live bot was run: there is no `.env`, Discord token, Lavalink node, or Plex server on this machine.

Each finding is marked:
- **Confirmed**: visible in the code.
- **Hypothesis**: plausible, needs the discriminating check listed.

---

## Part 1: What explains the screenshot

### 1. Radio / Replace Queue / Add to Queue fail on tracks where Similar Tracks works (Confirmed in code)

The user's clearest, deterministic complaint. The bot shows "No radio tracks were returned. This track may not have sonic analysis data." on a track where "Similar Tracks" returns 25.

- `GetRadioTracksAsync` (`Core/Services/PlexApi/PlexSonicService.cs:303`) builds candidates only from the seed track's own `Genre` and `Mood` tags, then pads with mood queries. It has no fallback.
- `GetSimilarTracksAsync` (`PlexSonicService.cs:196`) uses the same genre lookup but then pads with same-artist tracks (`{grandparentKey}/allLeaves`). That is why it still returns results.
- If both genre and mood lists are empty, radio returns `[]` and the handler shows the "no sonic data" warning. If metadata is missing, it returns `[]` silently (`PlexSonicService.cs:313-316`), so an error-like condition looks like "no data."

**Likely cause:** on many Plex servers, Genre is stored on the album or artist, not the track. The track's own `Genre` array is empty, so radio gets nothing, while similar survives via the artist fallback. This fits the screenshot exactly.

**Discriminator:** on the Plex server, fetch `/library/metadata/<ratingKey>` for the seed track. If `Genre` and `Mood` are absent on the track but present on its album/artist, this is the cause. The logs will also show `Radio generated 0 tracks` if the call succeeded but got nothing.

**Fix:**
- Fall back to album (`parentKey`) and artist (`grandparentKey`) genres.
- Add the same-artist padding that similar tracks already has.
- Return a distinct error when metadata fetch fails, instead of `[]`, so the user sees "Plex request failed" rather than "no sonic data."
- Only show "no sonic analysis" when Sonic Analysis is actually absent on the seed.

### 2. "No active player found" and "not connected" appear intermittently (Hypothesis, two candidates)

The code cannot tell these apart at runtime, which is itself the first problem:

- `GetPlayerAsync` (`Core/Services/LavaLink/PlayerService.cs:17-73`) sends its own followup for two failure cases (lines 23 and 63), and returns `null`. Callers then send a second "No active player found" followup. Users can see two messages.
- The status mapping (lines 57-62) collapses everything that isn't `UserNotInVoiceChannel` or `BotNotConnected` into "An unknown error occurred", and the calling handlers then show the generic "No active player." The actual `PlayerRetrieveStatus` is never logged.

**Candidate A: the Lavalink player no longer exists.** `visualPlayer.inactivityTimeout` (default 2.0 minutes, `ServiceRegistration.cs:129`) disconnects idle players. The next button press then finds no player. The screenshot's "The bot is currently not connected to a voice channel" fits this.

**Candidate B: voice-channel mismatch or stale voice cache.** `user.VoiceChannel` comes from the gateway cache. If it is stale, or the bot's player is in a different channel, retrieval fails for reasons unrelated to whether the player is alive.

**Discriminator (needs the user's logs):** log `result.Status` (see Hardening H1). If it says `BotNotConnected`, it is A. If it says anything else, it is B. Also check whether the Lavalink node still has a player for the guild at the time of failure.

### 3. 10062 "Unknown interaction" and 10060 "already acknowledged" (Hypothesis, two candidates)

The screenshot shows both, intermittently, on any button.

- **Confirmed in code:** every handler defers exactly once before doing work, and nothing in-process double-acks. The 3-second acknowledgement window is enforced by Discord, not by the host clock.
- **Confirmed in code:** `RunMode.Async` is set (`ServiceRegistration.cs:80`). Per Discord.NET's docs, async mode runs the handler off the gateway thread. Errors from handlers therefore do not come back through `HandleInteractionAsync`'s `!result.IsSuccess` path. Several handlers call `DeferAsync` outside their `try` (`MusicInteractionsHandler.cs:94`, `114-116`, `234`, `272`, `306`, `139-145`). If that call throws, the user gets no fallback message at all. Verify by subscribing to `ButtonExecuted` / `SlashCommandExecuted` style events, which the code does not currently do.

**Candidate A: a second bot process is connected with the same token.** Discord delivers each interaction to every gateway session. Whichever instance defers first wins; the other gets 10060. The losing instance may have no voice connection, which would produce "not connected" and "No Player" at random. `Install/linux-install.sh` only runs `docker compose -p plexbot down` and `up`, so it won't stop a `dotnet run` or a second host. Check: `docker ps`, and any local `dotnet` process, on every machine that has this `.env`.

**Candidate B: gateway or handler latency past 3 seconds.** The gateway can be delayed if a handler awaited on the gateway thread runs long. `Ready` is the prime suspect: `ReadyAsync` (`EventHandler.cs:38`) awaits `RegisterCommandsGloballyAsync` and `SetGameAsync` on every reconnect, and Discord.NET 3.20 invokes `Ready` on the gateway path. Discord.NET's `HandlerTimeout` (default 3000 ms) logs a warning when a handler exceeds it.

**Discriminators (needs the user's logs):**
- Search the logs for `HandlerTimeout` or any Discord.NET slow-handler warning. Hits support B.
- Look at the `Interaction received: ... elapsed=` debug lines (`EventHandler.cs:158`). Large values support B. Caveat: `elapsed` is computed from the host clock (see Clock section), so treat it as approximate.
- For a 10060, find the same interaction ID in the logs. Two instances both logging it supports A.

### 4. Progress clock drifts or looks wrong (Confirmed in code, cause is mostly rate limiting and sampling)

- The progress loop (`Core/Discord/Embeds/VisualPlayer.cs:153-203`) edits the player message once per second (`Task.Delay(1s)` at line 159, `ModifyAsync` at line 185). That is about the limit of Discord's per-channel message-edit rate. Track-change and button edits share the same bucket. When Discord.NET queues a request for rate limiting, the loop waits, so the display falls behind.
- The displayed position comes from `player.Position`, which Lavalink only reports every `playerUpdateInterval: 3` seconds (`Install/Docker/lavalink.base.yml:18`). The bar therefore advances in 3-second steps and is never extrapolated between updates.

**Fix:** extrapolate position from the last reported value using a monotonic `Stopwatch`, while the player is `Playing`. Edit the message every 5 seconds, not every second. This removes the rate-limit pressure and keeps the bar smooth.

---

## Part 2: Other confirmed defects (ordered by impact)

### High
- **Event subscriptions duplicate on every reconnect.** `EventHandler.cs:108-128` subscribes to `TrackStarted`, `TrackEnded`, and `PlayerDestroyed` inside `ReadyAsync`, which runs on every `Ready`. Each reconnect adds another subscriber, so `SetGameAsync` and any extension handler run multiple times per event. The `_modulesRegistered` guard covers only module registration.
- **Global command registration runs on every `Ready`** (`EventHandler.cs:99`). Discord limits bulk-overwrite of global commands per day; a reconnect storm can exhaust it. A failure here is caught and logged, but `_modulesRegistered` stays true, so commands are never retried until restart.
- **Queue mutation has no per-guild lock.** `PlayerService.AddToQueueAsync` checks `player.State` then plays (`PlayerService.cs:130-135`). Two concurrent adds can both see "not playing" and both call `PlayAsync`. `HandleRadioReplaceAsync` clears the queue (`MusicInteractionsHandler.cs:401-402`) and then adds; if the add fails, the queue is left empty.
- **Lavalink/Plex calls have no timeout in the resolver.** `TrackResolverService.ResolveTrackAsync` (lines 36-41) calls `LoadTrackAsync` with no timeout. If the Lavalink node stalls, the deferred interaction waits until Discord's 15-minute token expires, and the user sees "thinking" forever.
- **Startup waits a fixed 2 seconds instead of for `Ready`.** `BotHostedService.cs:80` (`Task.Delay(2000)`) before `GetChannel`. If the guild cache isn't populated yet, the static player channel is silently skipped (`BotHostedService.cs:82-86`) and never retried.
- **A failed `LoginAsync` or Lavalink start ends the process.** `BotHostedService.StartAsync` rethrows (`:57-60`). `PlexBotMain.Main` catches it and returns, so the process exits. Compose's `restart: unless-stopped` will restart it, but there is no backoff and no retry around Discord login or Lavalink connection.

### Medium
- **Sonic Adventure button calls `GetPlayerAsync` before acknowledging.** `MusicInteractionsHandler.cs:617-629` does Lavalink player retrieval before `RespondWithModalAsync`. Any slow path there is exposed to the 3-second window, with no defer and no fallback.
- **`GetPlayerAsync` sends followups on the caller's behalf.** `PlayerService.cs:23` and `:63` respond before the handler has decided how to respond. In the Sonic Adventure path the interaction is not yet acknowledged, so those followups fail, and the handler's own response then fails too.
- **Single global visual player.** `VisualPlayerStateManager` holds one `CurrentPlayerChannel` and one `CurrentPlayerMessage` for the whole process (`ServiceRegistration.cs:155`). The screenshot shows several bot channels in one guild (`plex-bot`, `baib_ross`, `strahd`, `static-plex-bot`). Each `AddToQueueAsync` overwrites `CurrentPlayerChannel` (`PlayerService.cs:98`), so the player message moves between channels and the progress loop edits whichever message was last set. This is a plausible cause of "inconsistent" behavior.
- **HTTP retry replays non-idempotent requests.** `HttpClientWrapper` retries `POST` and `PUT` on `HttpRequestException`. Playlist creation could be duplicated if the first request reached Plex but the response was lost.
- **Compose `depends_on: lavalink`** only waits for the container to start, not for Lavalink to be ready (`Install/Docker/docker-compose.yml`). Add a healthcheck to Lavalink and `condition: service_healthy`.
- **Event bus handler exceptions are logged but not isolated from the publisher**: `PublishAsync` is fired with `_ =` in several places (`CustomLavaLinkPlayer.cs:35`, `:39`). Failures are swallowed silently by design, which is fine, but nothing counts them.

### Low
- **Cooldown is set before the action runs** (`MusicInteractionsHandler.cs:884-911`). A legitimate second click within 2 seconds (for example Next then Previous) is silently dropped, and the user sees nothing.
- **Interaction `elapsed` debug log uses the host clock** (`EventHandler.cs:157-158`). It is labelled as a deadline measurement but is not reliable for that (see Clock).
- **Leftover diagnostic at Info level**: `MusicCommands.cs:560-562` logs "Ping: about to DeferAsync". Useful for the user's test, but it should be Debug.

---

## Part 3: Hardening plan (in order)

1. **H1: Log the real player status.** In `GetPlayerAsync`, log `result.Status`, guild ID, voice channel ID, and whether the Lavalink player exists. Remove the duplicate followups from `GetPlayerAsync`; let callers respond once. This is the cheapest way to settle the "No Player" question.
2. **H2: Log instance ID and interaction timing from Discord time.** Generate a per-process ID at startup and include it in every interaction log line. On a 10060, log "another instance may be running." Measure deadline with `Stopwatch` from receipt, not the host clock.
3. **H3: Unified interaction wrapper.** One helper that: defers first (inside try), runs the body, and on any exception sends exactly one fallback message via `FollowupAsync` if acknowledged or `RespondAsync` if not. Replace the ad-hoc `DeferAsync` calls. Subscribe to the InteractionService `*Executed` events so async-mode failures are logged and reported.
4. **H4: Fix the Ready path.** Move `Subscribe` calls and command registration behind a one-time guard (or into a separate `SetupOnce` method). Retry command registration with backoff on failure. Move `Ready` work off the gateway thread (`Task.Run`) so it cannot delay interactions.
5. **H5: Radio fallback chain.** Genre (track, then album, then artist), then mood, then same-artist padding. Distinguish error from empty. Only show the sonic-analysis message when the seed has no sonic data.
6. **H6: Per-guild player lock** around `AddToQueueAsync`, `StopAsync`, and radio replace/clear. A `SemaphoreSlim` per guild ID. Check state under the lock, then play.
7. **H7: Timeouts everywhere external.** Put a `CancellationTokenSource` timeout (for example 8 seconds) on Lavalink `LoadTrackAsync`, and on Plex requests in the interaction path. On timeout, show "Lavalink/Plex did not respond" instead of hanging.
8. **H8: Progress bar.** Extrapolate position from `Stopwatch`; edit every 5 seconds; skip edits when the message content is unchanged.
9. **H9: Visual player per guild.** Key `CurrentPlayerChannel` / `CurrentPlayerMessage` by guild ID (a dictionary), so multiple channels in one guild can't overwrite each other.
10. **H10: Startup and reconnect.** Await `Ready` (a `TaskCompletionSource`) before static-channel setup, with a timeout and retry. Retry `LoginAsync` and the Lavalink connection with exponential backoff instead of exiting. Add a Lavalink healthcheck and `condition: service_healthy` in compose.
11. **H11: Retry policy.** Retry only idempotent methods (GET, and PUT/DELETE where Plex treats them idempotently). Do not retry POST on connection errors unless the caller opts in.
12. **H12: Cooldown after success, not before.** Or drop the cooldown for buttons that are already idempotent (pause, repeat).

---

## Part 4: About the clock

Two different things get called "the clock," and only one is relevant to the errors:

- **Discord's 3-second window** is enforced on Discord's side against the interaction's snowflake timestamp. The bot's host clock does not affect whether an acknowledgement is accepted. Host drift cannot by itself produce 10062.
- **Host clock drift still matters for:**
  - TLS certificate validation (connections to Discord, Plex, and Lavalink can fail outright when the clock is far off).
  - The `elapsed` debug line and cooldown timestamps, which use `DateTime.UtcNow` and host time.
  - Log timestamps, which make correlating events across machines hard.

Recommendation: run NTP/chrony on the host, and verify with `timedatectl`. Do not add application-level clock sync; it cannot fix the 3-second window. The code already sets `UseSystemClock = false` and `UseInteractionSnowflakeDate = false` (`ServiceRegistration.cs:67-72`), which is the correct direction. That setting also means the library no longer rejects stale interactions locally, so every late interaction becomes a Discord-side 10062 instead.

---

## Part 5: What I need from the user

To settle Parts 1.2 and 1.3, the most useful evidence is the bot's log file (`logs/plex-bot-*.log`) covering:
- one "No Player" or "not connected" event (with the surrounding `Interaction received ... elapsed=` lines),
- one 10060 or 10062 event,
- any `HandlerTimeout` or slow-handler warnings,
- the output of `docker ps` on every machine that has this `.env`.

Also useful: run `/ping` in Discord while the bot is idle and during playback. The existing `/ping` command (`MusicCommands.cs:560`) reports whether the defer pipeline works.

## Limits of this audit

- The bot was not run. Behavior claims are from code reading, not a live Discord session.
- Lavalink4NET XML docs are not in the local NuGet cache, so `BotNotConnected` and `PlayerRetrieveStatus` semantics are unverified.
- The Plex track-metadata claim (Genre on album/artist, not track) needs one API check on the user's server.
