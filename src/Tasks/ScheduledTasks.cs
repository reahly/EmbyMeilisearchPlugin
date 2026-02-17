using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EmbyMeilisearchPlugin.Services;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;

namespace EmbyMeilisearchPlugin.Tasks
{
    public class RebuildIndexTask : IScheduledTask
    {
        private readonly ILogger _logger;

        public string Name => "Meilisearch: Rebuild Index";
        public string Key => "MeilisearchRebuildIndex";
        public string Description => "Drops and rebuilds the entire Meilisearch index (library + Live TV)";
        public string Category => "Meilisearch";

        public RebuildIndexTask(ILogManager logManager)
        {
            _logger = logManager.GetLogger("MeilisearchRebuildTask");
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            _logger.Info("Starting Meilisearch index rebuild...");
            progress.Report(0);

            var service = MeilisearchService.Instance;
            if (service == null || !service.IsInitialized)
            {
                _logger.Warn("Meilisearch service is not initialized");
                return;
            }

            try
            {
                progress.Report(10);
                await service.RebuildIndexAsync(cancellationToken);
                progress.Report(100);
                _logger.Info("Meilisearch index rebuild completed");
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Meilisearch index rebuild failed", ex);
                throw;
            }
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfo.TriggerDaily,
                    TimeOfDayTicks = TimeSpan.FromHours(3).Ticks
                }
            };
        }
    }

    public class IndexLiveTvTask : IScheduledTask
    {
        private readonly ILogger _logger;

        public string Name => "Meilisearch: Refresh Live TV";
        public string Key => "MeilisearchIndexLiveTv";
        public string Description => "Re-indexes only Live TV channels and programs (faster than full rebuild)";
        public string Category => "Meilisearch";

        public IndexLiveTvTask(ILogManager logManager)
        {
            _logger = logManager.GetLogger("MeilisearchLiveTvTask");
        }

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            _logger.Info("Starting Meilisearch Live TV indexing...");
            progress.Report(0);

            var service = MeilisearchService.Instance;
            if (service == null || !service.IsInitialized)
            {
                _logger.Warn("Meilisearch service is not initialized");
                return;
            }

            try
            {
                progress.Report(10);
                await service.IndexAllLiveTvItemsAsync(cancellationToken);
                progress.Report(100);
                _logger.Info("Meilisearch Live TV indexing completed");
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Meilisearch Live TV indexing failed", ex);
                throw;
            }
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfo.TriggerInterval,
                    IntervalTicks = TimeSpan.FromHours(6).Ticks
                }
            };
        }
    }
}
