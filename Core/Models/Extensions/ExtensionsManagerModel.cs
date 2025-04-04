using System.Collections.Concurrent;
using PlexBot.Utils;

namespace PlexBot.Core.Models.Extensions;

/// <summary>
/// Manages the discovery, loading, and lifecycle of bot extensions.
/// This class is the central coordinator for all extension-related operations,
/// providing a consistent interface for working with extensions and ensuring
/// they're properly initialized, configured, and eventually shut down.
/// </summary>
public class ExtensionManager
{
    /// <summary>
    /// Dictionary of all loaded extensions, keyed by their unique ID.
    /// Provides fast lookup of extensions by ID for dependency management.
    /// </summary>
    private readonly ConcurrentDictionary<string, Extension> _loadedExtensions = new();

    /// <summary>
    /// Service provider for dependency injection within extensions.
    /// Used to provide services to extensions during initialization.
    /// </summary>
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Base directory where extension folders are located.
    /// Each extension should have its own subdirectory within this path.
    /// </summary>
    private readonly string _extensionsDirectory;

    /// <summary>
    /// Initializes a new instance of the ExtensionManager class.
    /// Sets up the manager with the necessary dependencies and prepares
    /// for loading extensions from the specified directory.
    /// </summary>
    /// <param name="serviceProvider">The service provider for dependency injection</param>
    /// <param name="extensionsDirectory">Directory path where extensions are located</param>
    public ExtensionManager(IServiceProvider serviceProvider, string extensionsDirectory)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _extensionsDirectory = extensionsDirectory ?? throw new ArgumentNullException(nameof(extensionsDirectory));

        // Create the extensions directory if it doesn't exist
        if (!Directory.Exists(_extensionsDirectory))
        {
            Directory.CreateDirectory(_extensionsDirectory);
            Logs.Init($"Created extensions directory: {_extensionsDirectory}");
        }
    }

    /// <summary>
    /// Discovers all available extensions in the extensions directory.
    /// Scans each subdirectory for extension implementations by loading assemblies
    /// and finding types that derive from the Extension base class.
    /// </summary>
    /// <returns>A collection of discovered but not yet loaded extension instances</returns>
    public async Task<IEnumerable<Extension>> DiscoverExtensionsAsync()
    {
        List<Extension> discoveredExtensions = new();

        try
        {
            // Get all directories in the extensions folder - each should be an extension
            string[] extensionDirectories = Directory.GetDirectories(_extensionsDirectory);
            Logs.Info($"Found {extensionDirectories.Length} potential extension directories");

            foreach (string directory in extensionDirectories)
            {
                string extensionName = System.IO.Path.GetFileName(directory);
                Logs.Debug($"Scanning extension directory: {extensionName}");

                try
                {
                    // Look for extension DLLs in the directory
                    string[] dllFiles = Directory.GetFiles(directory, "*.dll");

                    foreach (string dllPath in dllFiles)
                    {
                        try
                        {
                            // Load the assembly and scan for extension types
                            Assembly assembly = Assembly.LoadFrom(dllPath);
                            Logs.Debug($"Loaded assembly: {assembly.FullName}");

                            // Find types that derive from Extension
                            var extensionTypes = assembly.GetTypes()
                                .Where(t => typeof(Extension).IsAssignableFrom(t) && !t.IsAbstract);

                            foreach (var extensionType in extensionTypes)
                            {
                                try
                                {
                                    // Create an instance of the extension
                                    Extension? extension = Activator.CreateInstance(extensionType) as Extension;

                                    if (extension != null)
                                    {
                                        Logs.Info($"Discovered extension: {extension.Name} v{extension.Version} by {extension.Author}");
                                        discoveredExtensions.Add(extension);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Logs.Error($"Failed to instantiate extension type {extensionType.FullName}: {ex.Message}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logs.Error($"Failed to load assembly {dllPath}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logs.Error($"Error scanning extension directory {directory}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to discover extensions: {ex.Message}");
        }

        return discoveredExtensions;
    }

    /// <summary>
    /// Loads all discovered extensions, respecting dependencies.
    /// Sorts extensions by dependency order, registers their services,
    /// and initializes them. Extensions with missing dependencies will not be loaded.
    /// </summary>
    /// <param name="serviceCollection">The service collection to register extension services with</param>
    /// <returns>The number of successfully loaded extensions</returns>
    public async Task<int> LoadAllExtensionsAsync(IServiceCollection serviceCollection)
    {
        try
        {
            // Discover all available extensions
            var extensions = await DiscoverExtensionsAsync();
            Logs.Info($"Discovered {extensions.Count()} extensions");

            // Sort extensions by dependency order
            List<Extension> sortedExtensions = SortExtensionsByDependencies(extensions);

            // Register extension services first
            foreach (var extension in sortedExtensions)
            {
                try
                {
                    extension.RegisterServices(serviceCollection);
                }
                catch (Exception ex)
                {
                    Logs.Error($"Failed to register services for extension {extension.Name}: {ex.Message}");
                }
            }

            // This counter tracks the number of extensions that were successfully loaded
            int loadedCount = 0;

            // Initialize extensions in dependency order
            foreach (var extension in sortedExtensions)
            {
                if (await LoadExtensionAsync(extension))
                {
                    loadedCount++;
                }
            }

            Logs.Info($"Successfully loaded {loadedCount} of {sortedExtensions.Count} extensions");
            return loadedCount;
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to load extensions: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Loads a single extension.
    /// Checks dependencies, initializes the extension, and adds it to the
    /// loaded extensions dictionary if successful.
    /// </summary>
    /// <param name="extension">The extension to load</param>
    /// <returns>True if the extension was successfully loaded; otherwise, false</returns>
    public async Task<bool> LoadExtensionAsync(Extension extension)
    {
        if (extension == null)
        {
            throw new ArgumentNullException(nameof(extension));
        }

        // Check if already loaded
        if (_loadedExtensions.TryGetValue(extension.Id, out _))
        {
            Logs.Warning($"Extension {extension.Name} is already loaded");
            return false;
        }

        // Check dependencies
        foreach (string dependencyId in extension.Dependencies)
        {
            if (!_loadedExtensions.ContainsKey(dependencyId))
            {
                Logs.Error($"Cannot load extension {extension.Name}: Missing dependency {dependencyId}");
                return false;
            }
        }

        try
        {
            // Initialize the extension
            if (await extension.InitializeAsync(_serviceProvider))
            {
                // Add to loaded extensions
                if (_loadedExtensions.TryAdd(extension.Id, extension))
                {
                    Logs.Info($"Successfully loaded extension: {extension.Name} v{extension.Version}");
                    return true;
                }
                else
                {
                    Logs.Error($"Failed to add extension {extension.Name} to loaded extensions dictionary");
                    await extension.ShutdownAsync();
                    return false;
                }
            }
            else
            {
                Logs.Error($"Extension {extension.Name} failed to initialize");
                return false;
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"Exception loading extension {extension.Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Unloads a single extension by its ID.
    /// Calls the extension's shutdown method and removes it from the loaded extensions.
    /// </summary>
    /// <param name="extensionId">The ID of the extension to unload</param>
    /// <returns>True if the extension was found and unloaded; otherwise, false</returns>
    public async Task<bool> UnloadExtensionAsync(string extensionId)
    {
        if (string.IsNullOrEmpty(extensionId))
        {
            throw new ArgumentNullException(nameof(extensionId));
        }

        // Try to get the extension
        if (_loadedExtensions.TryRemove(extensionId, out Extension? extension))
        {
            try
            {
                // Call shutdown
                await extension.ShutdownAsync();
                Logs.Info($"Unloaded extension: {extension.Name}");
                return true;
            }
            catch (Exception ex)
            {
                Logs.Error($"Error during extension shutdown for {extension.Name}: {ex.Message}");
                return false;
            }
        }

        Logs.Warning($"Extension with ID {extensionId} not found for unloading");
        return false;
    }

    /// <summary>
    /// Unloads all currently loaded extensions.
    /// Calls each extension's shutdown method in reverse dependency order.
    /// This is typically used during bot shutdown to ensure clean termination.
    /// </summary>
    /// <returns>The number of extensions successfully unloaded</returns>
    public async Task<int> UnloadAllExtensionsAsync()
    {
        int unloadedCount = 0;

        // Get all loaded extensions
        var extensions = _loadedExtensions.Values.ToList();

        // Sort in reverse dependency order
        List<Extension> sortedExtensions = SortExtensionsByDependencies(extensions);
        sortedExtensions.Reverse(); // Reverse to unload in opposite order

        // Unload each extension
        foreach (var extension in sortedExtensions)
        {
            if (await UnloadExtensionAsync(extension.Id))
            {
                unloadedCount++;
            }
        }

        Logs.Info($"Unloaded {unloadedCount} extensions");
        return unloadedCount;
    }

    /// <summary>
    /// Gets an extension by its ID.
    /// Used to access a specific extension's functionality or check its status.
    /// </summary>
    /// <param name="extensionId">The ID of the extension to get</param>
    /// <returns>The extension if found; otherwise, null</returns>
    public Extension? GetExtension(string extensionId)
    {
        if (string.IsNullOrEmpty(extensionId))
        {
            throw new ArgumentNullException(nameof(extensionId));
        }

        _loadedExtensions.TryGetValue(extensionId, out Extension? extension);
        return extension;
    }

    /// <summary>
    /// Gets all currently loaded extensions.
    /// Provides a snapshot of all active extensions for management or display.
    /// </summary>
    /// <returns>A collection of all loaded extensions</returns>
    public IEnumerable<Extension> GetAllLoadedExtensions()
    {
        return _loadedExtensions.Values.ToList();
    }

    /// <summary>
    /// Sorts a collection of extensions by dependency order.
    /// Uses a topological sort to ensure extensions are loaded/unloaded in the correct order,
    /// respecting dependencies between extensions.
    /// </summary>
    /// <param name="extensions">The extensions to sort</param>
    /// <returns>A list of extensions sorted by dependency order</returns>
    private List<Extension> SortExtensionsByDependencies(IEnumerable<Extension> extensions)
    {
        Dictionary<string, Extension> extensionMap = extensions.ToDictionary(e => e.Id);
        Dictionary<string, bool> visited = new();
        Dictionary<string, bool> inProgress = new();
        List<Extension> sorted = new();

        foreach (var extension in extensions)
        {
            if (!visited.ContainsKey(extension.Id))
            {
                if (!VisitExtension(extension.Id, extensionMap, visited, inProgress, sorted))
                {
                    Logs.Error($"Dependency cycle detected involving extension {extension.Id}");
                }
            }
        }

        return sorted;
    }

    /// <summary>
    /// Helper method for the topological sort algorithm.
    /// Recursively visits extensions and their dependencies to build the sorted list.
    /// </summary>
    /// <param name="id">The ID of the extension to visit</param>
    /// <param name="extensionMap">Map of extension IDs to extensions</param>
    /// <param name="visited">Set of visited extensions</param>
    /// <param name="inProgress">Set of extensions currently being processed (for cycle detection)</param>
    /// <param name="sorted">Output list of sorted extensions</param>
    /// <returns>True if sorting was successful; false if a cycle was detected</returns>
    private bool VisitExtension(string id, Dictionary<string, Extension> extensionMap, Dictionary<string, bool> visited, Dictionary<string, bool> inProgress, List<Extension> sorted)
    {
        // Check for cycles
        if (inProgress.ContainsKey(id))
        {
            return false; // Cycle detected
        }

        // If already visited, skip
        if (visited.ContainsKey(id))
        {
            return true;
        }

        // If the extension doesn't exist, skip
        if (!extensionMap.TryGetValue(id, out Extension? extension))
        {
            Logs.Warning($"Dependency {id} not found, skipping");
            return true;
        }

        // Mark as in progress for cycle detection
        inProgress[id] = true;

        // Visit dependencies first
        foreach (string dependencyId in extension.Dependencies)
        {
            if (!VisitExtension(dependencyId, extensionMap, visited, inProgress, sorted))
            {
                return false; // Propagate cycle detection
            }
        }

        // Mark as visited and add to sorted list
        visited[id] = true;
        inProgress.Remove(id);
        sorted.Add(extension);

        return true;
    }
}