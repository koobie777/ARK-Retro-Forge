using System.Reflection;
using System.Runtime.Loader;

namespace ARK.Core.Plugins;

/// <summary>
/// Manages plugin discovery and loading using AssemblyLoadContext
/// </summary>
public class PluginManager
{
    private readonly string _pluginsDirectory;
    private readonly List<ISystemModule> _loadedModules = [];
    private readonly List<AssemblyLoadContext> _loadContexts = [];

    public IReadOnlyList<ISystemModule> LoadedModules => _loadedModules.AsReadOnly();

    public PluginManager(string? pluginsDirectory = null)
    {
        _pluginsDirectory = pluginsDirectory ?? Path.Combine(AppContext.BaseDirectory, "plugins");
    }

    /// <summary>
    /// Discover and load all plugins from the plugins directory
    /// </summary>
    public async Task<int> LoadPluginsAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_pluginsDirectory))
        {
            Directory.CreateDirectory(_pluginsDirectory);
            return 0;
        }

        var pluginFiles = Directory.GetFiles(_pluginsDirectory, "*.dll", SearchOption.TopDirectoryOnly);
        var loadedCount = 0;

        foreach (var pluginFile in pluginFiles)
        {
            try
            {
                var module = LoadPlugin(pluginFile);
                if (module != null)
                {
                    await module.InitializeAsync(cancellationToken);
                    _loadedModules.Add(module);
                    loadedCount++;
                }
            }
            catch (Exception ex)
            {
                // Log error but continue loading other plugins
                Console.WriteLine($"Failed to load plugin {pluginFile}: {ex.Message}");
            }
        }

        return loadedCount;
    }

    /// <summary>
    /// Load a single plugin file
    /// </summary>
    private ISystemModule? LoadPlugin(string pluginPath)
    {
        var loadContext = new PluginLoadContext(pluginPath);
        _loadContexts.Add(loadContext);

        var assembly = loadContext.LoadFromAssemblyPath(pluginPath);

        foreach (var type in assembly.GetTypes())
        {
            if (typeof(ISystemModule).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract)
            {
                var instance = Activator.CreateInstance(type) as ISystemModule;
                if (instance != null)
                {
                    return instance;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Unload all plugins
    /// </summary>
    public void UnloadPlugins()
    {
        _loadedModules.Clear();

        foreach (var context in _loadContexts)
        {
            context.Unload();
        }

        _loadContexts.Clear();
    }

    /// <summary>
    /// Custom AssemblyLoadContext for plugin isolation
    /// </summary>
    private class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public PluginLoadContext(string pluginPath) : base(isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
            if (assemblyPath != null)
            {
                return LoadFromAssemblyPath(assemblyPath);
            }

            return null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (libraryPath != null)
            {
                return LoadUnmanagedDllFromPath(libraryPath);
            }

            return IntPtr.Zero;
        }
    }
}
