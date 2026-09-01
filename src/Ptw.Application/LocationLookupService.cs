using Ptw.Contracts;
using Ptw.Domain;

namespace Ptw.Application;

public sealed class LocationLookupService(
    ILocationMasterStore store,
    IActorContext actorContext,
    IClock clock)
{
    public async Task<PagedResponse<LocationOptionResponse>> ListAsync(
        CancellationToken cancellationToken)
    {
        var actor = actorContext.Current;
        var now = clock.UtcNow;
        var items = (await store.ListAsync(cancellationToken))
            .Where(item => item.Entry.Status == LocationMasterStatus.Approved
                && item.Entry.IsEffectiveAt(now)
                && HasLocationScope(actor, item.Entry.Code))
            .Select(item => new LocationOptionResponse(
                item.Entry.Id,
                item.Entry.Code,
                item.Entry.Name))
            .ToArray();
        return new PagedResponse<LocationOptionResponse>(items, items.Length);
    }

    private static bool HasLocationScope(Actor actor, string locationCode) =>
        actor.LocationScopes.Contains("*") || actor.LocationScopes.Contains(locationCode);
}
