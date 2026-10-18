using Xunit;

namespace PlexBot.Tests;

public class EmojiArtTests
{
    [Theory]
    [InlineData("pb_music")]
    [InlineData("pb_youtube")]
    [InlineData("pb_play")]
    [InlineData("pb_stop")]
    [InlineData("pb_success")]
    public void IconsTheBotUses_HaveArtInTheImageFolder(string name)
    {
        string path = Path.Combine(FindRepositoryRoot(), "Images", "Emoji", name + ".png");
        Assert.True(File.Exists(path), $"{name}.png is missing");
    }

    /// <summary>Walks up from the test binaries to the folder that holds the Images directory</summary>
    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory, "Images", "Emoji")))
                return directory;
            directory = Path.GetDirectoryName(directory);
        }
        throw new DirectoryNotFoundException("Images/Emoji was not found above the test output folder");
    }
}
