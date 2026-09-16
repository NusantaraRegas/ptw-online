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
                    .Select(option => new PermitWorkTypeOptionResponse(
                        option.Code,
                        option.Label,
                        option.RequiresDetail))
                    .ToArray()))
            .ToArray();

    [HttpGet("safety-equipment")]
    public IReadOnlyList<PermitSafetyEquipmentCatalogResponse> SafetyEquipment() =>
        Enum.GetValues<PermitClass>()
            .Select(permitClass => new PermitSafetyEquipmentCatalogResponse(
                permitClass.ToString(),
                PermitSafetyEquipmentCatalog.Resolve(permitClass)
                    .OrderBy(option => option.TemplateColumn)
                    .ThenBy(option => option.TemplateIndex)
                    .Select(option => new PermitSafetyEquipmentOptionResponse(option.Code, option.Label))
                    .ToArray()))
            .ToArray();

    [HttpGet("supporting-documents")]
    public IReadOnlyList<PermitSupportingDocumentOptionResponse> SupportingDocuments() =>
        PermitSupportingDocumentCatalog.Resolve()
            .OrderBy(option => option.TemplateColumn)
            .ThenBy(option => option.TemplateIndex)
            .Select(option => new PermitSupportingDocumentOptionResponse(
                option.Code,
                option.Label,
                option.TemplateColumn,
                option.TemplateIndex,
                option.Required,
                option.RequiresMetadata))
            .ToArray();
}
