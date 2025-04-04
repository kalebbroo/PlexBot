using SwarmUI.Utils;

namespace PlexBot.Core.Models.Extensions;

/// <summary>
/// Base class that all bot extensions must inherit from.
/// Provides the fundamental structure and lifecycle methods that the extension system
/// uses to discover, initialize, and manage extensions. This class handles the core
/// extension functionality while allowing derived classes to focus on their specific features.
/// </summary>
public abstract class Extension
{
    /// <summary>
    /// Gets the unique identifier for this extension.
    /// This should be a lowercase, hyphenated string with no spaces, e.g., "youtube-downloader".
    /// Used internally for extension management and must be unique across all extensions.
    /// </summary>
    public abstract string Id { get; }

    /// <summary>
    /// Gets the display name of the extension.
    /// This is the user-friendly name shown in UIs and logs.
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Gets the version of the extension using semantic versioning.
    /// Format should be MAJOR.MINOR.PATCH (e.g., "1.0.0").
    /// </summary>
    public abstract string Version { get; }

    /// <summary>
    /// Gets the author(s) of the extension.
    /// Names or usernames of the creators.
    /// </summary>
    public abstract string Author { get; }

    /// <summary>
    /// Gets a description of what the extension does.
    /// Should be a concise explanation of the extension's purpose and features.
    /// </summary>
    public abstract string Description { get; }

    /// <summary>
    /// Gets the minimum bot version required for this extension.
    /// Extensions will only load if the bot version is greater than or equal to this value.
    /// </summary>
    public virtual string MinimumBotVersion => "1.0.0";

    /// <summary>
    /// Gets a list of extension IDs that this extension depends on.
    /// These extensions must be loaded before this one can be initialized.
    /// </summary>
    public virtual IEnumerable<string> Dependencies => Array.Empty<string>();

    /// <summary>
    /// Gets a value indicating whether the extension is currently loaded and initialized.
    /// Set internally by the extension system during the lifecycle management process.
    /// </summary>
    public bool IsLoaded { get; private set; }

    /// <summary>
    /// Gets the timestamp when the extension was loaded.
    /// Used for diagnostics and uptime tracking.
    /// </summary>
    public DateTimeOffset LoadedAt { get; private set; }

    /// <summary>
    /// Gets any error message that occurred during initialization.
    /// Null if no errors occurred or the extension hasn't been initialized.
    /// </summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Initializes a new instance of the Extension class.
    /// Protected constructor ensures only derived classes can be instantiated.
    /// </summary>
    protected Extension()
    {
        IsLoaded = false;
    }

    /// <summary>
    /// Initializes the extension with the provided service provider.
    /// This method is called by the extension system during the extension loading process.
    /// It prepares the extension for use, creating any necessary resources and
    /// establishing connections to external services.
    /// </summary>
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

    /// <summary>
    /// Called by the extension system to register the extension's services.
    /// This method is called before initialization to allow the extension to register
    /// its dependencies in the service collection. Services registered here will be
    /// available to the extension and to other extensions that depend on them.
    /// </summary>
    /// <param name="services">The service collection to register services with</param>
    public virtual void RegisterServices(IServiceCollection services)
    {
        Logs.Debug($"Registering services for extension: {Name}");
        // Base implementation does nothing - extensions should override as needed
    }

    /// <summary>
    /// Called when the extension is being unloaded.
    /// Derived classes should override this to clean up any resources, close connections,
    /// unregister event handlers, etc. This ensures proper cleanup when an extension
    /// is disabled or the bot is shutting down.
    /// </summary>
    /// <returns>A task representing the asynchronous shutdown operation</returns>
    public virtual Task ShutdownAsync()
    {
        Logs.Info($"Shutting down extension: {Name}");
        IsLoaded = false;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Override this method to implement extension-specific initialization logic.
    /// This is where the extension should set up its core functionality, register
    /// commands, and prepare for operation. This method is called by the base
    /// InitializeAsync method and should return true only if initialization was successful.
    /// </summary>
    /// <param name="services">The service provider containing registered services</param>
    /// <returns>True if initialization was successful; otherwise, false</returns>
    protected abstract Task<bool> OnInitializeAsync(IServiceProvider services);

    /// <summary>
    /// Creates a human-readable representation of the extension primarily for debugging and logging.
    /// Includes the essential identifying information.
    /// </summary>
    /// <returns>A string containing the extension name, version and status</returns>
    public override string ToString()
    {
        return $"{Name} v{Version} by {Author} [{(IsLoaded ? "Loaded" : "Unloaded")}]";
    }
}