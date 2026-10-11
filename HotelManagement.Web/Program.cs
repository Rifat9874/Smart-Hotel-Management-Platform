using System.Threading.RateLimiting;
using HotelManagement.Web.Data;
using HotelManagement.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// ---------- MVC (every POST form is protected against CSRF automatically)
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

// ---------- our own services
builder.Services.Configure<HotelInfo>(builder.Configuration.GetSection("Hotel"));
builder.Services.AddSingleton<Db>();
builder.Services.AddSingleton<Clock>();
builder.Services.AddScoped<DbSetup>();

// ---------- login (cookie based, roles = USER_TYPE column)
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Denied";
        options.Cookie.Name = "hms.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();

// ---------- session (only used for the simulated mobile-banking OTP)
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.Name = "hms.session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// ---------- keep login cookies valid after the host recycles the app (if the folder is writable)
try
{
    var keyDir = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys");
    Directory.CreateDirectory(keyDir);
    var probe = Path.Combine(keyDir, ".probe");
    File.WriteAllText(probe, "ok");
    File.Delete(probe);
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keyDir))
        .SetApplicationName("RifatGrandHotel");
}
catch
{
    // read-only hosting folder: ASP.NET falls back to in-memory keys (users just have to log in again after a restart)
}

// ---------- slow down password guessing on Login / Forgot password
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

var app = builder.Build();

// ---------- friendly error page (shows the real message when DemoMode = true, handy while you set the site up)
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var feature = context.Features.Get<IExceptionHandlerFeature>();
    var demo = app.Configuration.GetValue<bool>("DemoMode");
    var msg = demo ? feature?.Error.Message : null;
    context.Response.StatusCode = 500;
    context.Response.ContentType = "text/html; charset=utf-8";
    var safe = System.Net.WebUtility.HtmlEncode(msg ?? "");
    await context.Response.WriteAsync(
        "<!doctype html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>" +
        "<title>Something went wrong</title><link rel='stylesheet' href='/css/site.css'></head><body>" +
        "<main class='container' style='padding:60px 0;text-align:center'>" +
        "<h1>Something went wrong</h1>" +
        "<p class='muted'>Sorry, the page could not be loaded.</p>" +
        (string.IsNullOrEmpty(safe) ? "" : "<pre class='errbox'>" + safe + "</pre>") +
        "<p>If this is a brand-new site, open <a href='/Setup'>/Setup</a> to check the database.</p>" +
        "<p><a class='btn' href='/'>Back to home</a></p></main></body></html>");
}));

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
