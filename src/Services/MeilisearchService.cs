using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EmbyMeilisearchPlugin.Models;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;

namespace EmbyMeilisearchPlugin.Services
{
    public class MeilisearchService : IDisposable
    {
        private static MeilisearchService _instance;
        public static MeilisearchService Instance => _instance;

        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;
        private readonly IJsonSerializer _jsonSerializer;
        private HttpClient _httpClient;
        private string _baseUrl;
        private string _indexName;
        private string _lastConfiguredUrl;
        private string _lastConfiguredKey;
        private string _lastConfiguredIndex;
        private bool _initialized;

        public bool IsInitialized => _initialized;

        public void ReloadConnection()
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null || !config.Enabled)
            {
                _initialized = false;
                return;
            }

            try
            {
                _baseUrl = config.MeilisearchUrl.TrimEnd('/');
                _indexName = config.IndexName;

                var newClient = new HttpClient();
                newClient.Timeout = TimeSpan.FromSeconds(30);
                if (!string.IsNullOrEmpty(config.MeilisearchApiKey))
                    newClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + config.MeilisearchApiKey);

                var oldClient = _httpClient;
                _httpClient = newClient;
                oldClient?.Dispose();

                _initialized = true;
                _lastConfiguredUrl = config.MeilisearchUrl;
                _lastConfiguredKey = config.MeilisearchApiKey;
                _lastConfiguredIndex = config.IndexName;
                _logger.Info("Meilisearch connection reloaded: " + _baseUrl);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Failed to reload Meilisearch connection", ex);
            }
        }

        private void CheckConfigChanged()
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null) return;

            if (config.MeilisearchUrl != _lastConfiguredUrl ||
                config.MeilisearchApiKey != _lastConfiguredKey ||
                config.IndexName != _lastConfiguredIndex)
            {
                ReloadConnection();
            }
        }

        public MeilisearchService(ILogger logger, ILibraryManager libraryManager, IJsonSerializer jsonSerializer)
        {
            _logger = logger;
            _libraryManager = libraryManager;
            _jsonSerializer = jsonSerializer;
            _instance = this;
        }

        public async Task InitializeAsync()
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null || !config.Enabled)
            {
                _logger.Info("Meilisearch plugin is disabled");
                return;
            }

            try
            {
                _baseUrl = config.MeilisearchUrl.TrimEnd('/');
                _indexName = config.IndexName;

                _httpClient = new HttpClient();
                _httpClient.Timeout = TimeSpan.FromSeconds(30);
                
                if (!string.IsNullOrEmpty(config.MeilisearchApiKey))
                {
                    _httpClient.DefaultRequestHeaders.Add("Authorization", "Bearer " + config.MeilisearchApiKey);
                }

                await CreateIndexIfNotExistsAsync();
                await ConfigureIndexSettingsAsync(config);

                _initialized = true;
                _lastConfiguredUrl = config.MeilisearchUrl;
                _lastConfiguredKey = config.MeilisearchApiKey;
                _lastConfiguredIndex = config.IndexName;
                _logger.Info("Meilisearch initialized successfully at " + _baseUrl);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Failed to initialize Meilisearch", ex);
                throw;
            }
        }

        private async Task CreateIndexIfNotExistsAsync()
        {
            try
            {
                var content = new StringContent(
                    "{\"uid\":\"" + _indexName + "\",\"primaryKey\":\"Id\"}",
                    Encoding.UTF8,
                    "application/json");

                var response = await _httpClient.PostAsync(_baseUrl + "/indexes", content);
                
                if (response.StatusCode != System.Net.HttpStatusCode.Created && 
                    response.StatusCode != System.Net.HttpStatusCode.Conflict &&
                    response.StatusCode != System.Net.HttpStatusCode.Accepted)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.Warn("Create index response: " + response.StatusCode + " - " + errorContent);
                }
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Failed to create index", ex);
            }
        }

        private async Task ConfigureIndexSettingsAsync(PluginConfiguration config)
        {
            try
            {
                var searchableAttrs = config.SearchableAttributes
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => "\"" + s.Trim() + "\"");
                
                var searchableJson = "[" + string.Join(",", searchableAttrs) + "]";
                var searchableContent = new StringContent(searchableJson, Encoding.UTF8, "application/json");
                await _httpClient.PutAsync(_baseUrl + "/indexes/" + _indexName + "/settings/searchable-attributes", searchableContent);

                var filterableAttrs = config.FilterableAttributes
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => "\"" + s.Trim() + "\"");
                
                var filterableJson = "[" + string.Join(",", filterableAttrs) + "]";
                var filterableContent = new StringContent(filterableJson, Encoding.UTF8, "application/json");
                await _httpClient.PutAsync(_baseUrl + "/indexes/" + _indexName + "/settings/filterable-attributes", filterableContent);

                var sortableJson = "[\"Name\",\"ProductionYear\",\"CommunityRating\",\"DateCreated\",\"PremiereDate\",\"ChannelNumber\",\"StartDate\"]";
                var sortableContent = new StringContent(sortableJson, Encoding.UTF8, "application/json");
                await _httpClient.PutAsync(_baseUrl + "/indexes/" + _indexName + "/settings/sortable-attributes", sortableContent);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Failed to configure index settings", ex);
            }
        }

        public long[] SearchForInternalIds(string searchTerm, string[] includeItemTypes, int limit)
        {
            CheckConfigChanged();
            if (!_initialized || _httpClient == null || string.IsNullOrWhiteSpace(searchTerm))
            {
                return null;
            }

            try
            {
                var config = Plugin.Instance?.Configuration;
                if (config == null || searchTerm.Length < config.MinSearchTermLength)
                {
                    return null;
                }

                var searchRequest = new StringBuilder();
                searchRequest.Append("{");
                searchRequest.Append("\"q\":\"" + EscapeJson(searchTerm) + "\"");
                searchRequest.Append(",\"limit\":" + Math.Min(limit, config.MaxSearchResults));
                searchRequest.Append(",\"attributesToRetrieve\":[\"InternalId\",\"Id\",\"ItemType\",\"Name\"]");

                if (includeItemTypes != null && includeItemTypes.Length > 0)
                {
                    var mappedTypes = includeItemTypes.Select(t => MapItemType(t.Trim())).ToArray();
                    var typeFilters = mappedTypes.Select(t => "ItemType = \"" + t + "\"");
                    searchRequest.Append(",\"filter\":\"" + EscapeJson(string.Join(" OR ", typeFilters)) + "\"");
                    _logger.Debug("Meilisearch type filter: " + string.Join(", ", mappedTypes));
                }

                searchRequest.Append("}");

                _logger.Debug("Meilisearch search request: " + searchRequest.ToString());

                var content = new StringContent(searchRequest.ToString(), Encoding.UTF8, "application/json");
                var response = _httpClient.PostAsync(_baseUrl + "/indexes/" + _indexName + "/search", content).Result;
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = response.Content.ReadAsStringAsync().Result;
                    _logger.Warn("Meilisearch search failed: " + response.StatusCode + " - " + errorContent);
                    return null;
                }

                var responseJson = response.Content.ReadAsStringAsync().Result;
                var result = _jsonSerializer.DeserializeFromString<MeilisearchSearchResponse>(responseJson);

                if (result?.Hits == null || result.Hits.Count == 0)
                {
                    _logger.Debug("Meilisearch search for '" + searchTerm + "' returned 0 results");
                    return new long[0];
                }

                var internalIds = result.Hits
                    .Where(h => h.InternalId > 0)
                    .Select(h => h.InternalId)
                    .Distinct()  // Remove any duplicate internal IDs from Meilisearch
                    .ToArray();

                _logger.Debug("Meilisearch search for '" + searchTerm + "' returned " + internalIds.Length + " results in " + result.ProcessingTimeMs + "ms");
                
                var typesFound = result.Hits.Select(h => h.ItemType).Distinct();
                _logger.Debug("Types in results: " + string.Join(", ", typesFound));

                return internalIds;
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Meilisearch search failed for: " + searchTerm, ex);
                return null;
            }
        }

        public SearchResultWithTypes SearchWithTypes(string searchTerm, string[] includeItemTypes, int limit)
        {
            CheckConfigChanged();
            if (!_initialized || _httpClient == null || string.IsNullOrWhiteSpace(searchTerm))
            {
                return null;
            }

            try
            {
                var config = Plugin.Instance?.Configuration;
                if (config == null || searchTerm.Length < config.MinSearchTermLength)
                {
                    return null;
                }

                var searchRequest = new StringBuilder();
                searchRequest.Append("{");
                searchRequest.Append("\"q\":\"" + EscapeJson(searchTerm) + "\"");
                searchRequest.Append(",\"limit\":" + Math.Min(limit, config.MaxSearchResults));
                searchRequest.Append(",\"attributesToRetrieve\":[\"InternalId\",\"Id\",\"ItemType\",\"Name\"]");

                if (includeItemTypes != null && includeItemTypes.Length > 0)
                {
                    var mappedTypes = includeItemTypes.Select(t => MapItemType(t.Trim())).ToArray();
                    var typeFilters = mappedTypes.Select(t => "ItemType = \"" + t + "\"");
                    searchRequest.Append(",\"filter\":\"" + EscapeJson(string.Join(" OR ", typeFilters)) + "\"");
                }

                searchRequest.Append("}");

                var content = new StringContent(searchRequest.ToString(), Encoding.UTF8, "application/json");
                var response = _httpClient.PostAsync(_baseUrl + "/indexes/" + _indexName + "/search", content).Result;

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var responseJson = response.Content.ReadAsStringAsync().Result;
                var result = _jsonSerializer.DeserializeFromString<MeilisearchSearchResponse>(responseJson);

                if (result?.Hits == null || result.Hits.Count == 0)
                {
                    return new SearchResultWithTypes { RegularIds = new long[0], LiveTvIds = new long[0], AllIdsOrdered = new long[0] };
                }

                var allIds = new List<long>();
                var regularIds = new List<long>();
                var liveTvIds = new List<long>();
                var seen = new HashSet<long>();

                foreach (var hit in result.Hits)
                {
                    if (hit.InternalId <= 0) continue;
                    if (seen.Contains(hit.InternalId)) continue;
                    seen.Add(hit.InternalId);

                    allIds.Add(hit.InternalId);

                    if (hit.ItemType == "LiveTvChannel" || hit.ItemType == "LiveTvProgram")
                    {
                        liveTvIds.Add(hit.InternalId);
                    }
                    else
                    {
                        regularIds.Add(hit.InternalId);
                    }
                }

                _logger.Debug("SearchWithTypes for '" + searchTerm + "': " + regularIds.Count + " regular, " + liveTvIds.Count + " LiveTV, " + allIds.Count + " total in " + result.ProcessingTimeMs + "ms");

                return new SearchResultWithTypes
                {
                    RegularIds = regularIds.ToArray(),
                    LiveTvIds = liveTvIds.ToArray(),
                    AllIdsOrdered = allIds.ToArray()
                };
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Meilisearch SearchWithTypes failed for: " + searchTerm, ex);
                return null;
            }
        }

        private string MapItemType(string typeName)
        {
            switch (typeName.ToLowerInvariant())
            {
                case "channel":
                case "tvchannnel":
                case "livetvchannel":
                    return "LiveTvChannel";
                case "program":
                case "tvprogram":
                case "livetvprogram":
                    return "LiveTvProgram";
                default:
                    return typeName;
            }
        }

        private T GetPropertyValue<T>(object obj, string propertyName, T defaultValue = default)
        {
            try
            {
                var prop = obj.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (prop != null)
                {
                    var value = prop.GetValue(obj);
                    if (value != null)
                    {
                        return (T)value;
                    }
                }
            }
            catch
            {
            }
            return defaultValue;
        }

        public MeilisearchMediaItem ConvertToDocument(BaseItem item)
        {
            var doc = new MeilisearchMediaItem
            {
                Id = item.Id.ToString("N"),
                InternalId = item.InternalId,
                Name = item.Name ?? string.Empty,
                OriginalTitle = item.OriginalTitle,
                SortName = item.SortName,
                ItemType = item.GetType().Name,
                Overview = item.Overview,
                ProductionYear = item.ProductionYear,
                PremiereDate = item.PremiereDate.HasValue ? ToUnixTimeSeconds(item.PremiereDate.Value) : (long?)null,
                CommunityRating = item.CommunityRating,
                OfficialRating = item.OfficialRating,
                Genres = item.Genres?.ToList() ?? new List<string>(),
                Studios = item.Studios?.ToList() ?? new List<string>(),
                Tags = item.Tags?.ToList() ?? new List<string>(),
                IsFolder = item.IsFolder,
                RunTimeTicks = item.RunTimeTicks,
                Path = item.Path,
                DateCreated = ToUnixTimeSeconds(item.DateCreated)
            };

            if (item.ProviderIds != null)
            {
                doc.ProviderIds = new Dictionary<string, string>(item.ProviderIds);
            }

            var episode = item as Episode;
            if (episode != null)
            {
                doc.SeriesName = episode.SeriesName;
                doc.SeasonNumber = episode.ParentIndexNumber;
                doc.EpisodeNumber = episode.IndexNumber;
                try
                {
                    var seriesId = episode.SeriesId;
                    if (seriesId != default)
                    {
                        doc.ParentId = seriesId.ToString("N");
                    }
                }
                catch { }
            }

            var movie = item as Movie;
            if (movie != null)
            {
                doc.Tagline = movie.Tagline;
            }

            var audio = item as Audio;
            if (audio != null)
            {
                doc.Album = audio.Album;
                doc.Artists = audio.Artists?.ToList() ?? new List<string>();
            }

            var liveTvChannel = item as LiveTvChannel;
            if (liveTvChannel != null)
            {
                doc.ChannelNumber = liveTvChannel.Number;
                doc.ChannelName = liveTvChannel.Name;
                
                doc.IsHD = GetPropertyValue<bool?>(liveTvChannel, "IsHD", null);
                
                if (!string.IsNullOrEmpty(liveTvChannel.Number))
                {
                    doc.SortName = liveTvChannel.Number.PadLeft(5, '0') + " " + liveTvChannel.Name;
                }
                
                _logger.Debug("Converting LiveTvChannel: " + liveTvChannel.Name + " (Number: " + liveTvChannel.Number + ", InternalId: " + liveTvChannel.InternalId + ")");
            }

            var liveTvProgram = item as LiveTvProgram;
            if (liveTvProgram != null)
            {
                doc.ProgramName = liveTvProgram.Name;
                
                doc.IsMovie = liveTvProgram.IsMovie;
                doc.IsSeries = liveTvProgram.IsSeries;
                doc.IsNews = liveTvProgram.IsNews;
                doc.IsSports = liveTvProgram.IsSports;
                doc.IsKids = liveTvProgram.IsKids;
                doc.IsLive = liveTvProgram.IsLive;
                doc.IsPremiere = liveTvProgram.IsPremiere;
                
                if (liveTvProgram.StartDate != default)
                {
                    doc.StartDate = ToUnixTimeSeconds(liveTvProgram.StartDate);
                }
                if (liveTvProgram.EndDate.HasValue)
                {
                    doc.EndDate = ToUnixTimeSeconds(liveTvProgram.EndDate.Value);
                }

                doc.EpisodeTitle = GetPropertyValue<string>(liveTvProgram, "EpisodeTitle", null);
                
                try
                {
                    var channelIdProp = liveTvProgram.GetType().GetProperty("ChannelId", BindingFlags.Public | BindingFlags.Instance);
                    if (channelIdProp != null)
                    {
                        var channelIdValue = channelIdProp.GetValue(liveTvProgram);
                        if (channelIdValue != null)
                        {
                            if (channelIdValue is Guid channelGuid && channelGuid != default)
                            {
                                doc.ParentId = channelGuid.ToString("N");
                            }
                            else if (channelIdValue is string channelStr && !string.IsNullOrEmpty(channelStr))
                            {
                                doc.ParentId = channelStr;
                            }
                        }
                    }
                }
                catch { }
            }

            return doc;
        }

        private static long ToUnixTimeSeconds(DateTime dateTime)
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return (long)(dateTime.ToUniversalTime() - epoch).TotalSeconds;
        }

        private static long ToUnixTimeSeconds(DateTimeOffset dateTime)
        {
            var epoch = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
            return (long)(dateTime - epoch).TotalSeconds;
        }

        public async Task IndexItemsAsync(IEnumerable<BaseItem> items, CancellationToken cancellationToken = default)
        {
            if (!_initialized || _httpClient == null) return;

            var documents = items.Select(ConvertToDocument).ToList();
            
            const int batchSize = 1000;
            for (int i = 0; i < documents.Count; i += batchSize)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var batch = documents.Skip(i).Take(batchSize).ToList();
                try
                {
                    var json = _jsonSerializer.SerializeToString(batch);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var response = await _httpClient.PostAsync(_baseUrl + "/indexes/" + _indexName + "/documents", content, cancellationToken);
                    
                    if (!response.IsSuccessStatusCode)
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        _logger.Warn("Failed to index batch: " + error);
                    }
                    else
                    {
                        _logger.Debug("Indexed batch " + ((i / batchSize) + 1) + " (" + batch.Count + " items)");
                    }
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Failed to index items batch", ex);
                }
            }
        }

        public async Task RemoveItemAsync(Guid itemId, CancellationToken cancellationToken = default)
        {
            if (!_initialized || _httpClient == null) return;

            try
            {
                await _httpClient.DeleteAsync(_baseUrl + "/indexes/" + _indexName + "/documents/" + itemId.ToString("N"), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Failed to remove item: " + itemId, ex);
            }
        }

        public async Task RebuildIndexAsync(CancellationToken cancellationToken = default)
        {
            if (!_initialized || _httpClient == null) return;

            var config = Plugin.Instance?.Configuration;
            if (config == null) return;

            try
            {
                _logger.Info("Starting full index rebuild...");

                await _httpClient.DeleteAsync(_baseUrl + "/indexes/" + _indexName + "/documents", cancellationToken);

                var itemTypes = config.IncludeItemTypes
                    .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .ToArray();

                _logger.Info("Will index these item types: " + string.Join(", ", itemTypes));

                var regularTypes = itemTypes.Where(t => 
                    !t.Equals("LiveTvChannel", StringComparison.OrdinalIgnoreCase) && 
                    !t.Equals("LiveTvProgram", StringComparison.OrdinalIgnoreCase)).ToArray();

                if (regularTypes.Length > 0)
                {
                    var query = new InternalItemsQuery
                    {
                        IncludeItemTypes = regularTypes,
                        Recursive = true,
                        IsVirtualItem = false
                    };

                    var items = _libraryManager.GetItemList(query);
                    var itemsList = items.ToList();
                    
                    _logger.Info("Found " + itemsList.Count + " regular library items to index");

                    await IndexItemsAsync(itemsList, cancellationToken);
                }

                await IndexAllLiveTvItemsAsync(cancellationToken);

                _logger.Info("Index rebuild completed");
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Failed to rebuild index", ex);
                throw;
            }
        }

        public async Task IndexAllLiveTvItemsAsync(CancellationToken cancellationToken = default)
        {
            if (!_initialized || _httpClient == null) return;

            var config = Plugin.Instance?.Configuration;
            if (config == null) return;

            var itemTypes = config.IncludeItemTypes
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .ToArray();

            var indexChannels = itemTypes.Contains("LiveTvChannel", StringComparer.OrdinalIgnoreCase);
            var indexPrograms = itemTypes.Contains("LiveTvProgram", StringComparer.OrdinalIgnoreCase);

            if (!indexChannels && !indexPrograms)
            {
                _logger.Warn("Live TV types (LiveTvChannel, LiveTvProgram) not in IncludeItemTypes config!");
                _logger.Warn("Current IncludeItemTypes: " + config.IncludeItemTypes);
                _logger.Warn("Please add 'LiveTvChannel,LiveTvProgram' to the IncludeItemTypes in plugin settings, or delete the plugin config file to reset to defaults.");
                _logger.Info("Will attempt to index Live TV anyway...");
                
                indexChannels = true;
                indexPrograms = true;
            }

            _logger.Info("Indexing Live TV items...");

            var allLiveTvItems = new List<BaseItem>();

            if (indexChannels)
            {
                try
                {
                    var channelQuery = new InternalItemsQuery
                    {
                        IncludeItemTypes = new[] { "LiveTvChannel" },
                        Recursive = true
                    };

                    var channels = _libraryManager.GetItemList(channelQuery);
                    _logger.Info("Approach 1 (LiveTvChannel query): Found " + channels.Length + " channels");
                    allLiveTvItems.AddRange(channels);
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Error querying LiveTvChannel by type", ex);
                }
            }

            if (indexPrograms)
            {
                try
                {
                    var programQuery = new InternalItemsQuery
                    {
                        IncludeItemTypes = new[] { "LiveTvProgram" },
                        Recursive = true
                    };

                    var programs = _libraryManager.GetItemList(programQuery);
                    _logger.Info("Approach 1 (LiveTvProgram query): Found " + programs.Length + " programs");
                    allLiveTvItems.AddRange(programs);
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Error querying LiveTvProgram by type", ex);
                }
            }

            if (allLiveTvItems.Count == 0)
            {
                _logger.Info("Approach 1 found no Live TV items, trying Approach 2 (full scan)...");
                
                try
                {
                    var allQuery = new InternalItemsQuery
                    {
                        Recursive = true
                    };

                    var allItems = _libraryManager.GetItemList(allQuery);
                    _logger.Info("Full scan found " + allItems.Length + " total items");

                    var typeCounts = allItems
                        .GroupBy(i => i.GetType().Name)
                        .OrderByDescending(g => g.Count())
                        .Take(20);
                    
                    _logger.Info("Top item types in library:");
                    foreach (var tc in typeCounts)
                    {
                        _logger.Info("  " + tc.Key + ": " + tc.Count());
                    }

                    if (indexChannels)
                    {
                        var channels = allItems.Where(i => i is LiveTvChannel).ToList();
                        _logger.Info("Approach 2: Found " + channels.Count + " LiveTvChannel items by type check");
                        allLiveTvItems.AddRange(channels);
                    }

                    if (indexPrograms)
                    {
                        var programs = allItems.Where(i => i is LiveTvProgram).ToList();
                        _logger.Info("Approach 2: Found " + programs.Count + " LiveTvProgram items by type check");
                        allLiveTvItems.AddRange(programs);
                    }
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("Error in full scan approach", ex);
                }
            }

            if (allLiveTvItems.Count == 0)
            {
                _logger.Warn("No Live TV items found via standard queries. Live TV may not be configured or uses different item types.");
            }

            var uniqueItems = allLiveTvItems
                .GroupBy(i => i.Id)
                .Select(g => g.First())
                .ToList();

            _logger.Info("Total unique Live TV items to index: " + uniqueItems.Count);

            if (uniqueItems.Count > 0)
            {
                foreach (var item in uniqueItems.Take(10))
                {
                    _logger.Info("  Sample item: " + item.Name + " (Type: " + item.GetType().Name + ", InternalId: " + item.InternalId + ")");
                }

                await IndexItemsAsync(uniqueItems, cancellationToken);
                _logger.Info("Live TV indexing completed");
            }
            else
            {
                _logger.Warn("No Live TV items found to index. Make sure Live TV is set up in Emby.");
            }
        }

        public async Task<string> GetIndexStatsAsync()
        {
            if (!_initialized || _httpClient == null) return "Not initialized";

            try
            {
                var response = await _httpClient.GetAsync(_baseUrl + "/indexes/" + _indexName + "/stats");
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
                return "Error: " + response.StatusCode;
            }
            catch (Exception ex)
            {
                return "Error: " + ex.Message;
            }
        }

        public async Task<string> DebugSearchAsync(string query, string typeFilter = null)
        {
            if (!_initialized || _httpClient == null) return "Not initialized";

            try
            {
                var searchRequest = new StringBuilder();
                searchRequest.Append("{");
                searchRequest.Append("\"q\":\"" + EscapeJson(query) + "\"");
                searchRequest.Append(",\"limit\":20");
                
                if (!string.IsNullOrEmpty(typeFilter))
                {
                    searchRequest.Append(",\"filter\":\"ItemType = \\\"" + typeFilter + "\\\"\"");
                }
                
                searchRequest.Append("}");

                var content = new StringContent(searchRequest.ToString(), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(_baseUrl + "/indexes/" + _indexName + "/search", content);
                
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                return "Error: " + ex.Message;
            }
        }

        private string EscapeJson(string str)
        {
            if (string.IsNullOrEmpty(str)) return str;
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
            _httpClient = null;
            _initialized = false;
        }
    }

    public class MeilisearchSearchResponse
    {
        public List<MeilisearchHit> Hits { get; set; }
        public int EstimatedTotalHits { get; set; }
        public int ProcessingTimeMs { get; set; }
        public string Query { get; set; }
    }

    public class MeilisearchHit
    {
        public string Id { get; set; }
        public long InternalId { get; set; }
        public string ItemType { get; set; }
        public string Name { get; set; }
    }

    public class SearchResultWithTypes
    {
        public long[] RegularIds { get; set; }
        public long[] LiveTvIds { get; set; }
        public long[] AllIdsOrdered { get; set; }
    }
}