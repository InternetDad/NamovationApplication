using Namovation.Domain;

namespace Namovation.Application;

public interface IRegistrationSettingsService
{
    Task<RegistrationSettings> GetAsync(CancellationToken cancellationToken = default);
    Task UpdateAuthenticatorRequirementAsync(bool required, string adminUserId, CancellationToken cancellationToken = default);
}
