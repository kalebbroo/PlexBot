using PlexBot.Utils;

namespace PlexBot.Core.Extensions;

/// <summary>Foundation class for all bot extensions that defines the core interface and lifecycle management that extensions must implement</summary>
public abstract class Extensions
{
    /// <summary>Unique identifier used for extension registration, dependency management, and configuration access</summary>
    public abstract string Id { get; }

    /// <summary>User-friendly display name shown in UIs, help commands, and logs to identify the extension to users</summary>
    public abstract string Name { get; }

    /// <summary>Semantic version number (MAJOR.MINOR.PATCH) used for compatibility checks and update notifications</summary>
    public abstract string Version { get; }

    /// <summary>Name or names of the developers who created and maintain the extension, shown in credits and support info</summary>
    public abstract string Author { get; }

    /// <summary>Brief explanation of the extension's functionality that helps users understand its purpose and capabilities</summary>
    public abstract string Description { get; }

    /// <summary>Minimum compatible bot version required, preventing loading on older bot versions that lack necessary features</summary>
    public virtual string MinimumBotVersion => "1.0.0";

    /// <summary>List of other extension IDs that must be loaded first, ensuring proper initialization order and required functionality</summary>
    public virtual IEnumerable<string> Dependencies => Array.Empty<string>();

    /// <summary>Runtime status flag indicating whether the extension has been successfully initialized and is currently active</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>Timestamp recording when the extension was successfully loaded, useful for uptime tracking and diagnostics</summary>
    public DateTimeOffset LoadedAt { get; private set; }

    /// <summary>Stores any initialization error messages to help diagnose and report extension loading failures</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>Protected constructor that initializes the base extension state, preventing direct instantiation of the abstract class</summary>
    protected Extensions()
    {
        IsLoaded = false;
    }

    /// <summary>Prepares the extension for use by establishing service connections, registering commands, and allocating resources</summary>
    /// <param name="services">The application's service provider containing registered dependencies and shared services</param>
    /// <returns>True if initialization succeeds, false if any critical setup fails, with error details in ErrorMessage</returns>
    public async Task<bool> InitializeAsync(IServiceProvider services)
    {
        try
        {
            Logs.Info($"Initializing extension: {Name} (v{Version})");

            // Call the implementation-specific initialization
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

    /// <summary>Registers the extension's services with the service collection</summary>
    /// <param name="services">The service collection to register services with</param>
    public virtual void RegisterServices(IServiceCollection services)
    {
        Logs.Debug($"Registering services for extension: {Name}");
        // Base implementation does nothing - extensions should override this
    }

    /// <summary>Extension-specific initialization logic that derived classes must implement to set up their unique functionality</summary>
    /// <param name="services">The application's service provider for accessing dependencies</param>
    /// <returns>True if initialization was successful; otherwise, false</returns>
    protected abstract Task<bool> OnInitializeAsync(IServiceProvider services);

    /// <summary>Called when the extension is being unloaded</summary>
    /// <returns>A task representing the asynchronous shutdown operation</returns>
    public virtual Task ShutdownAsync()
    {
        Logs.Info($"Shutting down extension: {Name}");
        IsLoaded = false;
        return Task.CompletedTask;
    }

    /// <summary>Returns a string that represents the extension</summary>
    /// <returns>A string containing the extension name, version and status</returns>
    public override string ToString()
    {
        return $"{Name} v{Version} by {Author} [{(IsLoaded ? "Loaded" : "Unloaded")}]";
    }
}