using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
namespace NzbDrone.Core.Notifications.Hardcover
{
    public class HardcoverNotification : NotificationBase<HardcoverNotificationSettings>
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        public override string Name => "Hardcover";
        public override string Link => "https://hardcover.app/";

        public HardcoverNotification(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public override void OnReleaseImport(BookDownloadMessage message)
        {
            var book = message.Book;
            var bookId = GetBookId(book.ForeignBookId);

            if (bookId == null)
            {
                _logger.Warn("Hardcover: Cannot push '{0}' — ForeignBookId '{1}' is not a valid Hardcover book ID", book.Title, book.ForeignBookId);
                return;
            }

            foreach (var listId in Settings.RemoveListIds)
            {
                RemoveBookFromList(bookId.Value, listId);
            }

            foreach (var listId in Settings.AddListIds)
            {
                AddBookToList(bookId.Value, listId);
            }
        }

        public override void OnBookDelete(BookDeleteMessage deleteMessage)
        {
            if (!deleteMessage.DeletedFiles)
            {
                return;
            }

            var bookId = GetBookId(deleteMessage.Book.ForeignBookId);
            if (bookId == null)
            {
                return;
            }

            foreach (var listId in Settings.RemoveListIds)
            {
                RemoveBookFromList(bookId.Value, listId);
            }
        }

        public override void OnAuthorDelete(AuthorDeleteMessage deleteMessage)
        {
            if (!deleteMessage.DeletedFiles)
            {
                return;
            }

            var books = deleteMessage.Author.Books?.Value ?? new List<NzbDrone.Core.Books.Book>();

            foreach (var book in books)
            {
                var bookId = GetBookId(book.ForeignBookId);
                if (bookId == null)
                {
                    continue;
                }

                foreach (var listId in Settings.RemoveListIds)
                {
                    RemoveBookFromList(bookId.Value, listId);
                }
            }
        }

        public override ValidationResult Test()
        {
            var failures = new List<ValidationFailure>();
            failures.AddIfNotNull(TestConnection());
            return new ValidationResult(failures);
        }

        public override object RequestAction(string action, IDictionary<string, string> query)
        {
            if (action.Equals("getLists", StringComparison.OrdinalIgnoreCase))
            {
                if (Settings.ApiKey.IsNullOrWhiteSpace())
                {
                    return new { options = new List<object>() };
                }

                try
                {
                    var lists = FetchLists();
                    var options = lists
                        .OrderBy(l => l.Name, StringComparer.InvariantCultureIgnoreCase)
                        .Select(l => new
                        {
                            Value = l.Id ?? l.Slug,
                            Name = l.Name ?? l.Slug
                        });

                    return new { options };
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Hardcover: Failed to fetch lists for notification settings");
                    return new { options = new List<object>() };
                }
            }

            return base.RequestAction(action, query);
        }

        private void AddBookToList(int bookId, string listId)
        {
            if (!int.TryParse(listId, out var listIdInt))
            {
                _logger.Warn("Hardcover: List ID '{0}' is not a valid integer, skipping add", listId);
                return;
            }

            var mutation = JsonSerializer.Serialize(new
            {
                query = @"mutation AddBookToList($listId: Int!, $bookId: Int!) {
                    insert_list_books_one(object: { list_id: $listId, book_id: $bookId }) {
                        id
                    }
                }",
                variables = new { listId = listIdInt, bookId }
            });

            try
            {
                var request = BuildRequest(mutation);
                _httpClient.Execute(request);
                _logger.Debug("Hardcover: Added book {0} to list {1}", bookId, listId);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Hardcover: Failed to add book {0} to list {1}", bookId, listId);
            }
        }

        private void RemoveBookFromList(int bookId, string listId)
        {
            if (!int.TryParse(listId, out var listIdInt))
            {
                _logger.Warn("Hardcover: List ID '{0}' is not a valid integer, skipping remove", listId);
                return;
            }

            var mutation = JsonSerializer.Serialize(new
            {
                query = @"mutation RemoveBookFromList($listId: Int!, $bookId: Int!) {
                    delete_list_books(where: { list_id: { _eq: $listId }, book_id: { _eq: $bookId } }) {
                        affected_rows
                    }
                }",
                variables = new { listId = listIdInt, bookId }
            });

            try
            {
                var request = BuildRequest(mutation);
                request.SuppressHttpError = true;
                _httpClient.Execute(request);
                _logger.Debug("Hardcover: Removed book {0} from list {1}", bookId, listId);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Hardcover: Failed to remove book {0} from list {1}", bookId, listId);
            }
        }

        private List<HardcoverListItem> FetchLists()
        {
            const string query = @"{""query"":""query Lists { me { lists { id name slug } } }""}";
            var request = BuildRequest(query);
            var response = _httpClient.Execute(request);

            if (response.HasHttpError)
            {
                throw new HttpException(request, response);
            }

            var payload = Newtonsoft.Json.JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JToken>(response.Content);
            var listsToken = payload?["data"]?["me"];

            if (listsToken == null || listsToken.Type != Newtonsoft.Json.Linq.JTokenType.Array)
            {
                return new List<HardcoverListItem>();
            }

            var result = new List<HardcoverListItem>();
            foreach (var me in listsToken.Children())
            {
                var lists = me["lists"];
                if (lists == null)
                {
                    continue;
                }

                foreach (var list in lists.Children())
                {
                    result.Add(new HardcoverListItem
                    {
                        Id = list.Value<string>("id"),
                        Slug = list.Value<string>("slug"),
                        Name = list.Value<string>("name")
                    });
                }
            }

            return result;
        }

        private HttpRequest BuildRequest(string body)
        {
            var apiKey = NormalizeApiKey(Settings.ApiKey);
            var request = new HttpRequestBuilder($"{Settings.BaseUrl.TrimEnd('/')}/v1/graphql")
                .Post()
                .Accept(HttpAccept.Json)
                .SetHeader("Authorization", $"Bearer {apiKey}")
                .SetHeader("X-Api-Key", apiKey)
                .SetHeader("User-Agent", "Readarr (Hardcover Notification)")
                .SetHeader("Content-Type", "application/json")
                .KeepAlive()
                .Build();

            request.RequestTimeout = TimeSpan.FromSeconds(30);
            request.SetContent(body);
            return request;
        }

        private ValidationFailure TestConnection()
        {
            try
            {
                FetchLists();
                return null;
            }
            catch (HttpException ex)
            {
                _logger.Warn(ex, "Hardcover: Test connection failed (HTTP {0})", ex.Response.StatusCode);
                if (ex.Response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                    ex.Response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    return new ValidationFailure(nameof(Settings.ApiKey), "Invalid Hardcover API key");
                }

                return new ValidationFailure(string.Empty, "Unable to connect to Hardcover. Check URL/API key and logs.");
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Hardcover: Test connection failed");
                return new ValidationFailure(string.Empty, "Unable to connect to Hardcover. Check logs for details.");
            }
        }

        private static int? GetBookId(string foreignBookId)
        {
            if (int.TryParse(foreignBookId, out var id))
            {
                return id;
            }

            return null;
        }

        private static string NormalizeApiKey(string apiKey)
        {
            if (apiKey.IsNullOrWhiteSpace())
            {
                return string.Empty;
            }

            var trimmed = apiKey.Trim();
            const string bearerPrefix = "bearer ";

            if (trimmed.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.Substring(bearerPrefix.Length).Trim();
            }

            return trimmed;
        }

        private class HardcoverListItem
        {
            public string Id { get; set; }
            public string Slug { get; set; }
            public string Name { get; set; }
        }
    }
}
