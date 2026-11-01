using FlexPlan.Data;
using FlexPlan.Models;
using FlexPlan.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

// Npgsql: keep using plain local DateTime values (FlexPlan stores local times).
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// When hosted behind a proxy (Render, etc.) the host handles HTTPS for us.
var behindProxy = builder.Configuration["FLEXPLAN_BEHIND_PROXY"] == "true";
if (behindProxy)
{
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.KnownNetworks.Clear();
        o.KnownProxies.Clear();
    });
}

builder.Services.AddControllersWithViews(options =>
{
    // Protect every POST action against CSRF.
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

// Database: PostgreSQL when DATABASE_URL is set (hosting), otherwise a local SQLite file.
var databaseUrl = builder.Configuration["DATABASE_URL"];
builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (!string.IsNullOrWhiteSpace(databaseUrl))
        options.UseNpgsql(BuildPostgresConnection(databaseUrl));
    else
        options.UseSqlite(builder.Configuration.GetConnectionString("Default"));
});

builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<SchedulerService>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Login";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
    });

var app = builder.Build();

// Create or upgrade the database.
using (var scope = app.Services.CreateScope())
{
    DatabaseBootstrapper.PrepareAndMigrate(
        scope.ServiceProvider.GetRequiredService<AppDbContext>(), app.Logger);
}

if (behindProxy) app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    if (!behindProxy) app.UseHsts();
}

if (!behindProxy) app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

// Accepts either a postgres://user:pass@host/db URL or a normal Npgsql connection string.
static string BuildPostgresConnection(string raw)
{
    if (!raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
        !raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        return raw;

    var uri = new Uri(raw);
    var info = uri.UserInfo.Split(':', 2);
    var cs = new Npgsql.NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.Port > 0 ? uri.Port : 5432,
        Username = Uri.UnescapeDataString(info[0]),
        Password = info.Length > 1 ? Uri.UnescapeDataString(info[1]) : "",
        Database = uri.AbsolutePath.TrimStart('/'),
        SslMode = Npgsql.SslMode.Require,
        TrustServerCertificate = true
    };
    return cs.ConnectionString;
}
