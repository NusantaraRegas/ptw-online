namespace Ptw.Domain;

public enum PermitStatus
{
    Draft,
    Submitted,
    UnderReview,
    RevisionRequired,
    AwaitingApproval,
    Approved,
    ReadyForIssue,
    Open,
    SuspensionRequested,
    Suspended,
    CompletionConfirmationPending,
    WorkCompleted,
    Closed,
    Rejected,
    Cancelled,
    Expired
}
