using System.Collections.Generic;

namespace EmbyMeilisearchPlugin.Models
{
    public class MeilisearchMediaItem
    {
        public string Id { get; set; }
        public long InternalId { get; set; }
        public string Name { get; set; }
        public string OriginalTitle { get; set; }
        public string SortName { get; set; }
        public string ItemType { get; set; }
        public string Overview { get; set; }
        public string Tagline { get; set; }
        public int? ProductionYear { get; set; }
        public long? PremiereDate { get; set; }
        public float? CommunityRating { get; set; }
        public string OfficialRating { get; set; }
        public List<string> Genres { get; set; }
        public List<string> Studios { get; set; }
        public List<string> Tags { get; set; }
        public bool IsFolder { get; set; }
        public long? RunTimeTicks { get; set; }
        public string Path { get; set; }
        public long DateCreated { get; set; }
        public Dictionary<string, string> ProviderIds { get; set; }

        public string SeriesName { get; set; }
        public string ParentId { get; set; }
        public int? SeasonNumber { get; set; }
        public int? EpisodeNumber { get; set; }

        public string Album { get; set; }
        public List<string> Artists { get; set; }

        public string ChannelNumber { get; set; }
        public string ChannelName { get; set; }
        public bool? IsHD { get; set; }

        public string ProgramName { get; set; }
        public string EpisodeTitle { get; set; }
        public long? StartDate { get; set; }
        public long? EndDate { get; set; }
        public bool? IsMovie { get; set; }
        public bool? IsSeries { get; set; }
        public bool? IsNews { get; set; }
        public bool? IsSports { get; set; }
        public bool? IsKids { get; set; }
        public bool? IsLive { get; set; }
        public bool? IsPremiere { get; set; }
    }
}
