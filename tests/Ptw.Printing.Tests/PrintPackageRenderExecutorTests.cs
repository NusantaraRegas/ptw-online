using Ptw.Application;

namespace Ptw.Printing.Tests;

public sealed class PrintPackageRenderExecutorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SuccessfulRenderStoresTheDocumentAndRecordsTheRendererVersion()
    {
        var store = new FakeStore(Job());
        var storage = new FakeStorage();
        var executor = new PrintPackageRenderExecutor(store, new FakeRenderer(), storage, new FixedClock());

        var claimed = await executor.RenderNextAsync(CancellationToken.None);

        Assert.True(claimed);
        Assert.Single(storage.Written);
        Assert.NotNull(store.Completed);
        Assert.Equal("fake-renderer/1", store.Completed!.Value.RendererVersion);
        Assert.Null(store.Failure);
    }

    [Fact]
    public async Task RenderFailureIsRecordedWithBackoffAndNeverTouchesThePermit()
    {
        var store = new FakeStore(Job());
        var executor = new PrintPackageRenderExecutor(
            store,
            new FakeRenderer { Throw = true },
            new FakeStorage(),
            new FixedClock());

        var claimed = await executor.RenderNextAsync(CancellationToken.None);

        Assert.True(claimed);
        Assert.Null(store.Completed);
        Assert.NotNull(store.Failure);
        Assert.Equal("render meledak", store.Failure!.Value.Reason);
        // First attempt backs off by two minutes, so a transient fault retries quickly.
        Assert.Equal(Now.AddMinutes(2), store.Failure!.Value.NextAttemptAt);
    }

    [Fact]
    public async Task NothingIsClaimedWhenTheRendererIsUnavailable()
    {
        var store = new FakeStore(Job());
        var executor = new PrintPackageRenderExecutor(
            store,
            new FakeRenderer { Available = false },
            new FakeStorage(),
            new FixedClock());

        Assert.False(await executor.RenderNextAsync(CancellationToken.None));
        Assert.False(store.ClaimAttempted);
    }

    [Fact]
    public async Task AnEmptyQueueIsNotTreatedAsWork()
    {
        var store = new FakeStore(null);
        var executor = new PrintPackageRenderExecutor(
            store,
            new FakeRenderer(),
            new FakeStorage(),
            new FixedClock());

        Assert.False(await executor.RenderNextAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(3, 8)]
    [InlineData(5, 30)]
    [InlineData(12, 30)]
    public void BackoffGrowsExponentiallyAndIsCapped(int attempts, int expectedMinutes) =>
        Assert.Equal(
            TimeSpan.FromMinutes(expectedMinutes),
            PrintPackageRenderExecutor.BackoffFor(attempts));

    private static PrintPackageRenderJob Job() => new(
        Guid.Parse("0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a01"),
        Guid.Parse("0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a02"),
        Guid.Parse("0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a03"),
        2,
        "{}",
        "HASH",
        0);

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class FakeRenderer : IPrintPackageRenderer
    {
        public bool Available { get; init; } = true;
        public bool Throw { get; init; }

        public bool IsAvailable => Available;

        public PrintPackageRenderResult Render(PrintPackageRenderRequest request) =>
            Throw
                ? throw new InvalidOperationException("render meledak")
                : new PrintPackageRenderResult([1, 2, 3], "application/pdf", "fake-renderer/1");
    }

    private sealed class FakeStorage : IGeneratedDocumentStorage
    {
        public List<Guid> Written { get; } = [];

        public Task<StoredGeneratedDocument> StoreAsync(
            Guid generatedDocumentId,
            byte[] content,
            CancellationToken cancellationToken)
        {
            Written.Add(generatedDocumentId);
            return Task.FromResult(new StoredGeneratedDocument($"{generatedDocumentId:N}.pdf", content.Length, "ABC"));
        }

        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new MemoryStream());

        public Task DeleteOrphanAsync(string storageKey, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FakeStore(PrintPackageRenderJob? job) : IPrintPackageStore
    {
        public bool ClaimAttempted { get; private set; }
        public (string RendererVersion, string MediaType)? Completed { get; private set; }
        public (string Reason, DateTimeOffset? NextAttemptAt)? Failure { get; private set; }

        public Task<PrintPackageRenderJob?> ClaimNextAsync(
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            ClaimAttempted = true;
            return Task.FromResult(job);
        }

        public Task CompleteRenderAsync(
            Guid generatedDocumentId,
            StoredGeneratedDocument stored,
            string mediaType,
            string rendererVersion,
            DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            Completed = (rendererVersion, mediaType);
            return Task.CompletedTask;
        }

        public Task FailRenderAsync(
            Guid generatedDocumentId,
            string failureReason,
            DateTimeOffset? nextAttemptAt,
            CancellationToken cancellationToken)
        {
            Failure = (failureReason, nextAttemptAt);
            return Task.CompletedTask;
        }

        public Task<PrintPackageDocumentEntry?> FindAsync(
            Guid permitId,
            Guid printPackageId,
            CancellationToken cancellationToken) =>
            Task.FromResult<PrintPackageDocumentEntry?>(null);

        public Task<string?> FindSnapshotJsonAsync(Guid printPackageId, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        public Task<IReadOnlyList<PrintPackageDocumentEntry>> ListForPermitAsync(
            Guid permitId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PrintPackageDocumentEntry>>([]);

        public Task RecordDownloadAsync(
            Guid permitId,
            Guid printPackageId,
            string actorId,
            string correlationId,
            DateTimeOffset now,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> RequestRetryAsync(
            Guid printPackageId,
            DateTimeOffset now,
            CancellationToken cancellationToken) => Task.FromResult(true);
    }
}
