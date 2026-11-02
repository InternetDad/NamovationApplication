using Microsoft.AspNetCore.Identity;

namespace Namovation.Infrastructure.Data;

public sealed class NamovationUser : IdentityUser
{
    public string DisplayName { get; set; } = "";
    public string? Designation { get; set; }
    public string? Location { get; set; }
}
