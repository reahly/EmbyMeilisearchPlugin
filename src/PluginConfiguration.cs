using MediaBrowser.Model.Plugins;

namespace EmbyMeilisearchPlugin
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public bool Enabled { get; set; } = true;
        public string MeilisearchUrl { get; set; } = "http://localhost:7700";
        public string MeilisearchApiKey { get; set; } = "";
        public string IndexName { get; set; } = "emby_media";
        public bool AutoSync { get; set; } = true;
        public string IncludeItemTypes { get; set; } = "Movie,Series,Episode,Audio,MusicAlbum,MusicArtist,MusicVideo,Video,BoxSet,Playlist,LiveTvChannel,LiveTvProgram";
        public string SearchableAttributes { get; set; } = "Name,OriginalTitle,SortName,Overview,Tagline,Genres,Studios,Tags,Album,Artists,SeriesName,ChannelNumber,ChannelName";
        public string FilterableAttributes { get; set; } = "ItemType,Genres,ProductionYear,CommunityRating,Studios,IsFolder,ChannelType";
        public int MaxSearchResults { get; set; } = 100;
        public int MinSearchTermLength { get; set; } = 1;
    }
}
