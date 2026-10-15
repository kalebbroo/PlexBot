using Discord;
using PlexBot.Core.Discord.Design;
using PlexBot.Core.Discord.Embeds;
using PlexBot.Core.Models;
using PlexBot.Core.Models.Media;
using PlexBot.Core.Models.Players;
using PlexBot.Core.Services.Music;
using Xunit;

namespace PlexBot.Tests;

public class IdlePlayerButtonTests
{
    private static readonly string[] ControlIds =
    [
        "previous:back", "pause_resume:pause", "skip:skip", "repeat:cycle", "queue_options:shuffle:1",
        "queue_options:options:1", "volume:down", "volume:up", "radio:start", "sonic:similar", "sonic:adventure", "kill:kill",
    ];

    private static DiscordButtonBuilder Builder(params IMusicProvider[] providers)
    {
        MusicProviderRegistry registry = new();
        foreach (IMusicProvider provider in providers)
            registry.RegisterProvider(provider);
        return new DiscordButtonBuilder(new EmojiRegistry(), registry);
    }

    private static List<List<ButtonBuilder>> Rows(ComponentBuilder components) =>
        (components.ActionRows ?? [])
            .Select(row => row.Components.OfType<ButtonBuilder>().ToList())
            .ToList();

    private static List<string> Ids(ComponentBuilder components) =>
        Rows(components).SelectMany(row => row).Select(button => button.CustomId).ToList();

    [Fact]
    public void IdleCard_OffersEntryRowThenRadioAndAdventure()
    {
        Assert.Equal(
            new[] { "entry:play", "entry:search", "idle:radio", "idle:adventure" },
            Ids(Builder().BuildButtons(ButtonFlag.Idle)));
    }

    [Fact]
    public void IdleCard_AddsProviderSearchAfterSearch_WhenASearchProviderIsRegistered()
    {
        DiscordButtonBuilder builder = Builder(new StubProvider("youtube", "YouTube", MusicProviderCapabilities.Search | MusicProviderCapabilities.UrlPlayback));

        Assert.Equal(
            new[] { "entry:play", "entry:search", "provider:search:youtube", "idle:radio", "idle:adventure" },
            Ids(builder.BuildButtons(ButtonFlag.Idle)));
    }

    [Fact]
    public void ProviderSearch_IgnoresPlexAndProvidersWithoutSearch()
    {
        DiscordButtonBuilder builder = Builder(
            new StubProvider("plex", "Plex", MusicProviderCapabilities.Search),
            new StubProvider("playlists", "Playlists", MusicProviderCapabilities.Playlists));

        Assert.Equal(
            new[] { "entry:play", "entry:search", "idle:radio", "idle:adventure" },
            Ids(builder.BuildButtons(ButtonFlag.Idle)));
    }

    [Fact]
    public void PlayButton_IsLabelledPlayWithTheNoteEmoji()
    {
        ButtonBuilder play = Builder().BuildButtons(ButtonFlag.Idle).ActionRows![0].Components.OfType<ButtonBuilder>().First();

        Assert.Equal("entry:play", play.CustomId);
        Assert.Equal("Play", play.Label);
        Assert.Equal("\U0001F3B5", Assert.IsType<Emoji>(play.Emote).Name);
    }

    [Fact]
    public void StaticPlayingCard_IsTheControlsThenTheEntryRow_InThreeFullRows()
    {
        DiscordButtonBuilder builder = Builder(new StubProvider("youtube", "YouTube", MusicProviderCapabilities.Search));

        List<List<ButtonBuilder>> rows = Rows(builder.BuildPlayingButtons(new ButtonContext(), staticChannel: true));

        Assert.Equal(new[] { 5, 5, 5 }, rows.Select(row => row.Count).ToArray());
        Assert.Equal(
            ControlIds.Concat(new[] { "entry:play", "entry:search", "provider:search:youtube" }),
            rows.SelectMany(row => row).Select(button => button.CustomId));
    }

    [Fact]
    public void StaticPlayingCard_HasOneRadioAndOneAdventureAndNoIdleButtons()
    {
        List<string> ids = Ids(Builder().BuildPlayingButtons(new ButtonContext(), staticChannel: true));

        Assert.Equal(1, ids.Count(id => id == "radio:start"));
        Assert.Equal(1, ids.Count(id => id == "sonic:adventure"));
        Assert.DoesNotContain("idle:radio", ids);
        Assert.DoesNotContain("idle:adventure", ids);
    }

    [Fact]
    public void RegularPlayingCard_IsUnchanged()
    {
        DiscordButtonBuilder builder = Builder(new StubProvider("youtube", "YouTube", MusicProviderCapabilities.Search));

        List<List<ButtonBuilder>> rows = Rows(builder.BuildPlayingButtons(new ButtonContext(), staticChannel: false));

        Assert.Equal(new[] { 5, 5, 2 }, rows.Select(row => row.Count).ToArray());
        Assert.Equal(ControlIds, rows.SelectMany(row => row).Select(button => button.CustomId));
    }

    [Fact]
    public void RegularGuildsCard_KeepsItsControls_WhenTheStaticRuleSaysNo()
    {
        bool staticCard = VisualPlayerStateManager.IsStaticCard(true, 100, 200);

        Assert.Equal(ControlIds, Ids(Builder().BuildPlayingButtons(new ButtonContext(), staticCard)));
    }

    /// <summary>A provider with only the members the button builder reads. Nothing here is called by these tests.</summary>
    private sealed class StubProvider(string id, string displayName, MusicProviderCapabilities capabilities) : IMusicProvider
    {
        public string Id => id;
        public string DisplayName => displayName;
        public bool IsAvailable => true;
        public int Priority => 100;
        public MusicProviderCapabilities Capabilities => capabilities;
        public Task<SearchResults> SearchAsync(string query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Track?> GetTrackDetailsAsync(string trackKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Track>> GetTracksAsync(string containerKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Album>> GetAlbumsAsync(string artistKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Track>> GetAllArtistTracksAsync(string artistKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Playlist>> GetPlaylistsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Playlist?> GetPlaylistDetailsAsync(string playlistKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
