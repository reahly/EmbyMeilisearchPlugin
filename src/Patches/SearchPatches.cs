using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Querying;
using EmbyMeilisearchPlugin.Services;

namespace EmbyMeilisearchPlugin.Patches
{
    public static class SearchPatches
    {
        private const string HarmonyId = "com.meilisearch.emby.search";
        private const int DefaultLimit = 50;

        private static Harmony _harmony;
        private static ILogger _logger;
        private static ILibraryManager _libraryManager;

        private static readonly Dictionary<string, CachedSearch> _cache = new Dictionary<string, CachedSearch>();
        private static readonly object _cacheLock = new object();
        private static readonly HashSet<string> _searchesInProgress = new HashSet<string>();
        private static readonly TimeSpan _cacheExpiry = TimeSpan.FromSeconds(30);

        private class CachedSearch
        {
            public long[] AllIdsOrdered { get; set; }
            public long[] RegularIds { get; set; }
            public long[] LiveTvIds { get; set; }
            public DateTime CachedAt { get; set; }
        }

        public static void Initialize(ILogger logger, ILibraryManager libraryManager)
        {
            _logger = logger;
            _libraryManager = libraryManager;

            try
            {
                _harmony = new Harmony(HarmonyId);

                var prefix = typeof(SearchPatches).GetMethod(nameof(GetItemsResultPrefix),
                    BindingFlags.NonPublic | BindingFlags.Static);
                var postfix = typeof(SearchPatches).GetMethod(nameof(GetItemsResultPostfix),
                    BindingFlags.NonPublic | BindingFlags.Static);

                if (prefix == null || postfix == null)
                {
                    _logger?.Error("SearchPatches: could not find prefix/postfix methods");
                    return;
                }

                var type = libraryManager.GetType();
                var targetMethodNames = new[] { "GetItemsResult", "QueryItems" };
                int patchCount = 0;

                foreach (var methodName in targetMethodNames)
                {
                    var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .Where(m => m.Name == methodName &&
                                    m.GetParameters().Length > 0 &&
                                    m.GetParameters()[0].ParameterType == typeof(InternalItemsQuery));

                    foreach (var method in methods)
                    {
                        try
                        {
                            _harmony.Patch(method, new HarmonyMethod(prefix), new HarmonyMethod(postfix));
                            patchCount++;
                        }
                        catch (Exception ex)
                        {
                            _logger?.ErrorException("SearchPatches: failed to patch " + method.Name, ex);
                        }
                    }
                }

                _logger?.Info("SearchPatches: initialized, " + patchCount + " methods patched on " + type.Name);
            }
            catch (Exception ex)
            {
                _logger?.ErrorException("SearchPatches: initialization failed", ex);
            }
        }

        public static void Shutdown() => Unpatch();

        public static void Unpatch()
        {
            try
            {
                _harmony?.UnpatchAll(HarmonyId);
                lock (_cacheLock)
                {
                    _cache.Clear();
                    _searchesInProgress.Clear();
                }
                _logger?.Info("SearchPatches: removed");
            }
            catch (Exception ex)
            {
                _logger?.ErrorException("SearchPatches: error during unpatch", ex);
            }
        }

        private static void GetItemsResultPostfix(
            InternalItemsQuery query,
            ref QueryResult<BaseItem> __result)
        {
        }

        private static bool GetItemsResultPrefix(
            InternalItemsQuery query,
            ref QueryResult<BaseItem> __result,
            object __instance,
            MethodBase __originalMethod)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(query.SearchTerm))
                    return true;

                var service = MeilisearchService.Instance;
                if (service == null || !service.IsInitialized)
                    return true;

                var config = Plugin.Instance?.Configuration;
                if (config == null || !config.Enabled)
                    return true;

                var searchTerm = query.SearchTerm;
                var cacheKey = "ms:" + searchTerm.ToLowerInvariant();
                var requestedTypes = query.IncludeItemTypes ?? Array.Empty<string>();

                var cached = GetOrCreateCachedSearch(cacheKey, searchTerm, service, config);
                if (cached == null || cached.AllIdsOrdered.Length == 0)
                    return true;

                if (requestedTypes.Any(t =>
                    t.Equals("TvChannel", StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("LiveTvChannel", StringComparison.OrdinalIgnoreCase)))
                {
                    return ModifyAndPassToNative(query, cached.LiveTvIds);
                }

                if (requestedTypes.Length > 0)
                {
                    return ModifyAndPassToNative(query, cached.RegularIds);
                }

                if (query.GroupByPresentationUniqueKey.HasValue &&
                    query.GroupByPresentationUniqueKey.Value == false)
                {
                    return BuildTabCountResult(query, ref __result, cached);
                }

                return BuildMainResult(query, ref __result, cached);
            }
            catch (Exception ex)
            {
                _logger?.ErrorException("SearchPatches prefix error", ex);
                return true;
            }
        }

        private static bool ModifyAndPassToNative(InternalItemsQuery query, long[] itemIds)
        {
            if (itemIds.Length == 0)
                return true;

            query.SearchTerm = null;
            query.ItemIds = itemIds;
            query.Recursive = true;
            query.Parent = null;
            query.TopParentIds = Array.Empty<long>();
            query.AncestorIds = Array.Empty<long>();
            query.EnforceContentRestriction = false;
            query.EnforceShareLevel = false;

            if (query.ExcludeItemTypes != null && query.ExcludeItemTypes.Length > 0)
                query.ExcludeItemTypes = Array.Empty<string>();

            return true;
        }

        private static bool BuildMainResult(
            InternalItemsQuery query,
            ref QueryResult<BaseItem> __result,
            CachedSearch cached)
        {
            var lm = _libraryManager;
            if (lm == null)
                return true;

            int limit = query.Limit ?? DefaultLimit;
            var items = new List<BaseItem>(limit);

            foreach (var id in cached.AllIdsOrdered)
            {
                if (id <= 0)
                    continue;
                var item = lm.GetItemById(id);
                if (item == null)
                    continue;
                if (item.GetType().Name == "Episode")
                    continue;

                items.Add(item);
                if (items.Count >= limit)
                    break;
            }

            __result = new QueryResult<BaseItem>
            {
                Items = items.ToArray(),
                TotalRecordCount = items.Count
            };

            return false;
        }

        private static bool BuildTabCountResult(
            InternalItemsQuery query,
            ref QueryResult<BaseItem> __result,
            CachedSearch cached)
        {
            var lm = _libraryManager;
            if (lm == null)
                return true;

            var items = new List<BaseItem>();
            var seenTypes = new HashSet<string>();

            int maxScan = Math.Min(cached.AllIdsOrdered.Length, 50);
            for (int i = 0; i < maxScan; i++)
            {
                var id = cached.AllIdsOrdered[i];
                if (id <= 0)
                    continue;
                var item = lm.GetItemById(id);
                if (item != null)
                {
                    var typeName = item.GetType().Name;
                    if (seenTypes.Add(typeName))
                        items.Add(item);
                }
            }

            __result = new QueryResult<BaseItem>
            {
                Items = items.ToArray(),
                TotalRecordCount = items.Count
            };

            return false;
        }

        private static CachedSearch GetOrCreateCachedSearch(
            string cacheKey,
            string searchTerm,
            MeilisearchService service,
            EmbyMeilisearchPlugin.PluginConfiguration config)
        {
            lock (_cacheLock)
            {
                if (_cache.TryGetValue(cacheKey, out var cached) &&
                    DateTime.UtcNow - cached.CachedAt < _cacheExpiry)
                {
                    return cached;
                }

                if (cached != null)
                    _cache.Remove(cacheKey);

                if (!_searchesInProgress.Contains(cacheKey))
                {
                    _searchesInProgress.Add(cacheKey);
                    return ExecuteSearch(cacheKey, searchTerm, service, config);
                }
            }

            for (int i = 0; i < 50; i++)
            {
                System.Threading.Thread.Sleep(10);
                lock (_cacheLock)
                {
                    if (_cache.TryGetValue(cacheKey, out var cached))
                        return cached;
                    if (!_searchesInProgress.Contains(cacheKey))
                    {
                        _searchesInProgress.Add(cacheKey);
                        return ExecuteSearch(cacheKey, searchTerm, service, config);
                    }
                }
            }

            _logger?.Warn("SearchPatches: timeout waiting for Meilisearch result for '" + searchTerm + "'");
            return null;
        }

        private static CachedSearch ExecuteSearch(
            string cacheKey,
            string searchTerm,
            MeilisearchService service,
            EmbyMeilisearchPlugin.PluginConfiguration config)
        {
            try
            {
                var fetchLimit = Math.Min(Math.Max(config.MaxSearchResults, 150), 500);
                var result = service.SearchWithTypes(searchTerm, null, fetchLimit);
                if (result == null)
                    return null;

                var cached = new CachedSearch
                {
                    AllIdsOrdered = result.AllIdsOrdered,
                    RegularIds = result.RegularIds,
                    LiveTvIds = result.LiveTvIds,
                    CachedAt = DateTime.UtcNow
                };

                lock (_cacheLock)
                {
                    _cache[cacheKey] = cached;

                    var expired = _cache
                        .Where(kvp => DateTime.UtcNow - kvp.Value.CachedAt > _cacheExpiry)
                        .Select(kvp => kvp.Key).ToList();
                    foreach (var key in expired)
                        _cache.Remove(key);
                }

                return cached;
            }
            finally
            {
                lock (_cacheLock)
                {
                    _searchesInProgress.Remove(cacheKey);
                }
            }
        }
    }
}
