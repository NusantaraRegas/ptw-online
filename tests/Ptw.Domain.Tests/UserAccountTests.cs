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
    public void InvalidAccountIsRejected()
    {
        var exception = Assert.Throws<DomainRuleViolationException>(() =>
            UserAccount.Create("", "operator", "Operator", null, null, DateTimeOffset.UtcNow));

        Assert.Equal("user.required", exception.Code);
    }
}
