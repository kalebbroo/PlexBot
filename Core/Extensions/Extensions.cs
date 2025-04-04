using PlexBot.Utils;

namespace PlexBot.Core.Extensions;

/// <summary>
/// Base class that all bot extensions must inherit from.
/// This abstract class defines the core interface that extensions must implement
/// and provides common functionality for extension lifecycle management.
/// Extensions inherit from this class and override methods to define their
/// specific behavior.
/// </summary>
public abstract class Extensions
{
    /// <summary>
    /// Gets the unique identifier for this extension.
    /// This should be a lowercase, hyphenated string with no spaces (e.g., "youtube-downloader")
    /// that uniquely identifies the extension for dependency management and configuration.
    /// </summary>
    public abstract string Id { get; }

    /// <summary>
    /// Gets the display name of the extension.
    /// This is the user-friendly name shown in UIs and logs, and can include spaces,
    /// capitalization, and special characters.
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Gets the version of the extension using semantic versioning.
    /// Should follow the MAJOR.MINOR.PATCH format (e.g., "1.0.0") to allow for
    /// proper version comparison and upgrade paths.
    /// </summary>
    public abstract string Version { get; }

    /// <summary>
    /// Gets the name(s) of the extension's author(s).
    /// Identifies the creator(s) of the extension for credit and support purposes.
    /// </summary>
    public abstract string Author { get; }

    /// <summary>
    /// Gets a description of what the extension does.
    /// Provides a brief overview of the extension's functionality and purpose
    /// to help users understand what it offers.
    /// </summary>
    public abstract string Description { get; }

    /// <summary>
    /// Gets the minimum bot version required for this extension.
    /// Extensions will only load if the bot version is greater than or equal to this value,
    /// preventing compatibility issues with older bot versions.
    /// </summary>
    public virtual string MinimumBotVersion => "1.0.0";

    /// <summary>
    /// Gets a list of extension IDs that this extension depends on.
    /// These extensions must be loaded before this one can be initialized,
    /// ensuring proper dependency management.
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
    /// Initializes a new instance of the Extensions class.
    /// Protected constructor ensures only derived classes can be instantiated.
    /// </summary>
    protected Extensions()
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
    /// <returns>True if initialization was successful; otherwise, false</returns>
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
    /// Registers the extension's services with the service collection.
    /// This method is called before initialization to allow the extension to register
    /// its dependencies in the service collection. Services registered here will be
    /// available to the extension during initialization.
    /// </summary>
    /// <param name="services">The service collection to register services with</param>
    public virtual void RegisterServices(IServiceCollection services)
    {
        Logs.Debug($"Registering services for extension: {Name}");
        // Base implementation does nothing - extensions should override this
    }

    /// <summary>
    /// Called when the extension is being unloaded.
    /// Extensions should override this to clean up any resources, close connections,
    /// unregister event handlers, etc.
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
    /// commands, and prepare for operation.
    /// </summary>
    /// <param name="services">The service provider containing registered services</param>
    /// <returns>True if initialization was successful; otherwise, false</returns>
    protected abstract Task<bool> OnInitializeAsync(IServiceProvider services);

    /// <summary>
    /// Returns a string that represents the extension.
    /// Provides a human-readable representation of the extension for logging and UI display.
    /// </summary>
    /// <returns>A string containing the extension name, version and status</returns>
    public override string ToString()
    {
        return $"{Name} v{Version} by {Author} [{(IsLoaded ? "Loaded" : "Unloaded")}]";
    }
}