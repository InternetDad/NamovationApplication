using System.ComponentModel.DataAnnotations;

namespace Namovation.Web.Models;

public sealed record AdminUserRow(string UserId, string Email, string DisplayName, string? Designation, string? Location, IReadOnlySet<string> Roles);

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
