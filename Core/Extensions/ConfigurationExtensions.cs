namespace PlexBot.Core.Extensions;

public static class ConfigurationExtensions
{
    public static string GetRequiredValue<T>(this IConfiguration configuration, string configurationValueName)
    {
        var value = configuration[configurationValueName];
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException($"Configuration value '{configurationValueName}' is required.");
        }
        return value;
    }
}
