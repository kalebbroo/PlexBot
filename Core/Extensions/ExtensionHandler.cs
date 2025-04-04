using PlexBot.Core.Models.Extensions;
using PlexBot.Utils;

namespace PlexBot.Core.Extensions;

/// <summary>
/// Manages the discovery, loading, and lifecycle of bot extensions.
/// This static class provides the primary interface for interacting with the extension
/// system, handling the loading of extension files, initialization of extension instances,
/// and maintenance of extension state.
/// </summary>
public static class ExtensionHandler
{
    /// <summary>
    /// Gets the collection of all loaded extensions.
    /// Provides access to currently active extensions for status reporting and management.
    /// </summary>
    public static List<Extension> LoadedExtensions { get; } = new();

    /// <summary>
    /// Gets the base directory where extensions are stored.
    /// This is the root directory that contains subdirectories for each extension.
    /// </summary>
    public static string ExtensionsDirectory { get; private set; } = string.Empty;

    /// <summary>
    /// Initializes the extension system with the specified extensions directory.
    /// Sets up the extension handler and ensures the extensions directory exists.
    /// </summary>
    /// <param name="extensionsDirectory">The directory path where extensions are located</param>
    public static void Initialize(string extensionsDirectory)
    {
        ExtensionsDirectory = extensionsDirectory;

        if (!Directory.Exists(ExtensionsDirectory))
        {
            Directory.CreateDirectory(ExtensionsDirectory);
            Logs.Init($"Created extensions directory: {ExtensionsDirectory}");
        }

        Logs.Init($"Extension handler initialized with directory: {ExtensionsDirectory}");
    }

    /// <summary>
    /// Discovers all available extensions in the extensions directory.
    /// Scans the extensions directory for extension implementations by loading CS files
    /// and finding types that derive from the Extension base class.
    /// </summary>
    /// <returns>A collection of discovered extension types</returns>
    public static IEnumerable<Type> DiscoverExtensions()
    {
        List<Type> discoveredExtensions = new();

        if (!Directory.Exists(ExtensionsDirectory))
        {
            Logs.Warning($"Extensions directory does not exist: {ExtensionsDirectory}");
            return discoveredExtensions;
        }

        // Get all subdirectories in the extensions folder - each should be an extension
        string[] extensionDirectories = Directory.GetDirectories(ExtensionsDirectory);
        Logs.Info($"Found {extensionDirectories.Length} potential extension directories");

        foreach (string directory in extensionDirectories)
        {
            string extensionName = System.IO.Path.GetFileName(directory);
            Logs.Debug($"Scanning extension directory: {extensionName}");

            try
            {
                // Look for dll files in the directory
                string[] dllFiles = Directory.GetFiles(directory, "*.dll");

                foreach (string dllPath in dllFiles)
                {
                    try
                    {
                        // Load the assembly
                        Assembly assembly = Assembly.LoadFrom(dllPath);
                        Logs.Debug($"Loaded assembly: {assembly.FullName}");

                        // Find types that derive from Extension
                        var extensionTypes = assembly.GetTypes()
                            .Where(t => typeof(Extension).IsAssignableFrom(t) && !t.IsAbstract);

                        foreach (var extensionType in extensionTypes)
                        {
                            Logs.Info($"Discovered extension type: {extensionType.FullName}");
                            discoveredExtensions.Add(extensionType);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logs.Error($"Failed to load assembly {dllPath}: {ex.Message}");
                    }
                }

                // Also look for direct CS files for development/direct execution
                string[] csFiles = Directory.GetFiles(directory, "*.cs");
                if (csFiles.Length > 0)
                {
                    Logs.Debug($"Found {csFiles.Length} CS files in extension directory");
                    // These will be compiled at runtime if needed
                }
            }
            catch (Exception ex)
            {
                Logs.Error($"Error scanning extension directory {directory}: {ex.Message}");
            }
        }

        return discoveredExtensions;
    }

    /// <summary>
    /// Loads all discovered extensions, respecting dependencies.
    /// Creates instances of discovered extension types, registers their services,
    /// and initializes them in the correct order to satisfy dependencies.
    /// </summary>
    /// <param name="services">The service collection to register extension services with</param>
    /// <returns>The number of successfully loaded extensions</returns>
    public static async Task<int> LoadAllExtensionsAsync(IServiceCollection services, IServiceProvider serviceProvider)
    {
        // Discover all available extensions
        var extensionTypes = DiscoverExtensions();

        // Create instances of all extensions
        List<Extension> extensions = new();
        foreach (var type in extensionTypes)
        {
            try
            {
                Extension extension = (Extension)Activator.CreateInstance(type)!;
                extensions.Add(extension);
            }
            catch (Exception ex)
            {
                Logs.Error($"Failed to create instance of extension type {type.FullName}: {ex.Message}");
            }
        }

        // Sort extensions by dependency order
        List<Extension> sortedExtensions = SortExtensionsByDependencies(extensions);

        // Register extension services first
        foreach (var extension in sortedExtensions)
        {
            try
            {
                Logs.Debug($"Registering services for extension: {extension.Name}");
                extension.RegisterServices(services);
            }
            catch (Exception ex)
            {
                Logs.Error($"Failed to register services for extension {extension.Name}: {ex.Message}");
            }
        }

        // Initialize extensions in dependency order
        int loadedCount = 0;
        foreach (var extension in sortedExtensions)
        {
            if (await LoadExtensionAsync(extension, serviceProvider))
            {
                loadedCount++;
            }
        }

        Logs.Info($"Successfully loaded {loadedCount} of {sortedExtensions.Count} extensions");
        return loadedCount;
    }

    /// <summary>
    /// Loads a single extension after verifying dependencies.
    /// Initializes the extension if all its dependencies are satisfied and
    /// adds it to the loaded extensions collection.
    /// </summary>
    /// <param name="extension">The extension to load</param>
    /// <param name="serviceProvider">The service provider for dependency injection</param>
    /// <returns>True if the extension was successfully loaded; otherwise, false</returns>
    public static async Task<bool> LoadExtensionAsync(Extension extension, IServiceProvider serviceProvider)
    {
        if (extension == null)
        {
            throw new ArgumentNullException(nameof(extension));
        }

        // Check if already loaded
        if (LoadedExtensions.Any(e => e.Id == extension.Id))
        {
            Logs.Warning($"Extension {extension.Name} is already loaded");
            return false;
        }

        // Check dependencies
        foreach (string dependencyId in extension.Dependencies)
        {
            if (!LoadedExtensions.Any(e => e.Id == dependencyId))
            {
                Logs.Error($"Cannot load extension {extension.Name}: Missing dependency {dependencyId}");
                return false;
            }
        }

        try
        {
            // Initialize the extension
            Logs.Info($"Initializing extension: {extension.Name} v{extension.Version}");
            if (await extension.InitializeAsync(serviceProvider))
            {
                // Add to loaded extensions
                LoadedExtensions.Add(extension);
                Logs.Info($"Successfully loaded extension: {extension.Name} v{extension.Version}");
                return true;
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
    public static async Task<bool> UnloadExtensionAsync(string extensionId)
    {
        if (string.IsNullOrEmpty(extensionId))
        {
            throw new ArgumentNullException(nameof(extensionId));
        }

        // Find the extension
        Extension? extension = LoadedExtensions.FirstOrDefault(e => e.Id == extensionId);
        if (extension == null)
        {
            Logs.Warning($"Extension with ID {extensionId} not found for unloading");
            return false;
        }

        try
        {
            // Call shutdown
            await extension.ShutdownAsync();

            // Remove from loaded extensions
            LoadedExtensions.Remove(extension);

            Logs.Info($"Unloaded extension: {extension.Name}");
            return true;
        }
        catch (Exception ex)
        {
            Logs.Error($"Error during extension shutdown for {extension.Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Unloads all currently loaded extensions.
    /// Calls each extension's shutdown method in reverse dependency order.
    /// </summary>
    /// <returns>The number of extensions successfully unloaded</returns>
    public static async Task<int> UnloadAllExtensionsAsync()
    {
        int unloadedCount = 0;

        // Sort extensions by dependency order
        List<Extension> sortedExtensions = SortExtensionsByDependencies(LoadedExtensions);
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
    /// Looks up a loaded extension by its unique identifier.
    /// </summary>
    /// <param name="extensionId">The ID of the extension to find</param>
    /// <returns>The extension if found; otherwise, null</returns>
    public static Extension? GetExtension(string extensionId)
    {
        if (string.IsNullOrEmpty(extensionId))
        {
            throw new ArgumentNullException(nameof(extensionId));
        }

        return LoadedExtensions.FirstOrDefault(e => e.Id == extensionId);
    }

    /// <summary>
    /// Sorts a collection of extensions by dependency order.
    /// Uses a topological sort to ensure extensions are loaded/unloaded in the correct order,
    /// respecting dependencies between extensions.
    /// </summary>
    /// <param name="extensions">The extensions to sort</param>
    /// <returns>A list of extensions sorted by dependency order</returns>
    private static List<Extension> SortExtensionsByDependencies(IEnumerable<Extension> extensions)
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
    private static bool VisitExtension(string id, Dictionary<string, Extension> extensionMap, Dictionary<string, bool> visited, Dictionary<string, bool> inProgress, List<Extension> sorted)
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