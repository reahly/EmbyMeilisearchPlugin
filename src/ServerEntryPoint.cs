using System;
using System.Linq;
using System.Threading.Tasks;
using EmbyMeilisearchPlugin.Patches;
using EmbyMeilisearchPlugin.Services;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace EmbyMeilisearchPlugin
{
    public class ServerEntryPoint : IServerEntryPoint
    {
        public static ServerEntryPoint Instance { get; private set; }

        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IJsonSerializer _jsonSerializer;
        private MeilisearchService _meilisearchService;

        public MeilisearchService MeilisearchService => _meilisearchService;

        public ServerEntryPoint(
            ILogManager logManager,
            ILibraryManager libraryManager,
            IJsonSerializer jsonSerializer)
        {
            _logger = logManager.GetLogger("MeilisearchPlugin");
            _libraryManager = libraryManager;
            _jsonSerializer = jsonSerializer;
            Instance = this;
        }

        private static void EnsureHarmonyLoaded(ILogger logger)
        {
            var pluginDir = Path.GetDirectoryName(Plugin.Instance.AssemblyFilePath);
            var harmonyPath = Path.Combine(pluginDir, "0Harmony.dll");

            if (!File.Exists(harmonyPath))
                throw new FileNotFoundException("0Harmony.dll not found", harmonyPath);

            var alc = AssemblyLoadContext.GetLoadContext(Assembly.GetExecutingAssembly());
            alc.LoadFromAssemblyPath(harmonyPath);
        }

        public void Run()
        {
            _logger.Info("Meilisearch Plugin starting...");

            try
            {
                var config = Plugin.Instance?.Configuration;
                if (config == null || !config.Enabled)
                {
                    _logger.Info("Meilisearch plugin is disabled");
                    return;
                }

                _meilisearchService = new MeilisearchService(_logger, _libraryManager, _jsonSerializer);

                Task.Run(async () =>
                {
                    try
                    {
                        await _meilisearchService.InitializeAsync();
                        EnsureHarmonyLoaded(_logger);
                        SearchPatches.Initialize(_logger, _libraryManager);

                        if (config.AutoSync)
                        {
                            _libraryManager.ItemAdded += OnItemAdded;
                            _libraryManager.ItemUpdated += OnItemUpdated;
                            _libraryManager.ItemRemoved += OnItemRemoved;
                        }

                        _logger.Info("Meilisearch plugin initialized successfully");
                    }
                    catch (Exception ex)
                    {
                        _logger.ErrorException("Failed to initialize Meilisearch plugin", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Failed to start Meilisearch plugin", ex);
            }
        }

        private void OnItemAdded(object sender, ItemChangeEventArgs e)
        {
            if (!ShouldIndexItem(e.Item)) return;
            Task.Run(async () =>
            {
                try { await _meilisearchService.IndexItemsAsync(new[] { e.Item }); }
                catch (Exception ex) { _logger.ErrorException("Failed to index: " + e.Item.Name, ex); }
            });
        }

        private void OnItemUpdated(object sender, ItemChangeEventArgs e)
        {
            if (!ShouldIndexItem(e.Item)) return;
            Task.Run(async () =>
            {
                try { await _meilisearchService.IndexItemsAsync(new[] { e.Item }); }
                catch (Exception ex) { _logger.ErrorException("Failed to update: " + e.Item.Name, ex); }
            });
        }

        private void OnItemRemoved(object sender, ItemChangeEventArgs e)
        {
            Task.Run(async () =>
            {
                try { await _meilisearchService.RemoveItemAsync(e.Item.Id); }
                catch (Exception ex) { _logger.ErrorException("Failed to remove: " + e.Item.Name, ex); }
            });
        }

        private bool ShouldIndexItem(MediaBrowser.Controller.Entities.BaseItem item)
        {
            if (item == null) return false;

            var config = Plugin.Instance?.Configuration;
            if (config == null) return false;

            var typeName = item.GetType().Name;
            var includeTypes = config.IncludeItemTypes
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim());

            if (includeTypes.Contains(typeName, StringComparer.OrdinalIgnoreCase))
                return true;

            if (item is LiveTvChannel && includeTypes.Contains("LiveTvChannel", StringComparer.OrdinalIgnoreCase))
                return true;

            if (item is LiveTvProgram && includeTypes.Contains("LiveTvProgram", StringComparer.OrdinalIgnoreCase))
                return true;

            return false;
        }

        public void Dispose()
        {
            try
            {
                SearchPatches.Unpatch();
                _libraryManager.ItemAdded -= OnItemAdded;
                _libraryManager.ItemUpdated -= OnItemUpdated;
                _libraryManager.ItemRemoved -= OnItemRemoved;
                _meilisearchService?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Error disposing Meilisearch plugin", ex);
            }
        }
    }
}
