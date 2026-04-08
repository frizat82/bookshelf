using System.Collections.Generic;
using System.Text;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Notifications.Hardcover;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.NotificationTests.Hardcover
{
    [TestFixture]
    public class HardcoverNotificationFixture : CoreTest<HardcoverNotification>
    {
        private const string ValidListsJson = @"{""data"":{""me"":[{""lists"":[{""id"":""123"",""name"":""Want to Read"",""slug"":""want-to-read""},{""id"":""456"",""name"":""Read"",""slug"":""read""}]}]}}";

        private HardcoverNotificationSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _settings = new HardcoverNotificationSettings
            {
                BaseUrl = "https://api.hardcover.app",
                ApiKey = "test-api-key",
                AddListIds = new[] { "123" },
                RemoveListIds = new[] { "456" }
            };

            Subject.Definition = new NotificationDefinition { Settings = _settings };

            GivenHttpResponse("{}");
        }

        private void GivenHttpResponse(string content)
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(s => s.Execute(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), Encoding.UTF8.GetBytes(content)));
        }

        private BookDownloadMessage GivenDownloadMessage(string foreignBookId = "42")
        {
            var book = Builder<Book>.CreateNew()
                .With(b => b.ForeignBookId = foreignBookId)
                .With(b => b.Title = "Test Book")
                .With(b => b.Editions = new LazyLoaded<List<Edition>>(new List<Edition>()))
                .With(b => b.Author = new LazyLoaded<Author>(Builder<Author>.CreateNew().Build()))
                .Build();

            return new BookDownloadMessage
            {
                Book = book,
                Author = Builder<Author>.CreateNew().Build()
            };
        }

        private BookDeleteMessage GivenBookDeleteMessage(string foreignBookId = "42", bool deletedFiles = true)
        {
            var book = Builder<Book>.CreateNew()
                .With(b => b.ForeignBookId = foreignBookId)
                .With(b => b.Title = "Test Book")
                .With(b => b.Author = new LazyLoaded<Author>(Builder<Author>.CreateNew().Build()))
                .Build();

            return new BookDeleteMessage(book, deletedFiles);
        }

        private AuthorDeleteMessage GivenAuthorDeleteMessage(bool deletedFiles = true)
        {
            var book = Builder<Book>.CreateNew()
                .With(b => b.ForeignBookId = "42")
                .Build();

            var author = Builder<Author>.CreateNew()
                .With(a => a.Books = new LazyLoaded<List<Book>>(new List<Book> { book }))
                .Build();

            return new AuthorDeleteMessage(author, deletedFiles);
        }

        private string GetRequestContent(HttpRequest request)
        {
            return Encoding.UTF8.GetString(request.ContentData);
        }

        [Test]
        public void should_add_book_to_add_lists_on_import()
        {
            Subject.OnReleaseImport(GivenDownloadMessage("42"));

            Mocker.GetMock<IHttpClient>()
                .Verify(c => c.Execute(It.Is<HttpRequest>(r =>
                    GetRequestContent(r).Contains("insert_list_books_one") &&
                    GetRequestContent(r).Contains("\"listId\":123") &&
                    GetRequestContent(r).Contains("\"bookId\":42"))),
                    Times.Once());
        }

        [Test]
        public void should_remove_book_from_remove_lists_on_import()
        {
            Subject.OnReleaseImport(GivenDownloadMessage("42"));

            Mocker.GetMock<IHttpClient>()
                .Verify(c => c.Execute(It.Is<HttpRequest>(r =>
                    GetRequestContent(r).Contains("delete_list_books") &&
                    GetRequestContent(r).Contains("\"listId\":456") &&
                    GetRequestContent(r).Contains("\"bookId\":42"))),
                    Times.Once());
        }

        [Test]
        public void should_make_two_requests_when_both_add_and_remove_lists_configured()
        {
            Subject.OnReleaseImport(GivenDownloadMessage("42"));

            Mocker.GetMock<IHttpClient>()
                .Verify(c => c.Execute(It.IsAny<HttpRequest>()), Times.Exactly(2));
        }

        [Test]
        public void should_not_call_api_when_foreign_book_id_is_not_integer()
        {
            Subject.OnReleaseImport(GivenDownloadMessage("gr:abc123"));

            Mocker.GetMock<IHttpClient>()
                .Verify(c => c.Execute(It.IsAny<HttpRequest>()), Times.Never());
        }

        [Test]
        public void should_not_call_api_when_foreign_book_id_is_empty()
        {
            Subject.OnReleaseImport(GivenDownloadMessage(""));

            Mocker.GetMock<IHttpClient>()
                .Verify(c => c.Execute(It.IsAny<HttpRequest>()), Times.Never());
        }

        [Test]
        public void should_only_remove_when_no_add_lists_configured()
        {
            _settings.AddListIds = new string[] { };

            Subject.OnReleaseImport(GivenDownloadMessage("42"));

            Mocker.GetMock<IHttpClient>()
                .Verify(c => c.Execute(It.Is<HttpRequest>(r =>
                    GetRequestContent(r).Contains("delete_list_books"))),
                    Times.Once());

            Mocker.GetMock<IHttpClient>()
                .Verify(c => c.Execute(It.Is<HttpRequest>(r =>
                    GetRequestContent(r).Contains("insert_list_books_one"))),
                    Times.Never());
        }

        [Test]
        public void should_remove_from_lists_on_book_delete_when_files_deleted()
        {
            Subject.OnBookDelete(GivenBookDeleteMessage(deletedFiles: true));

            Mocker.GetMock<IHttpClient>()
                .Verify(c => c.Execute(It.Is<HttpRequest>(r =>
                    GetRequestContent(r).Contains("delete_list_books"))),
                    Times.Once());
        }

        [Test]
        public void should_not_call_api_on_book_delete_when_files_not_deleted()
        {
            Subject.OnBookDelete(GivenBookDeleteMessage(deletedFiles: false));

            Mocker.GetMock<IHttpClient>()
                .Verify(c => c.Execute(It.IsAny<HttpRequest>()), Times.Never());
        }

        [Test]
        public void should_remove_all_author_books_from_lists_on_author_delete_when_files_deleted()
        {
            Subject.OnAuthorDelete(GivenAuthorDeleteMessage(deletedFiles: true));

            Mocker.GetMock<IHttpClient>()
                .Verify(c => c.Execute(It.Is<HttpRequest>(r =>
                    GetRequestContent(r).Contains("delete_list_books"))),
                    Times.Once());
        }

        [Test]
        public void should_not_call_api_on_author_delete_when_files_not_deleted()
        {
            Subject.OnAuthorDelete(GivenAuthorDeleteMessage(deletedFiles: false));

            Mocker.GetMock<IHttpClient>()
                .Verify(c => c.Execute(It.IsAny<HttpRequest>()), Times.Never());
        }

        [Test]
        public void should_return_lists_for_get_lists_action()
        {
            GivenHttpResponse(ValidListsJson);

            var result = Subject.RequestAction("getLists", new Dictionary<string, string>()) as dynamic;

            ((object)result).Should().NotBeNull();
        }

        [Test]
        public void should_return_empty_options_when_api_key_is_empty()
        {
            _settings.ApiKey = "";

            var result = Subject.RequestAction("getLists", new Dictionary<string, string>());

            Mocker.GetMock<IHttpClient>()
                .Verify(c => c.Execute(It.IsAny<HttpRequest>()), Times.Never());
        }

        [Test]
        public void should_pass_test_when_api_returns_lists()
        {
            GivenHttpResponse(ValidListsJson);

            var result = Subject.Test();

            result.IsValid.Should().BeTrue();
        }

        [Test]
        public void should_fail_test_when_api_returns_http_error()
        {
            Mocker.GetMock<IHttpClient>()
                .Setup(s => s.Execute(It.IsAny<HttpRequest>()))
                .Returns<HttpRequest>(r => new HttpResponse(r, new HttpHeader(), new byte[0], System.Net.HttpStatusCode.Unauthorized));

            var result = Subject.Test();

            result.IsValid.Should().BeFalse();
            result.Errors.Should().Contain(e => e.PropertyName == nameof(_settings.ApiKey));
        }

        [Test]
        public void should_use_bearer_token_in_authorization_header()
        {
            _settings.ApiKey = "my-secret-token";

            Subject.OnReleaseImport(GivenDownloadMessage("42"));

            Mocker.GetMock<IHttpClient>()
                .Verify(c => c.Execute(It.Is<HttpRequest>(r =>
                    r.Headers.ContainsKey("Authorization") &&
                    r.Headers["Authorization"] == "Bearer my-secret-token")),
                    Times.AtLeastOnce());
        }

        [Test]
        public void should_strip_bearer_prefix_from_api_key()
        {
            _settings.ApiKey = "Bearer my-secret-token";

            Subject.OnReleaseImport(GivenDownloadMessage("42"));

            Mocker.GetMock<IHttpClient>()
                .Verify(c => c.Execute(It.Is<HttpRequest>(r =>
                    r.Headers.ContainsKey("Authorization") &&
                    r.Headers["Authorization"] == "Bearer my-secret-token")),
                    Times.AtLeastOnce());
        }

        [Test]
        public void should_post_to_graphql_endpoint()
        {
            Subject.OnReleaseImport(GivenDownloadMessage("42"));

            Mocker.GetMock<IHttpClient>()
                .Verify(c => c.Execute(It.Is<HttpRequest>(r =>
                    r.Url.ToString().Contains("/v1/graphql"))),
                    Times.AtLeastOnce());
        }
    }
}
