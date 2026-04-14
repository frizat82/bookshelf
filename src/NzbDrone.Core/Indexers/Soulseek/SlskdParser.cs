using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Indexers.Soulseek
{
    public static class SlskdParser
    {
        private static readonly string[] FormatPreference = { "epub", "mobi", "azw3", "pdf", "djvu" };
        private static readonly Regex NonAlpha = new Regex(@"[^\w\s]", RegexOptions.Compiled);

        public static List<ReleaseInfo> ParseResults(
            SlskdSearchResponse searchResponse,
            string author,
            string title,
            SlskdIndexerSettings settings,
            Logger logger)
        {
            var results = new List<(ReleaseInfo Release, int Score)>();
            var allowedFormats = new HashSet<string>(settings.AllowedFormats, StringComparer.OrdinalIgnoreCase);

            foreach (var peer in searchResponse.Responses)
            {
                if (peer.QueueLength > 100)
                {
                    continue;
                }

                foreach (var file in peer.Files)
                {
                    var ext = GetExtension(file.Filename);
                    if (!allowedFormats.Contains(ext))
                    {
                        continue;
                    }

                    var basename = Path.GetFileNameWithoutExtension(
                        file.Filename.Replace('\\', '/').Split('/').Last());

                    var authorScore = Math.Max(
                        PartialRatio(Normalize(author), Normalize(basename)),
                        TokenSortRatio(Normalize(author), Normalize(basename)));

                    if (authorScore < settings.AuthorMinScore)
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(title))
                    {
                        var titleScore = Math.Max(
                            PartialRatio(Normalize(title), Normalize(basename)),
                            TokenSortRatio(Normalize(title), Normalize(basename)));

                        if (titleScore < settings.TitleMinScore)
                        {
                            continue;
                        }
                    }

                    var formatScore = FormatScore(ext);
                    var speedBonus = peer.HasFreeUploadSlot ? 10 : 0;
                    var totalScore = (formatScore * 10) + speedBonus;

                    var downloadData = new SlskdDownloadData
                    {
                        Username = peer.Username,
                        Filename = file.Filename,
                        Size = file.Size
                    };

                    var releaseTitle = !string.IsNullOrWhiteSpace(title)
                        ? $"{author} - {title}.{ext}"
                        : $"{author} - {basename}.{ext}";

                    var release = new ReleaseInfo
                    {
                        Guid = $"slskd:{peer.Username}:{file.Filename}",
                        Title = releaseTitle,
                        Size = file.Size,
                        DownloadUrl = downloadData.ToDownloadUrl(),
                        PublishDate = DateTime.UtcNow,
                        DownloadProtocol = DownloadProtocol.Soulseek,
                        Author = author,
                        Book = title,
                        Container = ext.ToUpperInvariant(),
                    };

                    results.Add((release, totalScore));
                }
            }

            logger.Debug("Soulseek: {0} matches found from {1} peer responses", results.Count, searchResponse.Responses.Count);

            return results
                .OrderByDescending(r => r.Score)
                .Select(r => r.Release)
                .ToList();
        }

        private static int FormatScore(string ext)
        {
            var idx = Array.IndexOf(FormatPreference, ext.ToLowerInvariant());
            return idx >= 0 ? FormatPreference.Length - idx : 0;
        }

        private static string GetExtension(string filename)
        {
            var clean = filename.Replace('\\', '/');
            return Path.GetExtension(clean).TrimStart('.').ToLowerInvariant();
        }

        private static string Normalize(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                return string.Empty;
            }

            s = NonAlpha.Replace(s.ToLowerInvariant(), " ");
            return Regex.Replace(s.Trim(), @"\s+", " ");
        }

        private static int Levenshtein(string a, string b)
        {
            if (a.Length == 0)
            {
                return b.Length;
            }

            if (b.Length == 0)
            {
                return a.Length;
            }

            var d = new int[a.Length + 1, b.Length + 1];
            for (var i = 0; i <= a.Length; i++)
            {
                d[i, 0] = i;
            }

            for (var j = 0; j <= b.Length; j++)
            {
                d[0, j] = j;
            }

            for (var i = 1; i <= a.Length; i++)
            {
                for (var j = 1; j <= b.Length; j++)
                {
                    var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                }
            }

            return d[a.Length, b.Length];
        }

        private static int Ratio(string a, string b)
        {
            var maxLen = Math.Max(a.Length, b.Length);
            if (maxLen == 0)
            {
                return 100;
            }

            return (int)(100.0 * (maxLen - Levenshtein(a, b)) / maxLen);
        }

        private static int PartialRatio(string query, string text)
        {
            if (string.IsNullOrEmpty(query))
            {
                return 100;
            }

            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            if (query.Length > text.Length)
            {
                return Ratio(query, text);
            }

            var best = 0;
            for (var i = 0; i <= text.Length - query.Length; i++)
            {
                var score = Ratio(query, text.Substring(i, query.Length));
                if (score > best)
                {
                    best = score;
                }

                if (best == 100)
                {
                    break;
                }
            }

            return best;
        }

        private static int TokenSortRatio(string a, string b)
        {
            var sortedA = string.Join(" ", a.Split(' ', StringSplitOptions.RemoveEmptyEntries).OrderBy(t => t));
            var sortedB = string.Join(" ", b.Split(' ', StringSplitOptions.RemoveEmptyEntries).OrderBy(t => t));
            return Ratio(sortedA, sortedB);
        }
    }
}
