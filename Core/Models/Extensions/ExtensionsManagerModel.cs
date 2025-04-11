using System.Collections.Concurrent;
using PlexBot.Utils;

namespace PlexBot.Core.Models.Extensions;

/// <summary>Manages the discovery, loading, and lifecycle of bot extensions, providing a central coordination point for all extension operations</summary>
public class ExtensionManager
{
    /// <summary>Dictionary of all loaded extensions keyed by their unique ID for fast lookup and dependency management</summary>
    private readonly ConcurrentDictionary<string, Extension> _loadedExtensions = [];

    /// <summary>Service provider for dependency injection to supply extensions with required services during initialization</summary>
    private readonly IServiceProvider _serviceProvider;

    /// <summary>Base directory path where extension folders are located, with each extension having its own subdirectory</summary>
    private readonly string _extensionsDirectory;

    /// <summary>Initializes a new ExtensionManager with the necessary dependencies for loading and managing extensions</summary>
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

    /// <summary>Discovers available extensions by scanning subdirectories for assemblies containing Extension-derived types</summary>
    /// <returns>A collection of discovered but not yet loaded extension instances</returns>
    public IEnumerable<Extension> DiscoverExtensions()
    {
        List<Extension> discoveredExtensions = [];

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
                                    if (Activator.CreateInstance(extensionType) is Extension extension)
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

    /// <summary>Loads all discovered extensions in dependency order, registering their services and initializing them</summary>
    /// <param name="serviceCollection">The service collection to register extension services with</param>
    /// <returns>The number of successfully loaded extensions</returns>
    public async Task<int> LoadAllExtensionsAsync(IServiceCollection serviceCollection)
    {
        try
        {
            // Discover all available extensions
            var extensions = DiscoverExtensions();
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

    /// <summary>Loads a single extension by checking dependencies, initializing it, and adding it to the loaded extensions</summary>
    /// <param name="extension">The extension to load</param>
    /// <returns>True if the extension was successfully loaded; otherwise, false</returns>
    public async Task<bool> LoadExtensionAsync(Extension extension)
    {
        if (extension != null)
        {
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
                    Logs.Error($"Extension {extension.Name} depends on {dependencyId} which is not loaded");
                    return false;
                }
            }

            try
            {
                // Initialize the extension
                Logs.Info($"Initializing extension: {extension.Name}");
                await extension.InitializeAsync(_serviceProvider);

                // Add to loaded extensions
                if (_loadedExtensions.TryAdd(extension.Id, extension))
                {
                    Logs.Info($"Successfully loaded extension: {extension.Name}");
                    return true;
                }
                else
                {
                    Logs.Error($"Failed to add extension {extension.Name} to loaded extensions dictionary");
                }
            }
            catch (Exception ex)
            {
                Logs.Error($"Failed to initialize extension {extension.Name}: {ex.Message}");
            }
        }

        return false;
    }

    /// <summary>Unloads a single extension by shutting it down and removing it from the loaded extensions</summary>
    /// <param name="extensionId">The ID of the extension to unload</param>
    /// <returns>True if the extension was successfully unloaded; otherwise, false</returns>
    public async Task<bool> UnloadExtensionAsync(string extensionId)
    {
        if (string.IsNullOrEmpty(extensionId))
        {
            return false;
        }

        // Check if the extension is loaded
        if (!_loadedExtensions.TryGetValue(extensionId, out Extension? extension))
        {
            Logs.Warning($"Extension {extensionId} is not loaded");
            return false;
        }

        // Check if other extensions depend on this one
        foreach (var loadedExtension in _loadedExtensions.Values)
        {
            if (loadedExtension.Dependencies.Contains(extensionId))
            {
                Logs.Error($"Cannot unload extension {extension.Name} because {loadedExtension.Name} depends on it");
                return false;
            }
        }

        try
        {
            // Shutdown the extension
            Logs.Info($"Shutting down extension: {extension.Name}");
            await extension.ShutdownAsync();

            // Remove from loaded extensions
            if (_loadedExtensions.TryRemove(extensionId, out _))
            {
                Logs.Info($"Successfully unloaded extension: {extension.Name}");
                return true;
            }
            else
            {
                Logs.Error($"Failed to remove extension {extension.Name} from loaded extensions dictionary");
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to shutdown extension {extension.Name}: {ex.Message}");
        }

        return false;
    }

    /// <summary>Unloads all extensions in reverse dependency order to ensure safe shutdown</summary>
    /// <returns>The number of successfully unloaded extensions</returns>
    public async Task<int> UnloadAllExtensionsAsync()
    {
        try
        {
            // Get all loaded extensions
            var extensions = _loadedExtensions.Values.ToList();

            // Sort extensions in reverse dependency order
            List<Extension> sortedExtensions = SortExtensionsByDependencies(extensions);
            sortedExtensions.Reverse(); // Reverse to unload in opposite order of loading

            // This counter tracks the number of extensions that were successfully unloaded
            int unloadedCount = 0;

            // Unload extensions in reverse dependency order
            foreach (var extension in sortedExtensions)
            {
                if (await UnloadExtensionAsync(extension.Id))
                {
                    unloadedCount++;
                }
            }

            Logs.Info($"Successfully unloaded {unloadedCount} of {sortedExtensions.Count} extensions");
            return unloadedCount;
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to unload extensions: {ex.Message}");
            return 0;
        }
    }

    /// <summary>Gets a loaded extension by its ID for direct access to its functionality</summary>
    /// <param name="extensionId">The ID of the extension to retrieve</param>
    /// <returns>The loaded extension if found; otherwise, null</returns>
    public Extension? GetExtension(string extensionId)
    {
        if (string.IsNullOrEmpty(extensionId))
        {
            return null;
        }

        _loadedExtensions.TryGetValue(extensionId, out Extension? extension);
        return extension;
    }

    /// <summary>Gets all currently loaded extensions for status reporting and management</summary>
    /// <returns>A collection of all loaded extensions</returns>
    public IEnumerable<Extension> GetAllExtensions()
    {
        return _loadedExtensions.Values;
    }

    /// <summary>Sorts extensions based on their dependencies to ensure proper loading order</summary>
    /// <param name="extensions">The extensions to sort</param>
    /// <returns>A list of extensions sorted by dependency order</returns>
    private List<Extension> SortExtensionsByDependencies(IEnumerable<Extension> extensions)
    {
        // Create a dictionary of extensions by ID for quick lookup
        Dictionary<string, Extension> extensionsDict = extensions.ToDictionary(e => e.Id);

        // Create a graph of dependencies
        Dictionary<string, List<string>> dependencyGraph = new();
        foreach (var extension in extensions)
        {
            dependencyGraph[extension.Id] = extension.Dependencies.ToList();
        }

        // Perform a topological sort
        List<string> sortedIds = TopologicalSort(dependencyGraph);

        // Convert back to Extension objects
        List<Extension> sortedExtensions = [];
        foreach (string id in sortedIds)
        {
            if (extensionsDict.TryGetValue(id, out Extension? extension))
            {
                sortedExtensions.Add(extension);
            }
        }

        return sortedExtensions;
    }

    /// <summary>Performs a topological sort on a dependency graph to determine the correct loading sequence</summary>
    /// <param name="graph">The dependency graph where keys are node IDs and values are lists of dependencies</param>
    /// <returns>A list of node IDs in topological order</returns>
    private List<string> TopologicalSort(Dictionary<string, List<string>> graph)
    {
        List<string> result = [];
        HashSet<string> visited = [];
        HashSet<string> temp = [];

        // Visit all nodes
        foreach (string node in graph.Keys)
        {
            if (!visited.Contains(node) && !temp.Contains(node))
            {
                TopologicalSortVisit(node, graph, visited, temp, result);
            }
        }

        return result;
    }

    /// <summary>Helper method for the topological sort algorithm to visit nodes in the dependency graph</summary>
    /// <param name="node">The current node being visited</param>
    /// <param name="graph">The dependency graph</param>
    /// <param name="visited">Set of permanently visited nodes</param>
    /// <param name="temp">Set of temporarily visited nodes (for cycle detection)</param>
    /// <param name="result">The result list being built</param>
    private void TopologicalSortVisit(
        string node,
        Dictionary<string, List<string>> graph,
        HashSet<string> visited,
        HashSet<string> temp,
        List<string> result)
    {
        // Check for cycles
        if (temp.Contains(node))
        {
            throw new InvalidOperationException($"Cyclic dependency detected involving extension {node}");
        }

        if (!visited.Contains(node))
        {
            temp.Add(node);

            // Visit all dependencies
            if (graph.TryGetValue(node, out List<string>? dependencies))
            {
                foreach (string dependency in dependencies)
                {
                    if (graph.ContainsKey(dependency)) // Only visit dependencies that exist in the graph
                    {
                        TopologicalSortVisit(dependency, graph, visited, temp, result);
                    }
                }
            }

            temp.Remove(node);
            visited.Add(node);
            result.Add(node);
        }
    }
}