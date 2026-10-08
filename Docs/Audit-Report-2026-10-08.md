# Reliability audit: fixes and test report (2026-10-08)

Branch: `fix/reliability-audit`. Companion to [Reliability-Audit.md](./Reliability-Audit.md), which has the original findings. This report covers what was changed, what was tested live in Discord, what broke during testing, and what is still open.

Environment: the bot ran in Docker (PlexBot + Lavalink) on this machine, logged in as the Plex Bot application. Testing was done in the KalebKorp `#testing` channel and the `Plex Bot Test` voice channel, with the microphone muted. The Plex server and the PlexRequests bridge are reached at the addresses configured in the local `.env` and `config.fds` (not recorded here).

## Summary

| Area | Status |
|---|---|
| H1 Player lookup: one response, real status logged | Done |
| H2 Instance ID in logs, host-clock labelled as estimate | Done |
| H3 Central fallback for failed commands (async run mode) | Done, verified (`/request search` produced "Command Error" instead of silence) |
| H4 Ready path: setup once, off the gateway thread, command registration retry | Done |
| H5 Radio: genre fallback to album/artist, same-artist padding, errors not empty lists | Done. Verified: radio generated 30 tracks on the seed |
| H6 Per-guild queue lock, replace clears only after first track resolves | Done. Verified: replace and play-all ran |
| H7 Timeout on Lavalink track loads (20 s) | Done |
| H8 Progress: 5 s edits, extrapolated position | Done. Verified: bar renders |
| H9 Visual player state per guild | Done (compile-verified; single-guild test only) |
| H10 Startup: wait for Ready, retry login and Lavalink with backoff | Done. Verified: clean start after restart |
| H11 No retry on POST | Done |
| H12 Cooldown set after success | **Not done.** Needs a product decision (see Open items) |
| Lavalink password from `.env` ignored by compose | Found and fixed during testing |
| Plex token written to logs | Found and fixed during testing (redacted at the logger) |

## Changes

**Interaction and player (Core/):**
- `PlayerService.cs`: `TryGetPlayerAsync` returns a reason and sends no Discord response. Callers respond once. `AddTracksAsync` and `ReplaceQueueAsync` run under a per-guild `SemaphoreSlim`. Clear, add and replace share the lock. `ClearQueueAsync` is new and used by the queue panel's Clear.
- `EventHandler.cs`: instance ID in every interaction log line. Central fallback via `SlashCommandExecuted`, `ComponentCommandExecuted`, `ModalCommandExecuted` and `ContextCommandExecuted`. One-time setup runs off the gateway thread. Command registration retries with backoff.
- `VisualPlayer.cs` and `PlayerOptionsModel.cs`: visual player state is keyed by guild. Progress edits every 5 s. Position is extrapolated from a monotonic clock between Lavalink reports.
- `PlexSonicService.cs`: radio genre lookup falls back to album and artist. Radio falls back to same-artist tracks. A failed seed lookup throws instead of returning an empty list.
- `TrackResolverService.cs`: Lavalink track loads have a 20 s deadline.
- `HttpClientWrapper.cs`: POST is never retried.
- `Logs.cs`: `X-Plex-Token=` values are redacted before any output or file write.
- `BotHostedService.cs`: Discord login retries in the background with backoff. Lavalink start retries. Static channel setup waits for Ready and polls the cache, with no fixed 2 s delay.

**Deployment (Install/):**
- `Install/start.sh` (new): starts the PlexBot stack (bot and Lavalink) only. Flags `--build`, `--stop`, `--logs`. `--build` now also force-recreates the bot container.
- `Install/Docker/docker-compose.yml`: removed the `LAVALINK_SERVER_PASSWORD` override that hid `.env` values. All compose calls now pass `--env-file ../../.env`, in `start.sh` and `linux-install.sh`.
- `Install/Docker/startup.sh`: copies `config.fds` on every start, not only when it's missing.

**Not in this PR:** `Extensions/PlexRequestsBridge/RequestCommands.cs`. The `Extensions/` folder is gitignored (`.gitignore:158`). The fix is on disk: `results.DistinctBy(r => (r.MediaId, r.MediaType))`. See Open items.

## Live test results

Commands:

| Command | Result |
|---|---|
| `/ping` | Pong. Defer took about 250 ms. |
| `/help` | Shows the Plex Bot help card. |
| `/search library queen` | Found 1 artist, 12 albums, 13 tracks. Picking "The Queen" from the menu queued it. |
| `/search` with a wrong `mode` value | "Unknown Source" error. Correct behavior for bad input. |
| `/play Bohemian Rhapsody` | Playing. Lavalink confirmed the position advancing (10.6 s, then 16.7 s, not paused). |
| `/play Under Pressure`, `/play Queen` | Queued ("Added to queue"). |
| `/playlist Kpop` | Loading, "Resolved 1/92 tracks". Playing "Really" by BLACKPINK. Many tracks failed to resolve (see Open items). |
| `/request search Dune` | Top match "Dune (2021), already on Plex". Failed before the fix, works after. |
| `/request status` | "Your Discord isn't linked yet." Correct. |
| `/request add`, `/request link` | **Not run.** `add` creates a real request, and `link` needs a code from your Plex Requests profile. |
| `/search mood`, `genre`, `station` | **Not tested.** |

Buttons on the visual player:

| Control | Result |
|---|---|
| Pause, resume | Logged "Playback paused / resumed by kalebbroo". |
| Skip | Went from Bohemian Rhapsody to Under Pressure. |
| Volume up / down | Lavalink volume 20, then 30, then 20. |
| Repeat | None to Queue (logged). |
| Queue Options: View | "Current Music Queue" shown. |
| Queue Options: Shuffle | "Shuffled 2 tracks." |
| Queue Options: Clear | "Removed 2 tracks from the queue." |
| Similar tracks (magnifier) | "Found 25 sonically similar tracks." |
| Play All Similar | "Added 15 of 25", 10 failed (see Open items). |
| Sonic adventure (compass) | Logged "Bohemian Rhapsody to The Queen, 14 tracks". The ephemeral result wasn't visible in Chrome, so this is confirmed from the log only. |
| Radio (📻) → Replace Queue | 30 radio tracks generated. |
| Radio → Add to Queue, Similar | **Not tested.** |
| Kill | "Player killed by kalebbroo". Bot left voice. |

Progress bar: after the emoji IDs were added to `config.fds`, the log says `Using custom Discord emoji (all 30 IDs loaded)`. The bar shows the custom images.

## Bugs found during testing (fixed)

1. **Lavalink password override.** Compose's `environment:` line set `LAVALINK_SERVER_PASSWORD` to a default, which overrode `env_file`. Lavalink always ran with the default password, and changes in `.env` did nothing. Fixed by removing the line and passing `--env-file`. Verified: the container's password hash matches `.env`.
2. **`config.fds` edits needed a rebuild, not a restart.** `startup.sh` copied the file only when it was missing, so edits to the emoji IDs didn't reach the running container until it was recreated. Fixed by copying on every start. (This needed an image rebuild, since `startup.sh` is baked into the image.)
3. **`start.sh --build` didn't replace the running container.** The image rebuilt, but Compose kept the old container, so the old extension code kept running. Fixed with `--force-recreate`. The `DistinctBy` fix did not reach the bot until this was caught.
4. **Plex token in logs.** Playback URLs include `X-Plex-Token`, and they were written in full to the console and the log file. Fixed by redacting in the logger. Verified: no token lines after the restart.
5. **Request search "Command Error".** A select menu with duplicate option values made Discord reject the response (`COMPONENT_OPTION_VALUE_DUPLICATED`). Fixed in the local extension (see Not in this PR). Verified after the container recreate.
6. **Duplicate event subscriptions on reconnect.** Found in code review, not reproduced live. Setup now runs once per process.

## Open items

1. **Plex playback failures (high impact).** Many tracks fail to load in Lavalink. Lavalink logs show `Premature end of Content-Length delimited message body (received: 0)` and `Could not read the file for detecting file type`. In the Kpop playlist, about 70 of 92 failed. In Play All Similar, 10 of 25 failed (all Christmas-album files). The failures aren't tied to one format: MP3, FLAC, M4A and Opus all failed. The likely cause is the Plex server returning empty bodies under concurrent requests. The bot resolves up to 3 tracks at once (`plex.maxConcurrentResolves`). **Options:** lower the concurrency, add backoff on failure, or switch to Plex's universal transcoder (MP3 or Opus URLs). This needs your decision.
2. **Plex token still in old logs.** Log files written before the redaction fix still contain the token, and the token was also pasted into chat earlier. Rotate the Plex token and delete `logs/plex-bot-2026-10-08.log` and older logs.
3. **PlexRequests fix is not in the PR.** Apply it to the local `Extensions/PlexRequestsBridge/RequestCommands.cs`, and decide whether `Extensions/` should be tracked.
4. **H12 (cooldown after success) not done.** It changes which clicks count as spam. Decide whether that's wanted.
5. **Command registration rate limit.** Each restart re-registers guild commands. Restarting several times in a few minutes hit Discord's rate limit (about 32 s). Registration should run only when the command definitions change.
6. **Stale global commands.** Plex Bot still has global `/ping`, `/search` and related entries from an earlier production-mode registration, so the command picker shows duplicates. bAIb Ross also shows its own `/request` entries that don't respond. The stale Plex Bot entries are harmless. To remove them, set Plex Bot's global command list to empty, or I can do it on request.
7. **Not tested:** `/search mood`, `genre`, `station`; radio Add to Queue and Similar; `/play` with a URL; multi-guild behavior (only KalebKorp was exercised).
8. **Font warning.** `NotoSansCJK-Regular.ttc: Table 'name' is missing` appears on each render. It's harmless but noisy.

## What was needed from you

- Decide on the Plex playback fix (open item 1).
- Rotate the Plex token and clear old logs (open item 2).
- Decide how to handle the PlexRequests extension (open item 3).
- Decide on H12 (open item 4).
