using Ptw.Domain;

namespace Ptw.Domain.Tests;

public sealed class UserAccountTests
{
    [Fact]
    public void ProfileNormalizesUsernameAndVersionsMaterialChanges()
    {
        var now = DateTimeOffset.UtcNow;
        var account = UserAccount.Create(
            "operator.one",
            " Operator.One ",
            "Operator One",
            "Operator",
            "Operasi",
            now);

        account.Update("Operator Satu", "Senior Operator", "Operasi", now.AddMinutes(1));
        account.SetActive(false, now.AddMinutes(2));

        Assert.Equal("operator.one", account.UserName);
        Assert.Equal("Operator Satu", account.DisplayName);
        Assert.False(account.IsActive);
        Assert.Equal(3, account.Version);
    }

    [Fact]
    public void SecurityStampRotatesOnDeactivationAndRevocationOnly()
    {
        var now = DateTimeOffset.UtcNow;
        var account = UserAccount.Create("operator.two", "operator.two", "Operator Dua", null, null, now);
        var original = account.SecurityStamp;
        Assert.False(string.IsNullOrWhiteSpace(original));

        account.Update("Operator Dua", "Operator", "Operasi", now.AddMinutes(1));
        Assert.Equal(original, account.SecurityStamp);

        account.RevokeSessions(now.AddMinutes(2));
        var revoked = account.SecurityStamp;
        Assert.NotEqual(original, revoked);
        Assert.Equal(3, account.Version);

        account.SetActive(false, now.AddMinutes(3));
        Assert.NotEqual(revoked, account.SecurityStamp);

        var rehydrated = UserAccount.Rehydrate(
            "operator.two", "operator.two", "Operator Dua", null, null, true, 4, now, now, "stamp-from-store");
        Assert.Equal("stamp-from-store", rehydrated.SecurityStamp);
    }

    [Fact]
    public void InvalidAccountIsRejected()
    {
        var exception = Assert.Throws<DomainRuleViolationException>(() =>
            UserAccount.Create("", "operator", "Operator", null, null, DateTimeOffset.UtcNow));

        Assert.Equal("user.required", exception.Code);
    }
}
