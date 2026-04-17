using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.Clients.Soulseek;

namespace NzbDrone.Core.Test.Download.DownloadClientTests.SlskdTests
{
    [TestFixture]
    public class SlskdFixture : DownloadClientFixtureBase<SlskdDownloadClient>
    {
        private const string DownloadDir = "/local/soulseek/complete";
        private const string Username = "testuser";
        private const string TransferId = "abc123-def456";

        private SlskdTransferResponse BuildResponse(string state, string remoteFilename, string directory = null)
        {
            directory ??= remoteFilename.Replace('\\', '/').Split('/').Reverse().Skip(1).FirstOrDefault() ?? string.Empty;

            return new SlskdTransferResponse
            {
                Username = Username,
                Directories = new List<SlskdTransferDirectory>
                {
                    new SlskdTransferDirectory
                    {
                        Directory = directory,
                        Files = new List<SlskdTransfer>
                        {
                            new SlskdTransfer
                            {
                                Id = TransferId,
                                Username = Username,
                                Filename = remoteFilename,
                                Size = 500_000,
                                BytesTransferred = state.StartsWith("Completed") ? 500_000 : 250_000,
                                State = state,
                                Direction = "Download",
                                PercentComplete = state.StartsWith("Completed") ? 100 : 50,
                                RemainingSeconds = state.StartsWith("Completed") ? 0 : 30,
                            }
                        }
                    }
                }
            };
        }

        private void GivenTransfers(params SlskdTransferResponse[] responses)
        {
            Mocker.GetMock<ISlskdProxy>()
                .Setup(v => v.GetTransfers(It.IsAny<SlskdDownloadClientSettings>()))
                .Returns(responses.ToList());

            Mocker.GetMock<ISlskdProxy>()
                .Setup(v => v.GetDownloadDirectory(It.IsAny<SlskdDownloadClientSettings>()))
                .Returns(DownloadDir);
        }

        [SetUp]
        public void Setup()
        {
            Subject.Definition = new DownloadClientDefinition();
            Subject.Definition.Settings = new SlskdDownloadClientSettings
            {
                BaseUrl = "http://slskd:5030",
                ApiKey = "test-key"
            };
        }

        // ── Status mapping ──────────────────────────────────────────────────
        [TestCase("Completed, Succeeded", DownloadItemStatus.Completed)]
        [TestCase("Completed, Errored",   DownloadItemStatus.Completed)]
        [TestCase("Completed, Rejected",  DownloadItemStatus.Completed)]
        [TestCase("Completed, TimedOut",  DownloadItemStatus.Completed)]
        [TestCase("InProgress",           DownloadItemStatus.Downloading)]
        [TestCase("Requested",            DownloadItemStatus.Queued)]
        [TestCase("Initializing",         DownloadItemStatus.Queued)]
        [TestCase("Queued, Locally",      DownloadItemStatus.Failed)]
        [TestCase("Queued, Remotely",     DownloadItemStatus.Failed)]
        public void state_string_maps_to_correct_status(string slskdState, DownloadItemStatus expected)
        {
            var response = BuildResponse(slskdState, @"ebooks\Author\Book (123)\Book - Author.epub", @"ebooks\Author\Book (123)");
            GivenTransfers(response);

            var item = Subject.GetItems().Single();
            item.Status.Should().Be(expected);
        }

        // ── OutputPath ───────────────────────────────────────────────────────
        [Test]
        public void completed_item_output_path_uses_last_remote_dir_not_full_path()
        {
            // slskd stores files at {downloadDir}/{lastRemoteDir}/{filename}
            // NOT {downloadDir}/{username}/{fullRemotePath}
            var response = BuildResponse(
                "Completed, Succeeded",
                @"ebooks\Jenny Odell\How to Do Nothing_ Resisting the Attention Economy (6570)\How to Do Nothing_ Resisting the Attention - Jenny Odell.epub",
                @"ebooks\Jenny Odell\How to Do Nothing_ Resisting the Attention Economy (6570)");
            GivenTransfers(response);

            var item = Subject.GetItems().Single();

            item.OutputPath.FullPath.Should().Be(
                "/local/soulseek/complete/How to Do Nothing_ Resisting the Attention Economy (6570)/How to Do Nothing_ Resisting the Attention - Jenny Odell.epub");
        }

        [Test]
        public void completed_item_with_nested_remote_path_still_maps_to_correct_local_path()
        {
            var response = BuildResponse(
                "Completed, Succeeded",
                @"Books\eBooks\James S. A. Corey\Drive (65)\Drive - James S. A. Corey.epub",
                @"Books\eBooks\James S. A. Corey\Drive (65)");
            GivenTransfers(response);

            var item = Subject.GetItems().Single();

            item.OutputPath.FullPath.Should().Be(
                "/local/soulseek/complete/Drive (65)/Drive - James S. A. Corey.epub");
        }

        [Test]
        public void downloading_item_has_no_output_path()
        {
            var response = BuildResponse("InProgress", @"ebooks\Author\Book (1)\Book - Author.epub", @"ebooks\Author\Book (1)");
            GivenTransfers(response);

            var item = Subject.GetItems().Single();
            item.OutputPath.IsEmpty.Should().BeTrue();
        }

        [Test]
        public void non_book_file_is_excluded_from_items()
        {
            // cover.jpg and metadata.xml should not appear in the queue
            var response = new SlskdTransferResponse
            {
                Username = Username,
                Directories = new List<SlskdTransferDirectory>
                {
                    new SlskdTransferDirectory
                    {
                        Directory = @"ebooks\Author\Book (1)",
                        Files = new List<SlskdTransfer>
                        {
                            new SlskdTransfer { Id = "1", Filename = @"ebooks\Author\Book (1)\Book - Author.epub", State = "Completed, Succeeded", Size = 500_000, BytesTransferred = 500_000, Direction = "Download" },
                            new SlskdTransfer { Id = "2", Filename = @"ebooks\Author\Book (1)\cover.jpg",         State = "Completed, Succeeded", Size = 50_000,  BytesTransferred = 50_000,  Direction = "Download" },
                            new SlskdTransfer { Id = "3", Filename = @"ebooks\Author\Book (1)\metadata.opf",     State = "Completed, Succeeded", Size = 10_000,  BytesTransferred = 10_000,  Direction = "Download" },
                        }
                    }
                }
            };
            GivenTransfers(response);

            var items = Subject.GetItems().ToList();
            items.Should().HaveCount(1);
            items[0].Title.Should().Contain("Author");
        }

        // ── CanMoveFiles / CanBeRemoved ──────────────────────────────────────
        [TestCase("Completed, Succeeded", true,  true)]
        [TestCase("Completed, Errored",   true,  true)]
        [TestCase("Completed, Rejected",  true,  true)]
        [TestCase("Completed, TimedOut",  true,  true)]
        [TestCase("InProgress",           false, false)]
        [TestCase("Requested",            false, false)]
        public void can_move_and_remove_flags_match_state(string state, bool canMove, bool canRemove)
        {
            var response = BuildResponse(state, @"ebooks\Author\Book (1)\Book - Author.epub", @"ebooks\Author\Book (1)");
            GivenTransfers(response);

            var item = Subject.GetItems().Single();
            item.CanMoveFiles.Should().Be(canMove);
            item.CanBeRemoved.Should().Be(canRemove);
        }

        // ── DownloadId ───────────────────────────────────────────────────────
        [Test]
        public void download_id_includes_username_and_transfer_id()
        {
            var response = BuildResponse("InProgress", @"ebooks\Author\Book (1)\Book - Author.epub", @"ebooks\Author\Book (1)");
            GivenTransfers(response);

            var item = Subject.GetItems().Single();
            item.DownloadId.Should().Be($"{Username}/{TransferId}");
        }

        // ── Upload transfers are ignored ─────────────────────────────────────
        [Test]
        public void upload_transfers_are_not_returned()
        {
            var response = new SlskdTransferResponse
            {
                Username = Username,
                Directories = new List<SlskdTransferDirectory>
                {
                    new SlskdTransferDirectory
                    {
                        Directory = @"ebooks\Author\Book (1)",
                        Files = new List<SlskdTransfer>
                        {
                            new SlskdTransfer { Id = "u1", Filename = @"ebooks\Author\Book (1)\Book - Author.epub", State = "Completed, Succeeded", Size = 500_000, Direction = "Upload" },
                        }
                    }
                }
            };
            GivenTransfers(response);

            Subject.GetItems().Should().BeEmpty();
        }

        // ── GetDownloadDirectory only called when there are completed items ──
        [Test]
        public void get_download_directory_not_called_when_no_completed_transfers()
        {
            var response = BuildResponse("InProgress", @"ebooks\Author\Book (1)\Book - Author.epub", @"ebooks\Author\Book (1)");
            GivenTransfers(response);

            Subject.GetItems().ToList();

            Mocker.GetMock<ISlskdProxy>()
                .Verify(v => v.GetDownloadDirectory(It.IsAny<SlskdDownloadClientSettings>()), Times.Never);
        }

        [Test]
        public void get_download_directory_called_once_when_completed_transfer_exists()
        {
            var response = BuildResponse("Completed, Succeeded", @"ebooks\Author\Book (1)\Book - Author.epub", @"ebooks\Author\Book (1)");
            GivenTransfers(response);

            Subject.GetItems().ToList();

            Mocker.GetMock<ISlskdProxy>()
                .Verify(v => v.GetDownloadDirectory(It.IsAny<SlskdDownloadClientSettings>()), Times.Once);
        }

        // ── Failed state message ──────────────────────────────────────────────
        [Test]
        public void failed_item_includes_state_in_message()
        {
            var response = BuildResponse("Queued, Remotely", @"ebooks\Author\Book (1)\Book - Author.epub", @"ebooks\Author\Book (1)");
            GivenTransfers(response);

            var item = Subject.GetItems().Single();
            item.Status.Should().Be(DownloadItemStatus.Failed);
            item.Message.Should().Contain("Queued, Remotely");
        }
    }
}
