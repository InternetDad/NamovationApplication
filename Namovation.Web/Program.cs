using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Namovation.Application;
using Namovation.Infrastructure.Data;
using Namovation.Infrastructure.Services;
using Namovation.Web.Identity;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
builder.Services.AddDbContext<NamovationDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.")));
builder.Services.AddDefaultIdentity<NamovationUser>(options => {
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequiredLength = 10;
    options.Password.RequireUppercase = !builder.Environment.IsDevelopment();
}).AddRoles<IdentityRole>().AddEntityFrameworkStores<NamovationDbContext>();
builder.Services.AddScoped<IUserClaimsPrincipalFactory<NamovationUser>, DevelopmentEmployeeClaimsFactory>();
builder.Services.AddScoped<IWorkflowService, WorkflowService>();

var app = builder.Build();
if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler("/Home/Error"); app.UseHsts(); }
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

using (var scope = app.Services.CreateScope())
{
    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in new[] { "Employee", "InnovateManager", "InnovateBoard", "Admin" })
        if (!await roles.RoleExistsAsync(role)) await roles.CreateAsync(new IdentityRole(role));

}
app.Run();
