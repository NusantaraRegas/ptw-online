namespace Ptw.Domain;

public sealed class DomainRuleViolationException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}
