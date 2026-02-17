using System;
using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace EmbyMeilisearchPlugin
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public static Plugin Instance { get; private set; }

        public override string Name => "Meilisearch Search";
        public override string Description => "Replaces Emby's built-in search with Meilisearch";
        public override Guid Id => new Guid("8e4c7b5a-1234-5678-9abc-def012345678");

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = "MeilisearchConfigPage",
                    EmbeddedResourcePath = "configPage.html",
                    IsMainConfigPage = true
                },
                new PluginPageInfo
                {
                    Name = "meilisearchjs",
                    EmbeddedResourcePath = "meilisearchjs.js"
                }
            };
        }
    }
}
