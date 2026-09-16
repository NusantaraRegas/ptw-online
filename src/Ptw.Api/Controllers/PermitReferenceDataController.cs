using Microsoft.AspNetCore.Mvc;
using Ptw.Contracts;
using Ptw.Domain;

namespace Ptw.Api.Controllers;

[ApiController]
[Route("api/v1/reference-data")]
public sealed class PermitReferenceDataController : ControllerBase
{
    [HttpGet("work-types")]
    public IReadOnlyList<PermitWorkTypeCatalogResponse> WorkTypes() =>
        Enum.GetValues<PermitClass>()
            .Select(permitClass => new PermitWorkTypeCatalogResponse(
                permitClass.ToString(),
                PermitWorkTypeCatalog.Resolve(permitClass)
                    .Select(option => new PermitWorkTypeOptionResponse(option.Code, option.Label))
                    .ToArray()))
            .ToArray();
}
