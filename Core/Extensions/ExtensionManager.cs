using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using PlexBot.Utils;
using Path = System.IO.Path;

namespace PlexBot.Core.Extensions;

/// <summary>Custom AssemblyLoadContext for extensions that falls back to the host (default ALC)
/// for shared types. This ensures type identity is maintained for shared interfaces like
/// Extension, IMusicProvider, etc. while allowing extension-private dependencies.</summary>
internal sealed class ExtensionLoadContext(string extensionDllPath) : AssemblyLoadContext(isCollectible: false)
{
    private readonly AssemblyDependencyResolver _resolver = new(extensionDllPath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Check if the default ALC already has this assembly — reuse it for type identity
        foreach (Assembly asm in Default.Assemblies)
        {
            if (string.Equals(asm.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase))
                return null;
        }

        // Try to resolve from the extension's deps.json for private dependencies
        string? resolved = _resolver.ResolveAssemblyToPath(assemblyName);
        if (resolved != null)
            return LoadFromAssemblyPath(resolved);

        // Fall back to default ALC
        return null;
    }
}

/// <summary>Manages the discovery, building, loading, and lifecycle of bot extensions.
/// Extensions are built from source at startup via dotnet build, then loaded dynamically.</summary>
public class ExtensionManager
{
    private readonly ConcurrentDictionary<string, Extension> _loadedExtensions = [];
    private readonly List<Extension> _discoveredExtensions = [];
    private readonly string _extensionsSourceDirectory;
    private readonly string _extensionsBinDirectory;

    /// <summary>Initializes the extension manager</summary>
    /// <param name="extensionsSourceDirectory">Source directory containing extension folders (each with .csproj)</param>
    /// <param name="extensionsBinDirectory">Output directory for compiled extension DLLs</param>
    public ExtensionManager(string extensionsSourceDirectory, string extensionsBinDirectory)
    {
        _extensionsSourceDirectory = extensionsSourceDirectory ?? throw new ArgumentNullException(nameof(extensionsSourceDirectory));
        _extensionsBinDirectory = extensionsBinDirectory ?? throw new ArgumentNullException(nameof(extensionsBinDirectory));

        if (!Directory.Exists(_extensionsSourceDirectory))
        {
            Directory.CreateDirectory(_extensionsSourceDirectory);
            Logs.Init($"Created extensions source directory: {_extensionsSourceDirectory}");
        }

        if (!Directory.Exists(_extensionsBinDirectory))
        {
            Directory.CreateDirectory(_extensionsBinDirectory);
        }
    }

    /// <summary>Phase 1: Builds, discovers, and instantiates extensions from the source directory.
    /// Each extension with a .csproj is compiled via dotnet build, then the resulting DLL is loaded.
    /// Called BEFORE the DI container is built so extensions can register services.</summary>
    public async Task<IReadOnlyList<Extension>> DiscoverAndInstantiateAsync()
    {
        _discoveredExtensions.Clear();

        try
        {
            if (!Directory.Exists(_extensionsSourceDirectory))
            {
                Logs.Info("No extensions directory found — skipping extension discovery");
                return _discoveredExtensions.AsReadOnly();
            }

            string[] extensionDirectories = Directory.GetDirectories(_extensionsSourceDirectory);
            Logs.Info($"Found {extensionDirectories.Length} extension directories in {_extensionsSourceDirectory}");

            foreach (string directory in extensionDirectories)
            {
                string extensionName = Path.GetFileName(directory);

                // Skip directories marked for disabling
                if (extensionName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                {
                    Logs.Info($"Skipping disabled extension: {extensionName}");
                    continue;
                }

                try
                {
                    Assembly? assembly = await BuildAndLoadExtensionAsync(directory, extensionName);
                    if (assembly == null)
                        continue;

                    DiscoverExtensionTypes(assembly, extensionName);
                }
                catch (Exception ex)
                {
                    Logs.Error($"Error processing extension {extensionName}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to discover extensions: {ex.Message}");
        }

        Logs.Info($"Discovered {_discoveredExtensions.Count} extensions total");
        return _discoveredExtensions.AsReadOnly();
    }

    /// <summary>Builds an extension project and loads the compiled DLL</summary>
    private async Task<Assembly?> BuildAndLoadExtensionAsync(string extensionDir, string extensionName)
    {
        // Find .csproj file in the extension directory
        string[] csprojFiles = Directory.GetFiles(extensionDir, "*.csproj");
        if (csprojFiles.Length == 0)
        {
            Logs.Warning($"No .csproj found in {extensionName} — skipping");
            return null;
        }

        string csprojPath = Path.GetFullPath(csprojFiles[0]);
        string dllName = Path.GetFileNameWithoutExtension(csprojFiles[0]);
        string outputDir = Path.GetFullPath(Path.Combine(_extensionsBinDirectory, extensionName));
        string targetDll = Path.Combine(outputDir, $"{dllName}.dll");

#if DEBUG
        string configuration = "Debug";
#else
        string configuration = "Release";
        // In release mode, skip rebuild if DLL already exists
        if (File.Exists(targetDll))
        {
            Logs.Info($"Loading cached extension: {extensionName}");
            return LoadExtensionAssembly(targetDll);
        }
#endif

        Logs.Info($"Building extension: {extensionName}...");

        // Clean local bin/obj to avoid stale artifacts
        string localBin = Path.Combine(extensionDir, "bin");
        string localObj = Path.Combine(extensionDir, "obj");
        if (Directory.Exists(localBin)) Directory.Delete(localBin, true);
        if (Directory.Exists(localObj)) Directory.Delete(localObj, true);

        // Build the extension via dotnet build as a subprocess
        try
        {
            string hostOutputDir = Path.GetFullPath(AppContext.BaseDirectory);
            ProcessStartInfo psi = new("dotnet", $"build \"{csprojPath}\" -c {configuration} -o \"{outputDir}\" -p:HostOutputDir=\"{hostOutputDir}\"")
            {
                WorkingDirectory = Path.GetFullPath(extensionDir),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using Process process = new() { StartInfo = psi };
            process.Start();

            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                Logs.Error($"Failed to build extension {extensionName} (exit code {process.ExitCode}):");
                // Log build output line by line for readability
                foreach (string line in stdout.Split('\n').Where(l => l.Contains("error") || l.Contains("Error")))
                    Logs.Error($"  {line.Trim()}");
                if (!string.IsNullOrWhiteSpace(stderr))
                    Logs.Error($"  stderr: {stderr.Trim()}");
                return null;
            }

            Logs.Info($"Built extension: {extensionName}");
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to run dotnet build for {extensionName}: {ex.Message}");
            return null;
        }

        if (!File.Exists(targetDll))
        {
            Logs.Error($"Extension {extensionName} built but DLL not found at {targetDll}");
            return null;
        }

        return LoadExtensionAssembly(targetDll);
    }

    /// <summary>Loads a compiled extension DLL into a custom AssemblyLoadContext</summary>
    private static Assembly? LoadExtensionAssembly(string dllPath)
    {
        try
        {
            string fullPath = Path.GetFullPath(dllPath);
            ExtensionLoadContext loadContext = new(fullPath);
            Assembly assembly = loadContext.LoadFromAssemblyPath(fullPath);
            Logs.Info($"Loaded extension assembly: {assembly.GetName().Name}");
            return assembly;
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to load extension assembly {dllPath}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Scans an assembly for Extension subtypes and instantiates them</summary>
    private void DiscoverExtensionTypes(Assembly assembly, string extensionName)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException rtle)
        {
            foreach (Exception? loaderEx in rtle.LoaderExceptions)
            {
                if (loaderEx != null)
                    Logs.Warning($"  Type load issue in {extensionName}: {loaderEx.Message}");
            }
            types = rtle.Types.OfType<Type>().ToArray();
        }

        var extensionTypes = types
            .Where(t => typeof(Extension).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        if (extensionTypes.Count == 0)
        {
            Logs.Warning($"No Extension subtypes found in {extensionName} ({types.Length} types scanned)");
            return;
        }

        foreach (var extensionType in extensionTypes)
        {
            try
            {
                if (Activator.CreateInstance(extensionType) is Extension extension)
                {
                    extension.SourceAssembly = assembly;
                    _discoveredExtensions.Add(extension);
                    Logs.Info($"Discovered extension: {extension.Name} v{extension.Version} by {extension.Author}");
                }
            }
            catch (Exception ex)
            {
                Logs.Error($"Failed to instantiate extension type {extensionType.FullName}: {ex.Message}");
            }
        }
    }

    /// <summary>Phase 2: Initializes all discovered extensions with the fully-built service provider.
    /// Called AFTER the DI container is built so extensions can resolve their registered services.</summary>
    public async Task<int> InitializeAllAsync(IServiceProvider serviceProvider)
    {
        try
        {
            List<Extension> sortedExtensions = SortExtensionsByDependencies(_discoveredExtensions);

            string botVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0";
            int loadedCount = 0;

            foreach (Extension extension in sortedExtensions)
            {
                if (!IsVersionCompatible(extension.MinimumBotVersion, botVersion))
                {
                    Logs.Error($"Extension {extension.Name} requires bot version {extension.MinimumBotVersion}, current is {botVersion}. Skipping.");
                    continue;
                }

                if (await LoadExtensionAsync(extension, serviceProvider))
                    loadedCount++;
            }

            Logs.Info($"Successfully initialized {loadedCount} of {sortedExtensions.Count} extensions");
            return loadedCount;
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to initialize extensions: {ex.Message}");
            return 0;
        }
    }

    /// <summary>Loads a single extension after verifying dependencies</summary>
    private async Task<bool> LoadExtensionAsync(Extension extension, IServiceProvider serviceProvider)
    {
        if (_loadedExtensions.TryGetValue(extension.Id, out _))
        {
            Logs.Warning($"Extension {extension.Name} is already loaded");
            return false;
        }

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
            if (await extension.InitializeAsync(serviceProvider))
            {
                if (_loadedExtensions.TryAdd(extension.Id, extension))
                {
                    Logs.Info($"Successfully loaded extension: {extension.Name} v{extension.Version}");
                    return true;
                }

                Logs.Error($"Failed to add extension {extension.Name} to loaded extensions dictionary");
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to initialize extension {extension.Name}: {ex.Message}");
        }

        return false;
    }

    /// <summary>Unloads a single extension by its ID</summary>
    public async Task<bool> UnloadExtensionAsync(string extensionId)
    {
        if (string.IsNullOrEmpty(extensionId))
            return false;

        if (!_loadedExtensions.TryGetValue(extensionId, out Extension? extension))
        {
            Logs.Warning($"Extension {extensionId} is not loaded");
            return false;
        }

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
            await extension.ShutdownAsync();

            if (_loadedExtensions.TryRemove(extensionId, out _))
            {
                Logs.Info($"Successfully unloaded extension: {extension.Name}");
                return true;
            }

            Logs.Error($"Failed to remove extension {extension.Name} from loaded extensions dictionary");
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to shutdown extension {extension.Name}: {ex.Message}");
        }

        return false;
    }

    /// <summary>Unloads all extensions in reverse dependency order</summary>
    public async Task<int> UnloadAllExtensionsAsync()
    {
        try
        {
            var extensions = _loadedExtensions.Values.ToList();
            List<Extension> sortedExtensions = SortExtensionsByDependencies(extensions);
            sortedExtensions.Reverse();

            int unloadedCount = 0;

            foreach (var extension in sortedExtensions)
            {
                if (await UnloadExtensionAsync(extension.Id))
                    unloadedCount++;
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

    /// <summary>Gets a loaded extension by its ID</summary>
    public Extension? GetExtension(string extensionId)
    {
        if (string.IsNullOrEmpty(extensionId))
            return null;

        _loadedExtensions.TryGetValue(extensionId, out Extension? extension);
        return extension;
    }

    /// <summary>Gets a loaded extension by type</summary>
    public T? GetExtension<T>() where T : Extension =>
        _loadedExtensions.Values.OfType<T>().FirstOrDefault();

    /// <summary>Gets all currently loaded extensions</summary>
    public IEnumerable<Extension> GetAllExtensions() => _loadedExtensions.Values;

    /// <summary>Checks if a required version is compatible with the current version</summary>
    private static bool IsVersionCompatible(string requiredVersion, string currentVersion)
    {
        if (System.Version.TryParse(requiredVersion, out Version? required) &&
            System.Version.TryParse(currentVersion, out Version? current))
        {
            return current >= required;
        }
        return true;
    }

    /// <summary>Sorts extensions based on their dependencies using topological sort</summary>
    private static List<Extension> SortExtensionsByDependencies(IEnumerable<Extension> extensions)
    {
        Dictionary<string, Extension> extensionsDict = extensions.ToDictionary(e => e.Id);
        Dictionary<string, List<string>> dependencyGraph = [];

        foreach (var extension in extensions)
            dependencyGraph[extension.Id] = extension.Dependencies.ToList();

        List<string> sortedIds = TopologicalSort(dependencyGraph);

        List<Extension> sortedExtensions = [];
        foreach (string id in sortedIds)
        {
            if (extensionsDict.TryGetValue(id, out Extension? extension))
                sortedExtensions.Add(extension);
        }

        return sortedExtensions;
    }

    private static List<string> TopologicalSort(Dictionary<string, List<string>> graph)
    {
        List<string> result = [];
        HashSet<string> visited = [];
        HashSet<string> temp = [];

        foreach (string node in graph.Keys)
        {
            if (!visited.Contains(node) && !temp.Contains(node))
                TopologicalSortVisit(node, graph, visited, temp, result);
        }

        return result;
    }

    private static void TopologicalSortVisit(string node, Dictionary<string, List<string>> graph,
        HashSet<string> visited, HashSet<string> temp, List<string> result)
    {
        if (temp.Contains(node))
            throw new InvalidOperationException($"Cyclic dependency detected involving extension {node}");

        if (!visited.Contains(node))
        {
            temp.Add(node);

            if (graph.TryGetValue(node, out List<string>? dependencies))
            {
                foreach (string dependency in dependencies)
                {
                    if (graph.ContainsKey(dependency))
                        TopologicalSortVisit(dependency, graph, visited, temp, result);
                }
            }

            temp.Remove(node);
            visited.Add(node);
            result.Add(node);
        }
    }
}
