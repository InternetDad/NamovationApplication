using System.ComponentModel.DataAnnotations;

namespace Namovation.Web.Models;

public sealed record AdminUserRow(string UserId, string Email, string DisplayName, string? Designation, string? Location, IReadOnlySet<string> Roles);
public sealed record AdminRegistrationSettingsViewModel(bool AuthenticatorSetupRequired, DateTime UpdatedAtUtc);

public sealed class AdminRegistrationSettingsInput
{
    public bool AuthenticatorSetupRequired { get; set; }
}

public sealed class AdminCreateUserInput
{
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
    [Required, StringLength(160)] public string DisplayName { get; set; } = "";
    [StringLength(120)] public string? Designation { get; set; }
    [StringLength(120)] public string? Location { get; set; }
    [Required, DataType(DataType.Password), MinLength(10)] public string Password { get; set; } = "";
    [Required, DataType(DataType.Password), Compare(nameof(Password))] public string ConfirmPassword { get; set; } = "";
    public List<string> Roles { get; set; } = ["Employee"];
}

public sealed class FirstAdminInput
{
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
    [Required, StringLength(160)] public string DisplayName { get; set; } = "";
    [Required, DataType(DataType.Password), MinLength(10)] public string Password { get; set; } = "";
    [Required, DataType(DataType.Password), Compare(nameof(Password))] public string ConfirmPassword { get; set; } = "";
}

public sealed class RegisterInput
{
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
    [Required, StringLength(160)] public string DisplayName { get; set; } = "";
    [StringLength(120)] public string? Designation { get; set; }
    [StringLength(120)] public string? Location { get; set; }
    [Required, DataType(DataType.Password), MinLength(10)] public string Password { get; set; } = "";
    [Required, DataType(DataType.Password), Compare(nameof(Password))] public string ConfirmPassword { get; set; } = "";
}

public sealed class AuthenticatorSetupInput
{
    [Required, RegularExpression("^\\d{6}$", ErrorMessage = "Enter the six-digit code shown by your authenticator app.")] public string Code { get; set; } = "";
}

public sealed record AuthenticatorSetupViewModel(string Email, string SharedKey);
public sealed record AuthenticatorSetupCompleteViewModel(IReadOnlyList<string> RecoveryCodes);

public sealed class AuthenticatorLoginInput
{
    [Required, StringLength(64)] public string Code { get; set; } = "";
    public bool RememberMe { get; set; }
    public bool RememberMachine { get; set; }
    public bool UseRecoveryCode { get; set; }
    public string? ReturnUrl { get; set; }
}

public sealed class LoginInput
{
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
    [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}
