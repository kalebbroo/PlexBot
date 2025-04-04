using PlexBot.Utils;

namespace PlexBot.Utils;

/// <summary>
/// Handles environment-based configuration for the application.
/// This utility class provides access to configuration values from environment variables
/// and .env files, with strong typing support and default values for missing configurations.
/// </summary>
public static class EnvConfig
{
    private static readonly Dictionary<string, string> _envVariables = new();
    private static bool _initialized = false;

    /// <summary>
    /// Initializes the configuration by loading values from .env files and environment variables.
    /// This method should be called at application startup to ensure all configuration is loaded
    /// before it's needed. It handles both reading from a .env file (if present) and from system
    /// environment variables, with environment variables taking precedence.
    /// </summary>
    /// <param name="envFilePath">Optional explicit path to the .env file</param>
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

    /// <summary>
    /// Gets a configuration value as a string, with an optional default value.
    /// This is the core method for retrieving configuration values, used by the
    /// strongly-typed getter methods.
    /// </summary>
    /// <param name="key">The configuration key to look up</param>
    /// <param name="defaultValue">The default value to return if the key is not found</param>
    /// <returns>The configuration value if found; otherwise, the default value</returns>
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

    /// <summary>
    /// Gets a configuration value as a boolean.
    /// Handles various string representations of boolean values ("true", "yes", "1", etc.)
    /// </summary>
    /// <param name="key">The configuration key to look up</param>
    /// <param name="defaultValue">The default value to return if the key is not found or cannot be parsed</param>
    /// <returns>The configuration value as a boolean</returns>
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

    /// <summary>
    /// Gets a configuration value as an integer.
    /// </summary>
    /// <param name="key">The configuration key to look up</param>
    /// <param name="defaultValue">The default value to return if the key is not found or cannot be parsed</param>
    /// <returns>The configuration value as an integer</returns>
    public static int GetInt(string key, int defaultValue = 0)
    {
        string value = Get(key);
        return int.TryParse(value, out int result) ? result : defaultValue;
    }

    /// <summary>
    /// Gets a configuration value as a long integer.
    /// </summary>
    /// <param name="key">The configuration key to look up</param>
    /// <param name="defaultValue">The default value to return if the key is not found or cannot be parsed</param>
    /// <returns>The configuration value as a long integer</returns>
    public static long GetLong(string key, long defaultValue = 0)
    {
        string value = Get(key);
        return long.TryParse(value, out long result) ? result : defaultValue;
    }

    /// <summary>
    /// Gets a configuration value as a double.
    /// </summary>
    /// <param name="key">The configuration key to look up</param>
    /// <param name="defaultValue">The default value to return if the key is not found or cannot be parsed</param>
    /// <returns>The configuration value as a double</returns>
    public static double GetDouble(string key, double defaultValue = 0)
    {
        string value = Get(key);
        return double.TryParse(value, out double result) ? result : defaultValue;
    }

    /// <summary>
    /// Gets a configuration value as a TimeSpan, parsing it from seconds.
    /// </summary>
    /// <param name="key">The configuration key to look up</param>
    /// <param name="defaultSeconds">The default seconds to return if the key is not found or cannot be parsed</param>
    /// <returns>The configuration value as a TimeSpan</returns>
    public static TimeSpan GetTimeSpan(string key, double defaultSeconds = 0)
    {
        double seconds = GetDouble(key, defaultSeconds);
        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// Gets a configuration value as an enum of type T.
    /// Case-insensitive matching is used for enum value names.
    /// </summary>
    /// <typeparam name="T">The enum type to parse</typeparam>
    /// <param name="key">The configuration key to look up</param>
    /// <param name="defaultValue">The default value to return if the key is not found or cannot be parsed</param>
    /// <returns>The configuration value as an enum of type T</returns>
    public static T GetEnum<T>(string key, T defaultValue) where T : struct, Enum
    {
        string value = Get(key);
        return Enum.TryParse(value, true, out T result) ? result : defaultValue;
    }

    /// <summary>
    /// Gets all configuration values with keys that start with the specified prefix.
    /// Useful for retrieving grouped configuration values, such as all settings for a
    /// particular component.
    /// </summary>
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

    /// <summary>
    /// Sets a configuration value at runtime.
    /// This is useful for dynamically updating configuration during execution
    /// or for setting derived configuration values.
    /// </summary>
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

    /// <summary>
    /// Loads environment variables from a .env file.
    /// Parses the file line by line, handling comments and variable assignments.
    /// </summary>
    /// <param name="filePath">The path to the .env file</param>
    private static void LoadEnvFile(string filePath)
    {
        try
        {
            string[] lines = File.ReadAllLines(filePath);
            foreach (string line in lines)
            {
                // Skip empty lines and comments
                string trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#"))
                {
                    continue;
                }

                // Parse key=value pairs
                int equalsPos = trimmedLine.IndexOf('=');
                if (equalsPos > 0)
                {
                    string key = trimmedLine.Substring(0, equalsPos).Trim();
                    string value = trimmedLine.Substring(equalsPos + 1).Trim();

                    // Remove surrounding quotes if present
                    if (value.StartsWith("\"") && value.EndsWith("\"") ||
                        value.StartsWith("'") && value.EndsWith("'"))
                    {
                        value = value.Substring(1, value.Length - 2);
                    }

                    _envVariables[key] = value;
                }
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"Error loading .env file: {ex.Message}");
        }
    }
}