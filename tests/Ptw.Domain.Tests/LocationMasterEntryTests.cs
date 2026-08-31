using Ptw.Domain;

namespace Ptw.Domain.Tests;

public sealed class LocationMasterEntryTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 31, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ApprovedEntryIsEffectiveOnlyInsideItsPeriod()
    {
        var entry = Create();
        entry.SubmitForApproval("maker.user", Now.AddMinutes(1));
        entry.Approve("checker.user", Now.AddMinutes(2));

        Assert.False(entry.IsEffectiveAt(Now.AddHours(-1)));
        Assert.True(entry.IsEffectiveAt(Now.AddHours(1)));
        Assert.False(entry.IsEffectiveAt(Now.AddDays(2)));
    }

    [Fact]
    public void MakerCannotApproveOwnChange()
    {
        var entry = Create();
        entry.SubmitForApproval("maker.user", Now.AddMinutes(1));

        var exception = Assert.Throws<DomainRuleViolationException>(() =>
            entry.Approve("maker.user", Now.AddMinutes(2)));

        Assert.Equal("location.maker_checker_required", exception.Code);
        Assert.Equal(LocationMasterStatus.PendingApproval, entry.Status);
    }

    [Fact]
    public void ApprovedEntryCannotBeEditedInPlace()
    {
        var entry = Create();
        entry.SubmitForApproval("maker.user", Now.AddMinutes(1));
        entry.Approve("checker.user", Now.AddMinutes(2));

        var exception = Assert.Throws<DomainRuleViolationException>(() => entry.UpdateDraft(
            "AREA-01",
            "Nama yang diubah",
            null,
            Now,
            Now.AddDays(1),
            "maker.user",
            Now.AddMinutes(3)));

        Assert.Equal("location.invalid_transition", exception.Code);
    }

    [Fact]
    public void InvalidEffectivePeriodIsRejected()
    {
        var exception = Assert.Throws<DomainRuleViolationException>(() => LocationMasterEntry.CreateDraft(
            "AREA-01",
            "Area Satu",
            null,
            Now,
            Now,
            "maker.user",
            Now));

        Assert.Equal("location.invalid_effective_period", exception.Code);
    }

    [Fact]
    public void CheckerCanReturnPendingEntryForChanges()
    {
        var entry = Create();
        entry.SubmitForApproval("maker.user", Now.AddMinutes(1));
        entry.ReturnForChanges("checker.user", "Nama perlu diperjelas", Now.AddMinutes(2));

        Assert.Equal(LocationMasterStatus.Draft, entry.Status);
        Assert.Equal(3, entry.Version);
        Assert.Contains(entry.Events, item => item.Type == "location_returned_for_changes");
    }

    private static LocationMasterEntry Create() => LocationMasterEntry.CreateDraft(
        "AREA-01",
        "Area Satu",
        null,
        Now,
        Now.AddDays(1),
        "maker.user",
        Now);
}
