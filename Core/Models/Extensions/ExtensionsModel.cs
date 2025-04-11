using PlexBot.Utils;

namespace PlexBot.Core.Models.Extensions;

/// <summary>Base class for all bot extensions providing the fundamental structure and lifecycle methods for extension management</summary>
public abstract class Extension
{
    /// <summary>Unique identifier for this extension used for dependency management and must be unique across all extensions</summary>
    public abstract string Id { get; }

    /// <summary>User-friendly display name shown in UIs and logs for better readability and recognition</summary>
    public abstract string Name { get; }

    /// <summary>Semantic version (MAJOR.MINOR.PATCH) used to track compatibility and feature updates</summary>
    public abstract string Version { get; }

    /// <summary>Names or usernames of the extension creators for attribution and support contacts</summary>
    public abstract string Author { get; }

    /// <summary>Concise explanation of the extension's purpose and features to help users understand its functionality</summary>
    public abstract string Description { get; }

    /// <summary>Minimum bot version required for compatibility, preventing loading on incompatible bot versions</summary>
    public virtual string MinimumBotVersion => "1.0.0";

    /// <summary>List of extension IDs that must be loaded before this extension can be initialized</summary>
    public virtual IEnumerable<string> Dependencies => Array.Empty<string>();

    /// <summary>Indicates whether the extension is currently loaded and initialized, managed by the extension system</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>Timestamp when the extension was loaded for diagnostics and uptime tracking</summary>
    public DateTimeOffset LoadedAt { get; private set; }

    /// <summary>Error message from initialization failures, null if no errors occurred or not yet initialized</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>Protected constructor ensuring only derived classes can be instantiated</summary>
    protected Extension()
    {
        IsLoaded = false;
    }

    /// <summary>Initializes the extension with required services and prepares it for use in the bot system</summary>
    /// <param name="services">The service provider containing registered services</param>
    /// <exception cref="Exception">Thrown if initialization fails</exception>
    /// <returns>A task representing the asynchronous initialization operation</returns>
    public async Task<bool> InitializeAsync(IServiceProvider services)
    {
        try
        {
            Logs.Info($"Initializing extension: {Name} (v{Version})");

            bool result = await OnInitializeAsync(services);

            if (result)
            {
                IsLoaded = true;
                LoadedAt = DateTimeOffset.UtcNow;
                ErrorMessage = null;
                Logs.Info($"Extension initialized successfully: {Name}");
            }
            else
            {
                ErrorMessage = "Extension initialization returned false";
                Logs.Warning($"Extension failed to initialize: {Name} - {ErrorMessage}");
            }

            return result;
        }
        catch (Exception ex)
        {
            IsLoaded = false;
            ErrorMessage = ex.Message;
            Logs.Error($"Exception during extension initialization: {Name} - {ex.Message}");
            return false;
        }
    }

    /// <summary>Registers the extension's services in the dependency injection container before initialization</summary>
    /// <param name="services">The service collection to register services with</param>
    public virtual void RegisterServices(IServiceCollection services)
    {
        Logs.Debug($"Registering services for extension: {Name}");
        // Base implementation does nothing - extensions should override as needed
    }

    /// <summary>Cleans up resources when the extension is being unloaded to prevent memory leaks and resource conflicts</summary>
    /// <returns>A task representing the asynchronous shutdown operation</returns>
    public virtual Task ShutdownAsync()
    {
        Logs.Info($"Shutting down extension: {Name}");
        IsLoaded = false;
        return Task.CompletedTask;
    }

    /// <summary>Extension-specific initialization logic to be implemented by derived classes</summary>
    /// <param name="services">The service provider containing registered services</param>
    /// <returns>True if initialization was successful; otherwise, false</returns>
    protected abstract Task<bool> OnInitializeAsync(IServiceProvider services);

    /// <summary>Creates a human-readable representation of the extension for debugging and logging purposes</summary>
    /// <returns>A string containing the extension name, version and status</returns>
    public override string ToString()
    {
        return $"{Name} v{Version} by {Author} [{(IsLoaded ? "Loaded" : "Unloaded")}]";
    }
}