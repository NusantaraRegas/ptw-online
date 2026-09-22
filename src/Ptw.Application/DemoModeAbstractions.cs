namespace Ptw.Application;

public sealed record StoredDemoModeSetting(
    bool Enabled,
    int Version,
    DateTimeOffset? UpdatedAt,
    string? UpdatedBy,
    string ETag);

public sealed record DemoModeCommandContext(
    string ActorId,
    string Operation,
    string Key,
    string RequestHash);

public interface IDemoModeStore
{
    Task<StoredDemoModeSetting> GetAsync(CancellationToken cancellationToken);
    Task<StoredDemoModeSetting?> FindCommandResultAsync(
        string actorId,
        string operation,
        string key,
        string requestHash,
        CancellationToken cancellationToken);
    Task<StoredDemoModeSetting> SetAsync(
        bool enabled,
        string expectedETag,
        Actor actor,
        string correlationId,
        DemoModeCommandContext command,
        CancellationToken cancellationToken);
}

