using PlexBot.Utils;

namespace PlexBot.Utils;

/// <summary>Centralizes configuration management by providing access to environment variables and .env files with strong typing support</summary>
public static class EnvConfig
{
    private static readonly Dictionary<string, string> _envVariables = [];
    private static bool _initialized = false;

    /// <summary>Loads configuration values from both .env files and system environment variables, with system variables taking precedence</summary>
    /// <param name="envFilePath">Optional custom path to the .env file, defaults to the application base directory</param>
    public static void Initialize(string? envFilePath = null)
    {
        if (_initialized)
        {
            return;
        }

        // Load from .env file if it exists
        string filePath = envFilePath ?? System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env");
        if (File.Exists(filePath))
        {
            Logs.Init($"Loading configuration from {filePath}");
            LoadEnvFile(filePath);
        }
        else
        {
            Logs.Warning($".env file not found at {filePath}");
        }

        // Load from environment variables (overriding .env file)
        foreach (var entry in Environment.GetEnvironmentVariables())
        {
            if (entry is System.Collections.DictionaryEntry dictionaryEntry &&
                dictionaryEntry.Key is string key &&
                dictionaryEntry.Value is string value)
            {
                _envVariables[key] = value;
            }
        }

        _initialized = true;
        Logs.Init($"Configuration initialized with {_envVariables.Count} values");
    }

    /// <summary>Retrieves a string configuration value, automatically initializing the system if needed</summary>
    /// <param name="key">The configuration key name to look up, case-sensitive</param>
    /// <param name="defaultValue">Value to return if the key doesn't exist, protecting against null configurations</param>
    /// <returns>The configuration value if found; otherwise, the provided default value</returns>
    public static string Get(string key, string defaultValue = "")
    {
        if (!_initialized)
        {
            Initialize();
        }

        if (_envVariables.TryGetValue(key, out string? value))
        {
            return value;
        }

        return defaultValue;
    }

    /// <summary>Converts a configuration string to a boolean value, supporting various common boolean representations</summary>
    /// <param name="key">The configuration key to look up, case-sensitive</param>
    /// <param name="defaultValue">The fallback value when the key is missing or has an invalid format</param>
    /// <returns>True for values like "true", "yes", "1"; False for "false", "no", "0"; default value for anything else</returns>
    public static bool GetBool(string key, bool defaultValue = false)
    {
        string value = Get(key);
        if (string.IsNullOrEmpty(value))
        {
            return defaultValue;
        }

        value = value.ToLowerInvariant();
        return value switch
        {
            "true" or "yes" or "1" or "on" or "enable" or "enabled" => true,
            "false" or "no" or "0" or "off" or "disable" or "disabled" => false,
            _ => defaultValue
        };
    }

    /// <summary>Parses a configuration string to an integer, protecting against conversion errors with a default value</summary>
    /// <param name="key">The configuration key to look up, case-sensitive</param>
    /// <param name="defaultValue">The fallback value when the key is missing or cannot be parsed</param>
    /// <returns>The integer value if conversion succeeds; otherwise, the default value</returns>
    public static int GetInt(string key, int defaultValue = 0)
    {
        string value = Get(key);
        if (string.IsNullOrEmpty(value) || !int.TryParse(value, out int result))
        {
            return defaultValue;
        }

        return result;
    }

    /// <summary>Reads and parses a .env file, extracting key-value pairs while handling comments and formatting</summary>
    /// <param name="filePath">Path to the .env file to read</param>
    private static void LoadEnvFile(string filePath)
    {
        // Each line in the file should be in the format KEY=VALUE
        foreach (string line in File.ReadAllLines(filePath))
        {
            string trimmedLine = line.Trim();

            // Skip empty lines and comments
            if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#"))
            {
                continue;
            }

            // Split on the first = character
            int equalSignIndex = trimmedLine.IndexOf('=');
            if (equalSignIndex <= 0)
            {
                Logs.Warning($"Invalid configuration line in .env file: {trimmedLine}");
                continue;
            }

            string key = trimmedLine[..equalSignIndex].Trim();
            string value = trimmedLine[(equalSignIndex + 1)..].Trim();

            // Remove quotes if present
            if ((value.StartsWith("\"") && value.EndsWith("\"")) ||
                (value.StartsWith("'") && value.EndsWith("'")))
            {
                value = value[1..^1];
            }

            _envVariables[key] = value;
        }
    }

    /// <summary>Gets a configuration value as a long integer.</summary>
    /// <param name="key">The configuration key to look up</param>
    /// <param name="defaultValue">The default value to return if the key is not found or cannot be parsed</param>
    /// <returns>The configuration value as a long integer</returns>
    public static long GetLong(string key, long defaultValue = 0)
    {
        string value = Get(key);
        return long.TryParse(value, out long result) ? result : defaultValue;
    }

    /// <summary>Gets a configuration value as a double.</summary>
    /// <param name="key">The configuration key to look up</param>
    /// <param name="defaultValue">The default value to return if the key is not found or cannot be parsed</param>
    /// <returns>The configuration value as a double</returns>
    public static double GetDouble(string key, double defaultValue = 0)
    {
        string value = Get(key);
        return double.TryParse(value, out double result) ? result : defaultValue;
    }

    /// <summary>Gets a configuration value as a TimeSpan, parsing it from seconds.</summary>
    /// <param name="key">The configuration key to look up</param>
    /// <param name="defaultSeconds">The default seconds to return if the key is not found or cannot be parsed</param>
    /// <returns>The configuration value as a TimeSpan</returns>
    public static TimeSpan GetTimeSpan(string key, double defaultSeconds = 0)
    {
        double seconds = GetDouble(key, defaultSeconds);
        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>Gets a configuration value as an enum of type T.</summary>
    /// <typeparam name="T">The enum type to parse</typeparam>
    /// <param name="key">The configuration key to look up</param>
    /// <param name="defaultValue">The default value to return if the key is not found or cannot be parsed</param>
    /// <returns>The configuration value as an enum of type T</returns>
    public static T GetEnum<T>(string key, T defaultValue) where T : struct, Enum
    {
        string value = Get(key);
        return Enum.TryParse(value, true, out T result) ? result : defaultValue;
    }

    /// <summary>Gets all configuration values with keys that start with the specified prefix.</summary>
    /// <param name="prefix">The prefix to filter keys by</param>
    /// <returns>A dictionary of matching configuration keys and values</returns>
    public static Dictionary<string, string> GetSection(string prefix)
    {
        if (!_initialized)
        {
            Initialize();
        }

        var result = new Dictionary<string, string>();
        foreach (var entry in _envVariables)
        {
            if (entry.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                result[entry.Key] = entry.Value;
            }
        }
        return result;
    }

    /// <summary>Sets a configuration value at runtime.</summary>
    /// <param name="key">The configuration key to set</param>
    /// <param name="value">The value to set</param>
    public static void Set(string key, string value)
    {
        if (!_initialized)
        {
            Initialize();
        }

        _envVariables[key] = value;
    }
}