using System.Text;
using Ptw.Contracts;
using Ptw.Domain;

namespace Ptw.Application;

public sealed class OperationsBoardService(
    IPermitStore store,
    IActorContext actorContext,
    IClock clock)
{
    private static readonly IReadOnlySet<string> AllowedRoles = new HashSet<string>(
        ["Administrator", "HSEValidator", "AreaOwnerSeniorOfficer", "AreaOwnerManager"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> Statuses =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["UNDER_VALIDATION"] = "UnderValidation",
            ["REVISION_REQUIRED"] = "RevisionRequired",
            ["AWAITING_AREA_APPROVAL"] = "AwaitingAreaApproval",
            ["ISSUED"] = "Issued",
            ["SUSPENDED"] = "Suspended",
            ["CLOSURE_REQUESTED"] = "ClosureRequested",
            ["CLOSED"] = "Closed",
            ["REJECTED"] = "Rejected",
            ["CANCELLED"] = "Cancelled",
            ["EXPIRED"] = "Expired"
        };

    private static readonly HashSet<string> OperationalStatuses = new(
        ["ISSUED", "SUSPENDED", "CLOSURE_REQUESTED"],
        StringComparer.OrdinalIgnoreCase);

    public async Task<OperationsBoardResponse> ListAsync(
        string? status,
        string? locationId,
        string? permitClass,
        string? search,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var actor = actorContext.Current;
        var isAdministrator = actor.Roles.Contains("Administrator");
        if (!actor.Roles.Overlaps(AllowedRoles))
        {
            throw new UnauthorizedAccessException(
                "Papan Operasi hanya tersedia untuk Administrator, PIC HSE, dan Pemilik Wilayah.");
        }

        if (offset < 0 || limit is < 1 or > 100)
        {
            throw new InvalidRequestException(
                "operations.pagination_invalid",
                "Offset harus nol atau lebih dan limit harus antara 1 sampai 100.");
        }

        var normalizedLocation = NormalizeOptional(locationId);
        if (normalizedLocation is not null
            && !actor.LocationScopes.Contains("*")
            && !actor.LocationScopes.Contains(normalizedLocation))
        {
            throw new UnauthorizedAccessException(
                "Lokasi Papan Operasi berada di luar cakupan otorisasi pengguna.");
        }

        var normalizedStatus = NormalizeOptional(status);
        if (normalizedStatus is not null
            && (!Statuses.TryGetValue(normalizedStatus, out _)
                || (!isAdministrator && !OperationalStatuses.Contains(normalizedStatus))))
        {
            throw new InvalidRequestException(
                "operations.status_invalid",
                "Status Papan Operasi tidak dikenali.");
        }

        PermitClass? parsedClass = null;
        var normalizedClass = NormalizeOptional(permitClass);
        if (normalizedClass is not null)
        {
            if (!Enum.TryParse<PermitClass>(normalizedClass, true, out var value))
            {
                throw new InvalidRequestException(
                    "operations.permit_class_invalid",
                    "Kelas izin Papan Operasi tidak dikenali.");
            }
            parsedClass = value;
        }

        var normalizedSearch = NormalizeOptional(search);
        if (normalizedSearch?.Length > 100)
        {
            throw new InvalidRequestException(
                "operations.search_too_long",
                "Kata pencarian Papan Operasi maksimum 100 karakter.");
        }

        var now = clock.UtcNow;
        var page = await store.ListOperationsBoardAsync(
            actor.LocationScopes,
            new OperationsBoardQuery(
                normalizedStatus is null ? null : Statuses[normalizedStatus],
                normalizedLocation,
                parsedClass,
                normalizedSearch,
                isAdministrator,
                offset,
                limit,
                now,
                now.AddHours(24)),
            cancellationToken);

        return new OperationsBoardResponse(
            new OperationsBoardMetricsResponse(
                page.Metrics.Total,
                page.Metrics.UnderValidation,
                page.Metrics.RevisionRequired,
                page.Metrics.AwaitingAreaApproval,
                page.Metrics.Issued,
                page.Metrics.Suspended,
                page.Metrics.ExpiringSoon,
                page.Metrics.ClosureRequested,
                page.Metrics.Closed,
                page.Metrics.Rejected,
                page.Metrics.Cancelled,
                page.Metrics.Expired),
            page.Items.Select(item => new OperationsBoardItemResponse(
                item.Id,
                item.PermitNumber,
                item.Draft.Title,
                item.Draft.Company,
                item.Draft.LocationId,
                item.Draft.PermitClass.ToString(),
                ToUpperSnakeCase(item.Status),
                item.Draft.ValidFrom,
                item.Draft.ValidUntil,
                item.UpdatedAt,
                item.SuspensionReason)).ToArray(),
            page.Count,
            now);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ToUpperSnakeCase(string value)
    {
        var result = new StringBuilder(value.Length + 4);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (index > 0 && char.IsUpper(character))
            {
                result.Append('_');
            }
            result.Append(char.ToUpperInvariant(character));
        }
        return result.ToString();
    }
}
