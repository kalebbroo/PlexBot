using System.Collections.Concurrent;
using System.Security.Cryptography;
using Discord;
using Discord.WebSocket;
using PlexBot.Utils;

using Path = System.IO.Path;

namespace PlexBot.Core.Discord.Design;

/// <summary>What a sync does with one emoji image</summary>
public enum EmojiSyncAction
{
    /// <summary>On the application with the same art: nothing to do</summary>
    Skip,

    /// <summary>Not on the application: upload it</summary>
    Upload,

    /// <summary>On the application, but the image changed since it was uploaded: delete it and upload the new art</summary>
    Replace,

    /// <summary>On the application from before hashes were kept: keep it and record the current image's hash</summary>
    Record,
}

/// <summary>The bot's own application emoji, usable in every guild it is in. On startup the PNGs in Images/Emoji are
/// uploaded if they are not already on the application, and replaced if their art changed. Any name not available
/// falls back to unicode, so the bot never shows a broken emoji.</summary>
public sealed class EmojiRegistry
{
    /// <summary>Every file in the image folder starts with this, and the emoji is named after the file</summary>
    public const string Prefix = "pb_";

    /// <summary>File in the data folder that records the hash of each uploaded image</summary>
    public const string HashFileName = "emoji-hashes.json";

    /// <summary>The application's emoji, by name</summary>
    public ConcurrentDictionary<string, Emote> ByName { get; } = new(StringComparer.Ordinal);

    /// <summary>Number of application emoji known to the registry</summary>
    public int Count => ByName.Count;

    // Completes when a sync has finished, whether or not it worked. Cards built before then can't use the synced emoji.
    private readonly TaskCompletionSource _syncFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Names already reported as falling back to unicode, so each one is logged once rather than for every button
    private readonly ConcurrentDictionary<string, bool> _reportedFallbacks = new(StringComparer.Ordinal);

    /// <summary>Decides what to do with one image, from whether its name is on the application, the hash recorded
    /// when it was last uploaded (null if none), and the hash of the image on disk</summary>
    public static EmojiSyncAction Decide(bool onApplication, string? recordedHash, string currentHash)
    {
        if (!onApplication) return EmojiSyncAction.Upload;
        if (recordedHash is null) return EmojiSyncAction.Record;
        return string.Equals(recordedHash, currentHash, StringComparison.OrdinalIgnoreCase) ? EmojiSyncAction.Skip : EmojiSyncAction.Replace;
    }

    /// <summary>SHA-256 of an image, as upper-case hex</summary>
    public static string HashImage(byte[] content) => Convert.ToHexString(SHA256.HashData(content));

    /// <summary>Lists the application's emoji, then uploads missing images and replaces changed ones, and caches the
    /// result by name. Safe to run on every start: unchanged images are skipped.</summary>
    public async Task SyncAsync(DiscordSocketClient client, string imageDirectory, string hashFile, CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<Emote> existing = await client.GetApplicationEmotesAsync().ConfigureAwait(false);
        foreach (Emote emote in existing)
            ByName[emote.Name] = emote;

        Dictionary<string, string> hashes = LoadHashes(hashFile);
        int uploaded = 0;
        int replaced = 0;

        foreach (string file in Directory.GetFiles(imageDirectory, Prefix + "*.png").OrderBy(f => f, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = Path.GetFileNameWithoutExtension(file);
            byte[] content = await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false);
            string hash = HashImage(content);
            bool onApplication = ByName.TryGetValue(name, out Emote? current);

            switch (Decide(onApplication, hashes.GetValueOrDefault(name), hash))
            {
                case EmojiSyncAction.Skip:
                    break;

                case EmojiSyncAction.Record:
                    hashes[name] = hash;
                    break;

                case EmojiSyncAction.Replace:
                    await client.DeleteApplicationEmoteAsync(current!.Id).ConfigureAwait(false);
                    ByName.TryRemove(name, out _);
                    await UploadAsync(client, name, content).ConfigureAwait(false);
                    hashes[name] = hash;
                    replaced++;
                    break;

                case EmojiSyncAction.Upload:
                    await UploadAsync(client, name, content).ConfigureAwait(false);
                    hashes[name] = hash;
                    uploaded++;
                    break;
            }
        }

        SaveHashes(hashFile, hashes);
        Logs.Info($"Application emoji ready: {ByName.Count} available ({uploaded} uploaded, {replaced} replaced)");
    }

    /// <summary>Uploads one image as an application emoji and caches it</summary>
    public async Task UploadAsync(DiscordSocketClient client, string name, byte[] content)
    {
        using MemoryStream stream = new(content);
        Emote created = await client.CreateApplicationEmoteAsync(name, new global::Discord.Image(stream)).ConfigureAwait(false);
        ByName[created.Name] = created;
        Logs.Info($"Uploaded application emoji :{created.Name}:");
    }

    /// <summary>Reads the recorded hashes, or an empty set if the file is missing or unreadable</summary>
    public static Dictionary<string, string> LoadHashes(string hashFile)
    {
        try
        {
            if (!File.Exists(hashFile)) return new Dictionary<string, string>(StringComparer.Ordinal);
            Dictionary<string, string>? stored = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(hashFile));
            return stored is null ? new(StringComparer.Ordinal) : new(stored, StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            Logs.Warning($"Could not read {hashFile}, treating every emoji as unrecorded: {ex.Message}");
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    /// <summary>Writes the hashes through a temporary file, so a crash mid-write can't leave a half-written file</summary>
    public static void SaveHashes(string hashFile, Dictionary<string, string> hashes)
    {
        string temporary = hashFile + ".tmp";
        File.WriteAllText(temporary, JsonConvert.SerializeObject(new SortedDictionary<string, string>(hashes, StringComparer.Ordinal),
            Formatting.Indented));
        File.Move(temporary, hashFile, overwrite: true);
    }

    /// <summary>Called when a sync has finished, whether it worked or not, so anything waiting for the emoji can go ahead</summary>
    public void MarkSyncFinished() => _syncFinished.TrySetResult();

    /// <summary>Waits for a sync to finish. Returns false if it has not finished within <paramref name="timeout"/>.</summary>
    public async Task<bool> WaitForSyncAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        try
        {
            await _syncFinished.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>The synced emoji for a name, or null when it is not on the application</summary>
    public Emote? Get(string name) => ByName.TryGetValue(name, out Emote? emote) ? emote : null;

    /// <summary>The emoji as text for a message body: <c>&lt;:name:id&gt;</c> when synced, otherwise the unicode fallback</summary>
    public string Text(string name, string unicodeFallback) => Resolve(name, unicodeFallback).ToString();

    /// <summary>The synced emoji for a name, or the unicode fallback when it is not available. A missing name is logged once.</summary>
    public IEmote Resolve(string name, string unicodeFallback)
    {
        Emote? emote = Get(name);
        if (emote is not null) return emote;
        if (_reportedFallbacks.TryAdd(name, true))
            Logs.Warning($"Application emoji :{name}: is not available, so the unicode fallback is shown");
        return new Emoji(unicodeFallback);
    }
}
