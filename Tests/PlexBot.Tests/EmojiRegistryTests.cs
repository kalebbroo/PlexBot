using Discord;
using PlexBot.Core.Discord.Design;
using PlexBot.Utils;
using Xunit;

namespace PlexBot.Tests;

public class EmojiRegistryTests
{
    [Fact]
    public void Resolve_FallsBackToUnicodeWhenTheEmojiIsNotSynced()
    {
        IEmote emote = new EmojiRegistry().Resolve("pb_play", "▶️");
        Assert.Equal("▶️", Assert.IsType<Emoji>(emote).Name);
    }

    [Fact]
    public void Get_IsNullBeforeTheSync()
    {
        Assert.Null(new EmojiRegistry().Get("pb_play"));
    }

    [Theory]
    [InlineData(false, null, "AA", EmojiSyncAction.Upload)]   // not on the application
    [InlineData(false, "AA", "AA", EmojiSyncAction.Upload)]   // deleted in the portal: upload again
    [InlineData(true, null, "AA", EmojiSyncAction.Record)]    // uploaded before hashes were kept
    [InlineData(true, "AA", "AA", EmojiSyncAction.Skip)]      // unchanged art
    [InlineData(true, "aa", "AA", EmojiSyncAction.Skip)]      // hex case does not matter
    [InlineData(true, "AA", "BB", EmojiSyncAction.Replace)]   // art changed since upload
    public void Decide_PicksTheSyncAction(bool onApplication, string? recordedHash, string currentHash, EmojiSyncAction expected)
    {
        Assert.Equal(expected, EmojiRegistry.Decide(onApplication, recordedHash, currentHash));
    }

    [Fact]
    public void HashImage_ChangesWhenTheImageChanges()
    {
        string original = EmojiRegistry.HashImage([1, 2, 3]);
        Assert.Equal(original, EmojiRegistry.HashImage([1, 2, 3]));
        Assert.NotEqual(original, EmojiRegistry.HashImage([1, 2, 4]));
    }

    [Fact]
    public void Hashes_RoundTripThroughTheFile()
    {
        string directory = Directory.CreateTempSubdirectory("plexbot-emoji-").FullName;
        try
        {
            string file = System.IO.Path.Combine(directory, EmojiRegistry.HashFileName);
            Assert.Empty(EmojiRegistry.LoadHashes(file));

            EmojiRegistry.SaveHashes(file, new Dictionary<string, string> { ["pb_skip"] = "BB", ["pb_play"] = "AA" });
            Dictionary<string, string> loaded = EmojiRegistry.LoadHashes(file);

            Assert.Equal("AA", loaded["pb_play"]);
            Assert.Equal("BB", loaded["pb_skip"]);
            Assert.False(File.Exists(file + ".tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LoadHashes_TreatsAnUnreadableFileAsEmpty()
    {
        string file = System.IO.Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, "{ not json");
            Assert.Empty(EmojiRegistry.LoadHashes(file));
        }
        finally
        {
            File.Delete(file);
        }
    }
}

public class AssetPathsTests
{
    [Fact]
    public void FindDirectory_ReturnsNullWhenNoRootHasIt()
    {
        Assert.Null(AssetPaths.FindDirectory("no-such-folder-" + Guid.NewGuid().ToString("N")));
    }

    [Fact]
    public void FindDirectory_FindsAFolderUnderTheWorkingDirectory()
    {
        string name = "plexbot-assets-" + Guid.NewGuid().ToString("N");
        string created = Directory.CreateDirectory(System.IO.Path.Combine(Directory.GetCurrentDirectory(), name, "Emoji")).FullName;
        try
        {
            Assert.Equal(created, AssetPaths.FindDirectory(name, "Emoji"));
        }
        finally
        {
            Directory.Delete(System.IO.Path.Combine(Directory.GetCurrentDirectory(), name), recursive: true);
        }
    }
}
