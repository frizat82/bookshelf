using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NLog;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.ImportLists.Hardcover
{
    public class HardcoverImportRequestGenerator : IImportListRequestGenerator
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public HardcoverImportSettings Settings { get; set; }

        public int MaxPages { get; set; } = 1;
        public int PageSize { get; set; } = 200;

        public ImportListPageableRequestChain GetListItems()
        {
            var pageableRequests = new ImportListPageableRequestChain();

            pageableRequests.Add(GetPagedRequests());

            return pageableRequests;
        }

        private IEnumerable<ImportListRequest> GetPagedRequests()
        {
            var apiKey = NormalizeApiKey(Settings.ApiKey);

            Logger.Info("Hardcover: Fetching books for lists '{0}'", Settings.ListIds);

            var statusIds = Settings.ListIds
                .Where(id => id.StartsWith("status:", System.StringComparison.OrdinalIgnoreCase))
                .Select(id => int.Parse(id.Substring("status:".Length)))
                .ToList();

            var listSlugs = Settings.ListIds
                .Where(id => !id.StartsWith("status:", System.StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (statusIds.Any())
            {
                var body = JsonSerializer.Serialize(new
                {
                    query = @"
                        query UserBooks($statuses: [Int!]!) { me { user_books(where: { status_id: { _in: $statuses } }) { book { id title contributions { author { id name } } } } } }
                    ",
                    variables = new { statuses = statusIds }
                });

                yield return new ImportListRequest(BuildRequest(apiKey, body));
            }

            if (listSlugs.Any())
            {
                var body = JsonSerializer.Serialize(new
                {
                    query = @"
                        query ListBooks($slugs: [String!]!) { me { lists(where: { slug: { _in: $slugs } } ) { slug name list_books { book { id title contributions { author { id name } } } } } } }
                    ",
                    variables = new { slugs = listSlugs }
                });

                yield return new ImportListRequest(BuildRequest(apiKey, body));
            }
        }

        private HttpRequest BuildRequest(string apiKey, string body)
        {
            var request = new HttpRequestBuilder($"{Settings.BaseUrl.TrimEnd('/')}/v1/graphql")
                .Post()
                .Accept(HttpAccept.Json)
                .SetHeader("Authorization", $"Bearer {apiKey}")
                .SetHeader("X-Api-Key", apiKey)
                .SetHeader("User-Agent", "Readarr (Hardcover Import)")
                .SetHeader("Content-Type", "application/json")
                .KeepAlive()
                .Build();

            request.SetContent(body);
            return request;
        }

        private string NormalizeApiKey(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return string.Empty;
            }

            var trimmed = apiKey.Trim();
            const string bearerPrefix = "bearer ";

            if (trimmed.StartsWith(bearerPrefix, System.StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.Substring(bearerPrefix.Length).Trim();
            }

            return trimmed;
        }
    }
}
