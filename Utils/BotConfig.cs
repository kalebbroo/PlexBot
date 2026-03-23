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

        if (filePath is null)
        {
            // Try to create config.fds from the template
            filePath = TryCreateFromTemplate();
        }

        if (filePath is not null)
        {
            Logs.Init($"Loading bot config from {filePath}");
            _config = FDSUtility.ReadFile(filePath);
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n[WARN] config.fds not found — using built-in defaults.");
            Console.WriteLine("To customize settings, copy the template:");
            Console.WriteLine("  cp RenameMe.config.fds config.fds");
            Console.ResetColor();
            _config = new FDSSection();
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

    /// <summary>Attempts to copy RenameMe.config.fds to config.fds and returns the new path, or null on failure</summary>
    private static string? TryCreateFromTemplate()
    {
        string[] searchDirs = [Directory.GetCurrentDirectory(), AppDomain.CurrentDomain.BaseDirectory];
        foreach (string dir in searchDirs)
        {
            string template = System.IO.Path.Combine(dir, "RenameMe.config.fds");
            if (!File.Exists(template)) continue;

            string destination = System.IO.Path.Combine(dir, "config.fds");
            try
            {
                File.Copy(template, destination);
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"[Init] No config.fds found — created from template at {destination}");
                Console.ResetColor();
                return destination;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[WARN] Found template but failed to copy: {ex.Message}");
                Console.ResetColor();
            }
        }
        return null;
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
