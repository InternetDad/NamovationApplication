using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.HttpOverrides;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Namovation.Application;
using Namovation.Infrastructure.Data;
using Namovation.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    // Render terminates TLS at its edge proxy and forwards requests to the container.
    // Its proxy addresses can change, so the service must only be exposed through Render.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("account-verification", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 8,
            Window = TimeSpan.FromMinutes(15),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var databaseUrl = builder.Configuration["DATABASE_URL"];
if (!string.IsNullOrWhiteSpace(databaseUrl))
{
    var uri = new Uri(databaseUrl);
    var credentials = Uri.UnescapeDataString(uri.UserInfo).Split(':', 2);
    var connection = new NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.Port > 0 ? uri.Port : 5432,
        Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
        Username = credentials[0],
        Password = credentials.Length > 1 ? credentials[1] : string.Empty
    };
    if (uri.Query.Contains("sslmode=require", StringComparison.OrdinalIgnoreCase))
    {
        connection.SslMode = SslMode.Require;
        connection.TrustServerCertificate = true;
    }
    connectionString = connection.ConnectionString;
}
builder.Services.AddDbContext<NamovationDbContext>(options => options.UseNpgsql(
    connectionString ?? throw new InvalidOperationException("Connection string 'DefaultConnection' or DATABASE_URL is missing.")));
builder.Services.AddDefaultIdentity<NamovationUser>(options => {
    options.SignIn.RequireConfirmedAccount = false;
    options.SignIn.RequireConfirmedEmail = false;
    options.Password.RequiredLength = 10;
    options.Password.RequireUppercase = !builder.Environment.IsDevelopment();
    options.User.RequireUniqueEmail = true;
}).AddRoles<IdentityRole>().AddEntityFrameworkStores<NamovationDbContext>();
builder.Services.AddScoped<IWorkflowService, WorkflowService>();
builder.Services.AddSingleton<IIdeaDocumentStore>(_ => new PrivateIdeaDocumentStore(
    Path.Combine(builder.Environment.ContentRootPath, "App_Data", "IdeaDocuments")));
builder.Services.AddScoped<IRegistrationSettingsService, RegistrationSettingsService>();

var app = builder.Build();
app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler("/Home/Error"); app.UseHsts(); }
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

using (var scope = app.Services.CreateScope())
{
    if (builder.Configuration.GetValue<bool>("RUN_MIGRATIONS_ON_STARTUP"))
        await scope.ServiceProvider.GetRequiredService<NamovationDbContext>().Database.MigrateAsync();

    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in new[] { "Employee", "InnovateManager", "InnovateBoard", "Admin" })
        if (!await roles.RoleExistsAsync(role)) await roles.CreateAsync(new IdentityRole(role));

}
app.Run();
