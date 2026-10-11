using System.Security.Claims;
using HotelManagement.Web.Data;
using HotelManagement.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HotelManagement.Web.Controllers;

public class AccountController : AppController
{
    private readonly IConfiguration _config;

    public AccountController(Db db, Clock clock, IConfiguration config) : base(db, clock)
    {
        _config = config;
    }

    // ------------------------------------------------------------------ login
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToDashboard(User.Role());
        ViewData["ReturnUrl"] = returnUrl;
        ViewData["DemoMode"] = _config.GetValue<bool>("DemoMode");
        return View();
    }

    [HttpPost, EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(string? email, string? password, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        ViewData["DemoMode"] = _config.GetValue<bool>("DemoMode");
        email = (email ?? "").Trim();

        if (email.Length == 0 || string.IsNullOrEmpty(password))
        {
            ViewData["Error"] = "Please enter both Email and Password.";
            return View();
        }

        var user = await Db.QueryOneAsync(
            "SELECT NAME, EMAIL, PASSWORD, USER_TYPE FROM USER_TABLE WHERE EMAIL = @email", new { email });

        // same message for "no such user" and "wrong password" (does not reveal which emails exist)
        var (ok, needsUpgrade) = user == null ? (false, false) : PasswordService.Verify(user.Str("PASSWORD"), password);
        if (user == null || !ok)
        {
            ViewData["Error"] = "Invalid email or password.";
            return View();
        }

        if (needsUpgrade)
        {
            await Db.ExecAsync("UPDATE USER_TABLE SET PASSWORD = @pw WHERE EMAIL = @email",
                new { pw = PasswordService.Hash(password), email = user.Str("EMAIL") });
        }

        var role = user.Str("USER_TYPE").Trim().ToUpperInvariant();
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, user.Str("EMAIL")),
            new(ClaimTypes.Name, user.Str("NAME")),
            new(ClaimTypes.Role, role)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)) return Redirect(returnUrl);
        return RedirectToDashboard(role);
    }

    private IActionResult RedirectToDashboard(string role) => role switch
    {
        "ADMIN" => RedirectToAction("Dashboard", "Admin"),
        "RECEPTIONIST" => RedirectToAction("Index", "Reception"),
        "STAFF" => RedirectToAction("Index", "Staff"),
        _ => RedirectToAction("Index", "Rooms")
    };

    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Index", "Home");
    }

    public IActionResult Denied() => View();

    // --------------------------------------------------------------- register
    [HttpGet]
    public IActionResult Register() => View();

    [HttpPost, EnableRateLimiting("auth")]
    public async Task<IActionResult> Register(string? name, string? phone, string? email, string? password, string? confirm)
    {
        name = (name ?? "").Trim();
        phone = (phone ?? "").Trim();
        email = (email ?? "").Trim();

        var errors = new Dictionary<string, string>();
        if (name.Length == 0 || name.Length > 50) errors["name"] = "Invalid Name";
        if (!Check.Phone(phone)) errors["phone"] = "Invalid Number (11 digits)";
        if (!Check.Email(email)) errors["email"] = "Invalid Email";
        if (password != confirm) errors["password"] = "Passwords do not match!";
        else if (!Check.Password(password)) errors["password"] = "Password must be at least 8 characters!";

        if (errors.Count == 0)
        {
            var exists = await Db.ScalarAsync<int>("SELECT COUNT(*) FROM USER_TABLE WHERE EMAIL = @email", new { email });
            if (exists > 0) errors["email"] = "This email is already registered.";
        }

        if (errors.Count > 0)
        {
            ViewData["Errors"] = errors;
            ViewData["name"] = name;
            ViewData["phone"] = phone;
            ViewData["email"] = email;
            return View();
        }

        await Db.ExecAsync(
            @"INSERT INTO USER_TABLE (NAME, PHONE_NUMBER, EMAIL, PASSWORD, USER_TYPE)
              VALUES (@name, @phone, @email, @pw, 'CUSTOMER')",
            new { name, phone, email, pw = PasswordService.Hash(password!) });

        FlashOk("Account created. You can log in now.");
        return RedirectToAction(nameof(Login));
    }

    // ---------------------------------------------------------- forgot password
    [HttpGet]
    public IActionResult ForgotPassword() => View();

    /// <summary>Same rule as the WinForms app: the e-mail plus the registered phone number must match.</summary>
    [HttpPost, EnableRateLimiting("auth")]
    public async Task<IActionResult> ForgotPassword(string? email, string? phone, string? password, string? confirm)
    {
        email = (email ?? "").Trim();
        phone = (phone ?? "").Trim();

        if (password != confirm) { ViewData["Error"] = "Passwords do not match!"; return View(); }
        if (!Check.Password(password)) { ViewData["Error"] = "Password must be at least 8 characters!"; return View(); }

        var stored = await Db.ScalarAsync<string>("SELECT PHONE_NUMBER FROM USER_TABLE WHERE EMAIL = @email", new { email });
        if (stored == null || stored != phone)
        {
            ViewData["Error"] = "Invalid Information";
            return View();
        }

        await Db.ExecAsync("UPDATE USER_TABLE SET PASSWORD = @pw WHERE EMAIL = @email",
            new { pw = PasswordService.Hash(password!), email });
        FlashOk("Your password has been successfully changed!");
        return RedirectToAction(nameof(Login));
    }
}
