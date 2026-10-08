# Plex playlist pipeline refactor: findings and plan

Status: planning. No code changes on this branch yet.

## What the failures actually are

The earlier theory was that some media types, or some tracks, cannot be played by Lavalink. The discriminator test does not support that. The cause is Plex throttling parallel file requests.

Test run against 20 failed Kpop parts from the 2026-10-08 log:

| Test | Result |
|---|---|
| Each of 12 parts fetched one at a time, from the host | All 200, full size (6–8 MB) |
| Same 12 fetched one at a time, from inside the Lavalink container | All 200, full size |
| Same 20 fetched with 10 in parallel, from the host | 4 × `503` (105-byte HTML body), 3 × `200` with 0 bytes, 13 × 200 with data |

A 503 body is an HTML page: `<html><head><title>Service Unavailable</title></head>...`. Lavalink hands it to its format probe as if it were audio, which produces `Could not read the file for detecting file type` and `Unknown file format`. A `200` with 0 bytes produces `Premature end of Content-Length delimited message body (received: 0)`. Both families come from the same cause.

Implications:

- **Retries help only if they wait.** The 0 of 77 recoveries happened because retries ran about 2 seconds after the first pass, against the same concurrency. Backoff over several seconds would likely recover most of them. This has not been measured yet.
- **Codecs are not the problem.** Tracks in mp3, flac, m4a and opus all loaded when fetched alone.
- **The fix is to limit concurrency and validate responses.** Lavalink should only ever receive a response the bot has already checked.

## Current pipeline (for reference)

- `TrackResolverService.ResolveTracksParallelAsync` resolves every track in a batch up front, `plex.maxConcurrentResolves` (default 3) at a time, and then retries failures once after 2 seconds.
- `ResolveTrackAsync` calls `audioService.Tracks.LoadTrackAsync(PlaybackUrl)`, which makes Lavalink fetch the file and probe it.
- `_resolveCache` caches `LavalinkTrack` by URL. It does not cache the Plex API responses, and it doesn't reduce file fetches.
- A track that fails when it reaches the front of the queue is skipped.
- Lavalink also fetches the file again when it plays, so each track is fetched at least twice.

## Plan

### 1. Validate the Plex response before Lavalink sees it (bot side)
Before calling `LoadTrackAsync`, the bot issues a ranged request (`Range: bytes=0-0`) to the file URL, using the same HTTP client as Plex. The bot then checks the status code and that the response is audio, meaning a non-HTML `Content-Type` and a non-zero length. Only validated URLs reach Lavalink.

- 503 or 429 → retriable, back off.
- 200 with 0 bytes or HTML → retriable, back off.
- 404 → permanent for this track. Mark it failed and move on.

### 2. Backoff and retry, scoped to the failure
- Retry network-family and throttle-family failures with exponential backoff, for example 2 s, 5 s, 15 s, then give up. Make these values configurable in `config.fds`.
- Log the failure family, the HTTP status, and the part ID for each track, so the next investigation doesn't need a correlation exercise.

### 3. Lower and configure concurrency
- Change the default of `plex.maxConcurrentResolves` from 3 to 1 or 2, and measure. The discriminator run shows 10 parallel fetches trigger throttling.
- Add an explicit limit on in-flight file requests per Plex server, separate from the resolve worker count, so Lavalink's own fetches are counted too.

### 4. Just-in-time resolution
- Resolve only the current track plus a small window ahead, for example the next 2 to 3. The rest of the queue holds unresolved placeholders, and each placeholder is resolved as it comes into the window.
- This removes the burst of 92 resolves at the start of a playlist. It also fixes the request-ordering wait from PR 45: a later `/play` no longer waits for a whole playlist to resolve.
- Interacts with the existing ordering turnstile: the turnstile should order placeholder insertion, and resolution happens later.

### 5. Retry at play time
- If a track fails when it reaches the front of the queue, retry it through the same validated path before skipping. Notify the user only if all attempts fail.

### 6. Caching
- Keep the existing API-level caches (music section ID, similar tracks). They are not the bottleneck.
- Keep `_resolveCache` but key it by part ID and validity, and expire entries. Plex part URLs include a `X-Plex-Token`. Cache keys must not contain the token, and cached URLs must not be logged.
- Don't cache file contents in the bot. Lavalink already streams.

### 7. Fallback: Plex transcode URL (only if still needed)
- If a track keeps failing after validation and backoff, try Plex's universal transcoder for an audio stream instead of the direct file. This should be verified before use. Check how python-plexapi builds `getStreamURL` rather than guessing parameters.

## Open questions to settle before implementing

1. Measure recovery with backoff: rerun the failed Kpop parts with 2, 5 and 15 second waits, and with concurrency 1 and 2. Decide the defaults from that data.
2. Does Plex's remote access setting "Limit remote stream bitrate" change the 503 behaviour? Check Settings → Remote Access on the server. This is a configuration question for the user.
3. Lavaplayer probe depth: does a larger probe window help the "unknown format" case? This matters only if a validated response still fails the probe. Research the lavaplayer probe and embedded-art handling before assuming it.
4. Confirm the Plex universal transcoder parameters (`/music/:/transcode/universal/start.*`) against the server, using python-plexapi's source.

## Out of scope for this PR

- The `Extensions/PlexRequestsBridge` duplicate-option fix. It sits in a gitignored folder and needs a decision on tracking.
- H12, cooldown after success.
- Rotating the Plex token and deleting logs written before token redaction.

## Test plan

- Unit-level: the validation and backoff logic, with a fake HTTP handler returning 503, 200-empty, HTML, and valid audio.
- Live: the Kpop playlist (92 tracks) and the Play-All-Similar set (25 tracks). Target: almost all tracks load. Record the failure counts per family before and after.
- Live: pause, skip and queue operations during just-in-time loading, to confirm the ordering and cancellation behaviour from PR 45 still holds.
