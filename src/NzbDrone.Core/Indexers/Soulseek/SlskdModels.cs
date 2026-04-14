using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace NzbDrone.Core.Indexers.Soulseek
{
    public class SlskdSearchRequest
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("searchText")]
        public string SearchText { get; set; }

        [JsonProperty("searchTimeout")]
        public int SearchTimeout { get; set; }

        [JsonProperty("fileLimit")]
        public int FileLimit { get; set; }

        [JsonProperty("filterResponses")]
        public bool FilterResponses { get; set; } = true;

        [JsonProperty("maximumPeerQueueLength")]
        public int MaximumPeerQueueLength { get; set; } = 100;

        [JsonProperty("minimumPeerUploadSpeed")]
        public int MinimumPeerUploadSpeed { get; set; } = 0;

        [JsonProperty("minimumResponseFileCount")]
        public int MinimumResponseFileCount { get; set; } = 1;

        [JsonProperty("responseLimit")]
        public int ResponseLimit { get; set; }
    }

    public class SlskdSearchResponse
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("searchText")]
        public string SearchText { get; set; }

        [JsonProperty("isComplete")]
        public bool IsComplete { get; set; }

        [JsonProperty("state")]
        public string State { get; set; }

        [JsonProperty("fileCount")]
        public int FileCount { get; set; }

        [JsonProperty("responseCount")]
        public int ResponseCount { get; set; }

        [JsonProperty("elapsedMilliseconds")]
        public int ElapsedMilliseconds { get; set; }

        [JsonProperty("searchTimeout")]
        public int SearchTimeout { get; set; }

        [JsonProperty("responses")]
        public List<SlskdPeerResponse> Responses { get; set; } = new ();
    }

    public class SlskdPeerResponse
    {
        [JsonProperty("username")]
        public string Username { get; set; }

        [JsonProperty("hasFreeUploadSlot")]
        public bool HasFreeUploadSlot { get; set; }

        [JsonProperty("uploadSpeed")]
        public long UploadSpeed { get; set; }

        [JsonProperty("queueLength")]
        public int QueueLength { get; set; }

        [JsonProperty("fileCount")]
        public int FileCount { get; set; }

        [JsonProperty("files")]
        public List<SlskdFile> Files { get; set; } = new ();
    }

    public class SlskdFile
    {
        [JsonProperty("filename")]
        public string Filename { get; set; }

        [JsonProperty("size")]
        public long Size { get; set; }

        [JsonProperty("extension")]
        public string Extension { get; set; }
    }

    public class SlskdDownloadData
    {
        [JsonProperty("u")]
        public string Username { get; set; }

        [JsonProperty("f")]
        public string Filename { get; set; }

        [JsonProperty("s")]
        public long Size { get; set; }

        public string ToDownloadUrl()
        {
            var json = JsonConvert.SerializeObject(this);
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
        }

        public static SlskdDownloadData FromDownloadUrl(string url)
        {
            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(url));
            return JsonConvert.DeserializeObject<SlskdDownloadData>(json);
        }
    }
}
