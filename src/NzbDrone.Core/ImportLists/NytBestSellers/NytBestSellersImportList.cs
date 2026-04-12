using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists.NytBestSellers
{
    public class NytBestSellersImportList : ImportListBase<NytBestSellersSettings>
    {
        private readonly IHttpClient _httpClient;

        public override string Name => "NYT Best Sellers";
        public override ImportListType ListType => ImportListType.Other;
        public override TimeSpan MinRefreshInterval => TimeSpan.FromHours(24);

        private static readonly Dictionary<string, string> Lists = new ()
        {
            ["hardcover-fiction"] = "https://www.nytimes.com/books/best-sellers/hardcover-fiction/",
            ["hardcover-nonfiction"] = "https://www.nytimes.com/books/best-sellers/hardcover-nonfiction/",
            ["combined-print-and-e-book-fiction"] = "https://www.nytimes.com/books/best-sellers/combined-print-and-e-book-fiction/",
        };

        public NytBestSellersImportList(IHttpClient httpClient,
                                        IImportListStatusService importListStatusService,
                                        IConfigService configService,
                                        IParsingService parsingService,
                                        Logger logger)
            : base(importListStatusService, configService, parsingService, logger)
        {
            _httpClient = httpClient;
        }

        public override IList<ImportListItemInfo> Fetch()
        {
            var items = new List<ImportListItemInfo>();

            var urlsToFetch = new List<string>();

            if (Settings.IncludeFiction)
            {
                urlsToFetch.Add(Lists["hardcover-fiction"]);
            }

            if (Settings.IncludeNonFiction)
            {
                urlsToFetch.Add(Lists["hardcover-nonfiction"]);
            }

            if (Settings.IncludeCombined)
            {
                urlsToFetch.Add(Lists["combined-print-and-e-book-fiction"]);
            }

            foreach (var url in urlsToFetch)
            {
                try
                {
                    var request = new HttpRequest(url);
                    request.Headers.Add("User-Agent", "Mozilla/5.0 (compatible; Readarr)");
                    var response = _httpClient.Get(request);

                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        _logger.Warn("NYT Best Sellers: HTTP {0} fetching {1}", response.StatusCode, url);
                        continue;
                    }

                    items.AddRange(ParsePage(response.Content, url));
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "NYT Best Sellers: Failed to fetch {0}", url);
                }
            }

            _logger.Debug("NYT Best Sellers: Found {0} total books", items.Count);
            return items;
        }

        private static IEnumerable<ImportListItemInfo> ParsePage(string html, string url)
        {
            var items = new List<ImportListItemInfo>();

            // Match <article itemtype="https://schema.org/Book"> blocks
            var articlePattern = new Regex(
                @"<article[^>]+itemtype=""https://schema\.org/Book""[^>]*>(.*?)</article>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);

            var titlePattern = new Regex(
                @"<h3[^>]+itemprop=""name""[^>]*>(.*?)</h3>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);

            var authorPattern = new Regex(
                @"<p[^>]+itemprop=""author""[^>]*>(.*?)</p>",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);

            var htmlTagPattern = new Regex(@"<[^>]+>");

            foreach (Match articleMatch in articlePattern.Matches(html))
            {
                var articleContent = articleMatch.Groups[1].Value;

                var titleMatch = titlePattern.Match(articleContent);
                var authorMatch = authorPattern.Match(articleContent);

                if (!titleMatch.Success || !authorMatch.Success)
                {
                    continue;
                }

                var title = WebUtility.HtmlDecode(htmlTagPattern.Replace(titleMatch.Groups[1].Value, string.Empty)).Trim();
                var authorRaw = WebUtility.HtmlDecode(htmlTagPattern.Replace(authorMatch.Groups[1].Value, string.Empty)).Trim();

                // Strip leading "by " and take first author before " and "
                var author = authorRaw;
                if (author.StartsWith("by ", StringComparison.OrdinalIgnoreCase))
                {
                    author = author.Substring(3).Trim();
                }

                var andIndex = author.IndexOf(" and ", StringComparison.OrdinalIgnoreCase);
                if (andIndex > 0)
                {
                    author = author.Substring(0, andIndex).Trim();
                }

                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(author))
                {
                    continue;
                }

                items.Add(new ImportListItemInfo
                {
                    Book = title,
                    Author = author,
                });
            }

            return items;
        }

        protected override void Test(List<ValidationFailure> failures)
        {
            try
            {
                var request = new HttpRequest(Lists["hardcover-fiction"]);
                request.Headers.Add("User-Agent", "Mozilla/5.0 (compatible; Readarr)");
                var response = _httpClient.Get(request);

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    failures.Add(new ValidationFailure(string.Empty, $"Unable to reach NYT Best Sellers (HTTP {(int)response.StatusCode})"));
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "NYT Best Sellers test failed");
                failures.Add(new ValidationFailure(string.Empty, "Unable to reach NYT Best Sellers. Check logs for details."));
            }
        }
    }
}
