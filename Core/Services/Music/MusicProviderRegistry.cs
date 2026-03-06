using System.Collections.Concurrent;
using PlexBot.Utils;

namespace PlexBot.Core.Services.Music;

/// <summary>Central registry for music providers. Extensions register their providers here
/// to make them available for search, autocomplete, and playback.
/// Follows the same ConcurrentDictionary + Register/Unregister pattern as DiscordButtonBuilder.</summary>
public class MusicProviderRegistry
{
    private readonly ConcurrentDictionary<string, IMusicProvider> _providers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Register a music provider. Replaces any existing provider with the same ID.</summary>
    public bool RegisterProvider(IMusicProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        bool isNew = _providers.TryAdd(provider.Id, provider);
        if (!isNew)
            _providers[provider.Id] = provider;
        Logs.Info($"Music provider {(isNew ? "registered" : "updated")}: {provider.DisplayName} ({provider.Id})");
        return isNew;
    }

    /// <summary>Unregister a provider by ID</summary>
    public bool UnregisterProvider(string providerId)
    {
        bool removed = _providers.TryRemove(providerId, out IMusicProvider? provider);
        if (removed)
            Logs.Info($"Music provider unregistered: {provider!.DisplayName} ({providerId})");
        return removed;
    }

    /// <summary>Get a provider by ID (case-insensitive)</summary>
    public IMusicProvider? GetProvider(string providerId)
    {
        _providers.TryGetValue(providerId, out IMusicProvider? provider);
        return provider;
    }

    /// <summary>Get all available providers ordered by priority (lower = first)</summary>
    public IReadOnlyList<IMusicProvider> GetAvailableProviders()
    {
        return _providers.Values
            .Where(p => p.IsAvailable)
            .OrderBy(p => p.Priority)
            .ToList();
    }
}
