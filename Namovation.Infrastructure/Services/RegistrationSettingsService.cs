using Microsoft.EntityFrameworkCore;
using Namovation.Application;
using Namovation.Domain;
using Namovation.Infrastructure.Data;

namespace Namovation.Infrastructure.Services;

public sealed class RegistrationSettingsService(NamovationDbContext db) : IRegistrationSettingsService
{
    public async Task<RegistrationSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var settings = await db.RegistrationSettings.SingleOrDefaultAsync(x => x.Id == RegistrationSettings.SingletonId, cancellationToken);
        if (settings is not null) return settings;

        settings = new RegistrationSettings { AuthenticatorSetupRequired = true };
        db.RegistrationSettings.Add(settings);
        await db.SaveChangesAsync(cancellationToken);
        return settings;
    }

    public async Task UpdateAuthenticatorRequirementAsync(bool required, string adminUserId, CancellationToken cancellationToken = default)
    {
        var settings = await GetAsync(cancellationToken);
        settings.AuthenticatorSetupRequired = required;
        settings.UpdatedAtUtc = DateTime.UtcNow;
        settings.UpdatedByUserId = adminUserId;
        await db.SaveChangesAsync(cancellationToken);
    }
}
