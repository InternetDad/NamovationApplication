namespace Namovation.Domain;

public sealed class RegistrationSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public bool AuthenticatorSetupRequired { get; set; } = true;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? UpdatedByUserId { get; set; }
}
