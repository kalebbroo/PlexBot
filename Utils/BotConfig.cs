using FreneticUtilities.FreneticDataSyntax;

namespace PlexBot.Utils;

/// <summary>Manages structured application configuration loaded from a config.fds file using Frenetic Data Syntax</summary>
public static class BotConfig
{
    private static FDSSection _config = new();
    private static bool _initialized;

    /// <summary>Loads the config.fds file from disk. Call once at startup after EnvConfig.Initialize()</summary>
    public static void Initialize(string? configPath = null)
    {
        if (_initialized) return;

        string? filePath = configPath ?? FindConfigFile();
        if (filePath != null)
        {
            Logs.Init($"Loading bot config from {filePath}");
            _config = FDSUtility.ReadFile(filePath);
        }
        else
        {
            Logs.Warning("config.fds not found in any expected location, using defaults");
        }

        _initialized = true;
    }

    /// <summary>Gets a string value by dot-separated path (e.g. "visualPlayer.staticChannel.channelId")</summary>
    public static string GetString(string key, string defaultValue = "")
    {
        EnsureInitialized();
        return _config.GetString(key) ?? defaultValue;
    }

    /// <summary>Gets a boolean value by dot-separated path</summary>
    public static bool GetBool(string key, bool defaultValue = false)
    {
        EnsureInitialized();
        return _config.GetBool(key) ?? defaultValue;
    }

    /// <summary>Gets an integer value by dot-separated path</summary>
    public static int GetInt(string key, int defaultValue = 0)
    {
        EnsureInitialized();
        return _config.GetInt(key) ?? defaultValue;
    }

    /// <summary>Gets a double value by dot-separated path</summary>
    public static double GetDouble(string key, double defaultValue = 0)
    {
        EnsureInitialized();
        return _config.GetDouble(key) ?? defaultValue;
    }

    /// <summary>Gets an unsigned long value by dot-separated path</summary>
    public static ulong GetULong(string key, ulong defaultValue = 0)
    {
        EnsureInitialized();
        return _config.GetUlong(key) ?? defaultValue;
    }

    /// <summary>Gets a list of strings by dot-separated path, or null if the key is absent</summary>
    public static List<string>? GetStringList(string key)
    {
        EnsureInitialized();
        return _config.GetStringList(key);
    }

    private static void EnsureInitialized()
    {
        if (!_initialized) Initialize();
    }

    /// <summary>Searches for config.fds in standard locations: cwd, base directory, and up the directory tree</summary>
    private static string? FindConfigFile()
    {
        string cwdPath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "config.fds");
        if (File.Exists(cwdPath)) return cwdPath;

        string basePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.fds");
        if (File.Exists(basePath)) return basePath;

        DirectoryInfo? dir = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null)
        {
            string candidate = System.IO.Path.Combine(dir.FullName, "config.fds");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        return null;
    }
}
