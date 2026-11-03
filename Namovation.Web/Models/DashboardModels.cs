namespace Namovation.Web.Models;

public sealed record DashboardMetric(string Label, string Value, string Detail);
public sealed record DashboardShortcut(string Title, string Description, string Controller, string Action, string ButtonText);
public sealed record HomeDashboardModel(
    bool IsAuthenticated,
    string DisplayName,
    bool AuthenticatorSetupPending,
    bool ShowRegister,
    bool ShowFirstAdminSetup,
    IReadOnlyList<DashboardMetric> Metrics,
    IReadOnlyList<DashboardShortcut> Shortcuts);
