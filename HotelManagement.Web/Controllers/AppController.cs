using System.Security.Claims;
using HotelManagement.Web.Data;
using HotelManagement.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace HotelManagement.Web.Controllers;

/// <summary>Small base class: gives every controller the database, the clock and flash messages.</summary>
public abstract class AppController : Controller
{
    protected readonly Db Db;
    protected readonly Clock Clock;

    protected AppController(Db db, Clock clock)
    {
        Db = db;
        Clock = clock;
    }

    protected string Email => User.Email();

    /// <summary>Green message shown at the top of the next page.</summary>
    protected void FlashOk(string message)
    {
        TempData["flash"] = message;
        TempData["flashType"] = "ok";
    }

    /// <summary>Red message shown at the top of the next page.</summary>
    protected void FlashError(string message)
    {
        TempData["flash"] = message;
        TempData["flashType"] = "error";
    }

    /// <summary>Redirect only to local URLs (stops "open redirect" tricks).</summary>
    protected IActionResult RedirectLocal(string? url, string fallbackAction, string fallbackController)
    {
        if (!string.IsNullOrEmpty(url) && Url.IsLocalUrl(url)) return Redirect(url);
        return RedirectToAction(fallbackAction, fallbackController);
    }

    /// <summary>After "Edit profile": put the new name into the login cookie so the top bar updates at once.</summary>
    protected async Task RefreshNameAsync(string newName)
    {
        var claims = User.Claims.Where(c => c.Type != ClaimTypes.Name).ToList();
        claims.Add(new Claim(ClaimTypes.Name, newName));
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
    }

    /// <summary>Next free numeric id for a table whose key column is text (BOOKING_ID, P_ID, ...).</summary>
    protected static string NextNumericIdSql(string table, string column) =>
        $"SELECT ISNULL(MAX(TRY_CAST({column} AS INT)), 0) + 1 FROM {table} WITH (UPDLOCK, HOLDLOCK)";
}
