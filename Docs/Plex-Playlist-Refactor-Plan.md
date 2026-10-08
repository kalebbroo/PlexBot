# Plex playback pipeline refactor

Status: implemented on `refactor/plex-playlist-pipeline`. This page records why tracks failed, what changed, and how it was checked.

## Why tracks failed

There were two causes. Codecs were not one of them: flac, mp3, m4a and opus all load when Plex returns the file.

### 1. Plex drops file requests when several run at once

Lavalink's loads of the 77 parts that failed on 2026-10-08, all made directly against Lavalink `/v4/loadtracks`:

| Concurrency | Loaded |
|---|---|
| 1 (sequential), 20 parts | 20/20 |
| 3 (the old default), 40 parts | 37/40 |
| 10, 40 parts | 27/40 |
| Failures retried one at a time after 2 s | 7/7 |

Plex sends response headers, then closes the connection with no body. In the Lavalink log this shows as `Could not read the file for detecting file type`, caused by `Premature end of Content-Length delimited message body (expected: N; received: 0)`. Occasionally Lavalink reports `Unknown file format` instead. The REST response only says `severity: fault, "Something went wrong while looking up the track."`, so the bot can't tell these cases apart. Every load error is treated as retriable. A load with no match is the only permanent failure.

The m4a and FLAC probes also seek with ranged requests, so one load can be several Plex requests. Lavalink then fetches the file again from the start when it plays.

### 2. The old retry could never succeed

Lavalink4NET caches failed loads for 30 minutes in its default `CacheMode.Dynamic`. The old retry pass ran 2 seconds after the first pass, and got the cached failure back without asking Lavalink again. That is why the 2026-10-08 run recovered 0 of 77.

## What changed

### Phase 1: shared throttle and retries
- **`PlexStreamGate`** (singleton, all guilds):
  - caps concurrent Plex loads (`plex.stream.maxConcurrentLoads`, default 2)
  - holds a shared cooldown: a failed load sets one, and every caller waits for it, so retries don't recreate the burst
- **`TrackResolverService`**:
  - Plex loads go through the gate with `CacheMode.Refresh`, and retry after 2, 5 and 15 s (`plex.stream.retryDelaysSeconds`)
  - every failure is logged with the part ID, attempt number and Lavalink severity, and never the token
- **Resolve cache**:
  - keyed by Plex part key (`Track.PartKey`, from `Media[0].Part[0].key`), never a URL with a token
  - entries expire after 60 minutes (`plex.resolveCacheMinutes`)
  - eviction runs in batches rather than sorting the whole cache on every insert
- **Play-time retry** in `CustomLavaLinkPlayer`:
  - `NotifyTrackExceptionAsync` logs the cause
  - a `LoadFailed` end on a Plex track is replayed once after the backoff
  - if it fails again, the track is skipped with a short notice in the channel
  - the replay is dropped if the queue was cleared, stopped or replaced meanwhile (the generation check from PR #45)

### Phase 2: just-in-time resolution
- **Placeholders**: `AddTracksAsync` resolves only the first track, then adds the rest as `CustomTrackQueueItem` placeholders. A placeholder holds the Plex metadata, and its `TrackReference` holds only the URL.
  - The batch releases its place in the request order straight away, so a later `/play` isn't held behind a 92-track playlist.
- **`QueueResolveService`** runs one worker per guild:
  - each pass resolves the unresolved items among the first `plex.stream.resolveAhead` (default 3)
  - it re-reads the queue every pass, so shuffle, clear and replace never leave it holding stale positions
  - it's woken when a track starts, when tracks are added, and on shuffle, and also polls every 10 s
- **The next track before playback**:
  - skip waits up to 10 s for the next item to resolve
  - the end of a track waits up to 5 s
  - an item that fails every retry is removed, with a notice in the channel
  - if a placeholder still reaches the player unresolved, Lavalink loads the URL itself, and the play-time retry goes through the resolver
- **Visual player**: updates are serialised per guild. A track start and the batch's queue refresh could otherwise both send a new player message.
- **Play All Similar**: no longer sends a second "Tracks Added" follow-up.

## Live results (KalebKorp, Plex Bot Test voice channel)

| Check | Before | After |
|---|---|---|
| `/playlist Kpop` (93 tracks), Phase 1 only | about 70 of 92 failed | 92/92 resolved. 22 failed the first attempt; 17 recovered on the 2nd, 5 on the 3rd |
| `/playlist Kpop`, Phase 2 | answered after about 2 min of resolving | answered at once: "Playing Pink Venom, and queued 92 more" |
| Plex loads over 6 minutes of playlist, 4 skips, a shuffle, `/play`, Play All Similar | 92+ up front | 13 in total |
| Plays where Lavalink had to load an unresolved URL itself | — | 0 of 6 |
| `/play Under Pressure` while the playlist was queued | waited for the playlist | queued at once |
| Skip ×3, shuffle then skip, view queue, kill | — | all worked. The queue view shows placeholder titles and durations |

## Tests

`Tests/PlexBot.Tests` (xUnit) covers:
- the retry schedule and config parsing
- failure classification
- the gate's concurrency limit and shared cooldown
- cache keys and log IDs never containing the token

Run it with `dotnet test Tests/PlexBot.Tests`. The project is excluded from the bot build and the Docker context.

## Follow-ups (not in this PR)

- **Plex transcoder fallback.** python-plexapi builds `/audio/:/transcode/universal/start.m3u8` (HLS), not a progressive file. Nobody has checked whether lavaplayer can play it. Only worth trying if failures remain.
- **"Next Up" after a shuffle.** The player image isn't refreshed after a shuffle. This predates the refactor.
- **Infinite radio.** `plex.radio.infinite` and `refillThreshold` do nothing, because `GetRefillTracksAsync` is never called.
- **Remaining items from PR #45.** H12 (cooldown after success), tracking the PlexRequests extension, rotating the Plex token, and cleaning the old logs.
