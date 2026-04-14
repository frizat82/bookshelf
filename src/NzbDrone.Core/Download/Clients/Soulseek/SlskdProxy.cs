using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.Download.Clients.Soulseek
{
    public class SlskdTransfer
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("username")]
        public string Username { get; set; }

        [JsonProperty("filename")]
        public string Filename { get; set; }

        [JsonProperty("size")]
        public long Size { get; set; }

        [JsonProperty("state")]
        public string State { get; set; }

        [JsonProperty("percentComplete")]
        public double PercentComplete { get; set; }

        [JsonProperty("bytesTransferred")]
        public long BytesTransferred { get; set; }

        [JsonProperty("direction")]
        public string Direction { get; set; }

        [JsonProperty("remainingSeconds")]
        public double? RemainingSeconds { get; set; }
    }

    public class SlskdTransferResponse
    {
        [JsonProperty("username")]
        public string Username { get; set; }

        [JsonProperty("directories")]
        public List<SlskdTransferDirectory> Directories { get; set; } = new ();
    }

    public class SlskdTransferDirectory
    {
        [JsonProperty("files")]
        public List<SlskdTransfer> Files { get; set; } = new ();
    }

    public class SlskdEnqueueRequest
    {
        [JsonProperty("filename")]
        public string Filename { get; set; }

        [JsonProperty("size")]
        public long Size { get; set; }
    }

    public class SlskdOptions
    {
        [JsonProperty("directories")]
        public SlskdDirectories Directories { get; set; }
    }

    public class SlskdDirectories
    {
        [JsonProperty("downloads")]
        public string Downloads { get; set; }
    }

    public interface ISlskdProxy
    {
        void TestConnection(SlskdDownloadClientSettings settings);
        string EnqueueDownload(SlskdDownloadClientSettings settings, string username, string filename, long size);
        List<SlskdTransferResponse> GetTransfers(SlskdDownloadClientSettings settings);
        void RemoveTransfer(SlskdDownloadClientSettings settings, string username, string transferId, bool deleteData);
        string GetDownloadDirectory(SlskdDownloadClientSettings settings);
    }

    public class SlskdProxy : ISlskdProxy
    {
        private readonly IHttpClient _httpClient;

        public SlskdProxy(IHttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public void TestConnection(SlskdDownloadClientSettings settings)
        {
            var req = BuildRequest(settings, "/api/v0/application");
            _httpClient.Get(req);
        }

        public string EnqueueDownload(SlskdDownloadClientSettings settings, string username, string filename, long size)
        {
            var body = new[] { new SlskdEnqueueRequest { Filename = filename, Size = size } };
            var encoded = Uri.EscapeDataString(username);
            var req = BuildRequest(settings, $"/api/v0/transfers/downloads/{encoded}");
            req.Method = System.Net.Http.HttpMethod.Post;
            req.Headers.ContentType = "application/json";
            req.SetContent(JsonConvert.SerializeObject(body));

            var response = _httpClient.Execute(req);
            var transfers = JsonConvert.DeserializeObject<List<SlskdTransfer>>(response.Content);
            return (transfers?.Count > 0) ? $"{username}/{transfers[0].Id}" : string.Empty;
        }

        public List<SlskdTransferResponse> GetTransfers(SlskdDownloadClientSettings settings)
        {
            var req = BuildRequest(settings, "/api/v0/transfers/downloads?includeRemoved=true");
            var response = _httpClient.Get(req);
            return JsonConvert.DeserializeObject<List<SlskdTransferResponse>>(response.Content)
                   ?? new List<SlskdTransferResponse>();
        }

        public void RemoveTransfer(SlskdDownloadClientSettings settings, string username, string transferId, bool deleteData)
        {
            try
            {
                var encoded = Uri.EscapeDataString(username);
                var suffix = deleteData ? "?deleteFile=true" : string.Empty;
                var req = BuildRequest(settings, $"/api/v0/transfers/downloads/{encoded}/{transferId}{suffix}");
                req.Method = System.Net.Http.HttpMethod.Delete;
                _httpClient.Execute(req);
            }
            catch
            {
                var req = BuildRequest(settings, "/api/v0/transfers/downloads/all/completed");
                req.Method = System.Net.Http.HttpMethod.Delete;
                _httpClient.Execute(req);
            }
        }

        public string GetDownloadDirectory(SlskdDownloadClientSettings settings)
        {
            var req = BuildRequest(settings, "/api/v0/options");
            var response = _httpClient.Get(req);
            var options = JsonConvert.DeserializeObject<SlskdOptions>(response.Content);
            return options?.Directories?.Downloads ?? string.Empty;
        }

        private static HttpRequest BuildRequest(SlskdDownloadClientSettings settings, string path)
        {
            return new HttpRequestBuilder($"{settings.BaseUrl.TrimEnd('/')}{path}")
                .SetHeader("X-API-Key", settings.ApiKey)
                .Accept(HttpAccept.Json)
                .Build();
        }
    }
}
