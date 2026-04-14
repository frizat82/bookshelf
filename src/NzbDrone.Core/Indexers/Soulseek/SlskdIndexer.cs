using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentValidation.Results;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Indexers.Soulseek
{
    public class SlskdIndexer : IndexerBase<SlskdIndexerSettings>
    {
        private readonly IHttpClient _httpClient;

        public override string Name => "Soulseek (slskd)";
        public override DownloadProtocol Protocol => DownloadProtocol.Soulseek;
        public override bool SupportsRss => false;
        public override bool SupportsSearch => true;

        public SlskdIndexer(IHttpClient httpClient,
                            IIndexerStatusService indexerStatusService,
                            IConfigService configService,
                            IParsingService parsingService,
                            Logger logger)
            : base(indexerStatusService, configService, parsingService, logger)
        {
            _httpClient = httpClient;
        }

        public override Task<IList<ReleaseInfo>> FetchRecent()
        {
            return Task.FromResult<IList<ReleaseInfo>>(new List<ReleaseInfo>());
        }

        public override async Task<IList<ReleaseInfo>> Fetch(BookSearchCriteria searchCriteria)
        {
            var searchText = $"{searchCriteria.Author.Name} {searchCriteria.BookTitle}";
            return await Search(searchText, searchCriteria.Author.Name, searchCriteria.BookTitle);
        }

        public override async Task<IList<ReleaseInfo>> Fetch(AuthorSearchCriteria searchCriteria)
        {
            return await Search(searchCriteria.Author.Name, searchCriteria.Author.Name, null);
        }

        public override HttpRequest GetDownloadRequest(string link)
        {
            throw new NotSupportedException("Soulseek downloads are handled by the slskd download client, not via HTTP.");
        }

        protected override async Task Test(List<ValidationFailure> failures)
        {
            try
            {
                var request = BuildRequest("/api/v0/application");
                _httpClient.Get(request);
            }
            catch (Exception ex)
            {
                failures.Add(new ValidationFailure(string.Empty, $"Cannot connect to slskd: {ex.Message}"));
            }

            await Task.CompletedTask;
        }

        private async Task<IList<ReleaseInfo>> Search(string searchText, string author, string title)
        {
            var searchId = Guid.NewGuid().ToString();
            var timeoutMs = Settings.SearchTimeout * 1000;

            _logger.Debug("Soulseek: Starting search '{0}' (id={1}, timeout={2}s)", searchText, searchId, Settings.SearchTimeout);

            var searchRequest = new SlskdSearchRequest
            {
                Id = searchId,
                SearchText = searchText,
                SearchTimeout = timeoutMs,
                FileLimit = Settings.FileLimit,
                ResponseLimit = Settings.ResponseLimit
            };

            var postReq = BuildRequest("/api/v0/searches");
            postReq.Method = System.Net.Http.HttpMethod.Post;
            postReq.Headers.ContentType = "application/json";
            postReq.SetContent(JsonConvert.SerializeObject(searchRequest));
            _httpClient.Execute(postReq);

            await Task.Delay(2000);

            SlskdSearchResponse response = null;
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs + 20000);

            while (DateTime.UtcNow < deadline)
            {
                response = GetSearch(searchId);

                if (response.IsComplete)
                {
                    _logger.Debug("Soulseek: Search complete — {0} files, {1} peers", response.FileCount, response.ResponseCount);
                    break;
                }

                var p = Math.Min(1.0, response.ElapsedMilliseconds / (double)timeoutMs);
                var delaySec = Math.Clamp((16 * p * p) - (16 * p) + 5, 0.5, 5.0);
                await Task.Delay(TimeSpan.FromSeconds(delaySec));
            }

            try
            {
                var delReq = BuildRequest($"/api/v0/searches/{searchId}");
                delReq.Method = System.Net.Http.HttpMethod.Delete;
                _httpClient.Execute(delReq);
            }
            catch
            {
                // Non-fatal cleanup
            }

            if (response?.Responses == null || response.Responses.Count == 0)
            {
                _logger.Debug("Soulseek: No responses for '{0}'", searchText);
                return new List<ReleaseInfo>();
            }

            var releases = SlskdParser.ParseResults(response, author, title ?? string.Empty, Settings, _logger);
            return CleanupReleases(releases);
        }

        private SlskdSearchResponse GetSearch(string searchId)
        {
            var req = BuildRequest($"/api/v0/searches/{searchId}?includeResponses=true");
            var response = _httpClient.Get(req);
            return JsonConvert.DeserializeObject<SlskdSearchResponse>(response.Content);
        }

        private HttpRequest BuildRequest(string path)
        {
            return new HttpRequestBuilder($"{Settings.BaseUrl.TrimEnd('/')}{path}")
                .SetHeader("X-API-Key", Settings.ApiKey)
                .Accept(HttpAccept.Json)
                .Build();
        }
    }
}
