using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;

using TrainingServer.Extensibility;

namespace TrainingServer;

internal class PluginManager
{
	public event Action? OnPluginsListUpdated;

	public HashSet<string> SearchPath = new() { ".", "./plugins" };
	public readonly ConcurrentDictionary<IPlugin, bool> _loadedPlugins = new();

	readonly IServer _managingServer;

	readonly Dictionary<string, DateTime> _loadedPluginTimes = new();
	readonly Dictionary<Type, object> _injectionDict;

	public PluginManager(IServer managingServer)
	{
		_managingServer = managingServer;

		_injectionDict = new() {
			{ typeof(IServer), _managingServer },
			{ typeof(Aircraft[]), () => _managingServer.Aircraft.Values.ToArray() }
		};

		Task.Run(ScanPluginsAsync);
	}

	public Task TickAsync(TimeSpan delta) => Task.WhenAll(
		_loadedPlugins.Where(kvp => kvp.Value).AsParallel().Select(kvp => kvp.Key.TickAsync(delta))
	);

	public Task ProcessTextMessageAsync(string sender, string recipient, string message) => Task.WhenAll(
		_loadedPlugins.Where(kvp => kvp.Value).AsParallel().Select(kvp => kvp.Key.ProcessTextMessageAsync(sender, recipient, message))
	);

	/// <summary>Scans for plugins being added/removed/updated.</summary>
	private async Task ScanPluginsAsync()
	{
		while (true)
		{
			HashSet<string> locatedDlls = new();

			// Find _EVERYTHING_.
			foreach (string path in SearchPath)
			{
				if (!Directory.Exists(path))
					continue;

				locatedDlls.UnionWith(Directory.EnumerateFiles(path, "*.dll", SearchOption.AllDirectories));
			}

			// Remove anything unmodified since the last sweep.
			foreach (string path in locatedDlls)
			{
				if (_loadedPluginTimes.TryGetValue(path, out var oldTime) && File.GetLastWriteTime(path) == oldTime)
				{
					locatedDlls.Remove(path);
					continue;
				}

				_loadedPluginTimes[path] = File.GetLastWriteTime(path);
			}

			// Reflect the DLLs into something we can use.
			HashSet<Type> pluginTypes = new();

			foreach (string path in locatedDlls)
			{
				Assembly asm = Assembly.LoadFile(path);

				pluginTypes.UnionWith(asm.GetExportedTypes().Where(et => et.GetInterfaces().Contains(typeof(IPlugin))));
			}

			/// <summary>Generate a plugin from a given <see cref="ConstructorInfo"/>.</summary>
			IPlugin Instantiate(ConstructorInfo ci)
			{
				List<object> paramList = new();
				foreach (ParameterInfo pi in ci.GetParameters())
				{
					object v = _injectionDict[pi.ParameterType];

					if (v.GetType() == typeof(Func<>).MakeGenericType(pi.ParameterType))
						v = ((dynamic)v).Invoke();

					paramList.Add(v);
				}

				return (IPlugin)ci.Invoke(paramList.ToArray());
			}

			bool updated = pluginTypes.Any();

			// Instantiate the types while building the injection dictionary.
			for (int iteration = 0; pluginTypes.Any() && iteration < pluginTypes.Count; ++iteration)
			{
				foreach (Type pluginType in pluginTypes)
				{
					if (pluginType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).Where(c => c.GetParameters().All(pi => _injectionDict.ContainsKey(pi.ParameterType))).FirstOrDefault() is ConstructorInfo ci)
					{
						IPlugin plugin = Instantiate(ci);
						_loadedPlugins[plugin] = false;
						_injectionDict[pluginType] = plugin;

						pluginTypes.Remove(pluginType);
					}
				}
			}

			if (pluginTypes.Any())
				throw new TypeLoadException("Cannot load plugins due to missing dependencies: " + string.Join(", ", pluginTypes.Select(t => t.FullName)));

			if (updated)
				OnPluginsListUpdated?.Invoke();

			await Task.Delay(5000);
		}
	}
}
