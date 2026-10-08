using Path = System.IO.Path;

namespace PlexBot.Utils;

/// <summary>Locates the bot's folders on disk. The same lookup order is used everywhere: the Docker app root
/// (/app), then the app's base directory, then the working directory (running from the repo).</summary>
public static class AssetPaths
{
    /// <summary>The app root inside the Docker image</summary>
    public const string DockerRoot = "/app";

    /// <summary>The roots searched, in order</summary>
    public static IEnumerable<string> Roots => [DockerRoot, AppContext.BaseDirectory, Directory.GetCurrentDirectory()];

    /// <summary>The first existing directory at the given path under one of the <see cref="Roots"/>, or null.
    /// Example: <c>FindDirectory("Images", "Emoji")</c>.</summary>
    public static string? FindDirectory(params string[] segments) =>
        Roots.Select(root => Path.Combine([root, .. segments])).FirstOrDefault(Directory.Exists);

    /// <summary>The first existing file at the given path under one of the <see cref="Roots"/>, or null.
    /// Example: <c>FindFile("Images", "Icons", "play.png")</c>.</summary>
    public static string? FindFile(params string[] segments) =>
        Roots.Select(root => Path.Combine([root, .. segments])).FirstOrDefault(File.Exists);

    /// <summary>A file in the bot's persistent data folder (mounted at /app/data in Docker). The folder is created
    /// next to the app if it does not exist yet.</summary>
    public static string DataFile(string fileName)
    {
        string directory = FindDirectory("data") ?? Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, fileName);
    }
}
