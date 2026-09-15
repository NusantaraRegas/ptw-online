namespace Ptw.Domain;

public enum PermitStatus
{
    Draft,
    UnderValidation,
    RevisionRequired,
    AwaitingAreaApproval,
    Issued,
    Suspended,
    ClosureRequested,
    Closed,
    Rejected,
    Cancelled,
    Expired
}
