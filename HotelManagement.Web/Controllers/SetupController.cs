using HotelManagement.Web.Data;
using HotelManagement.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace HotelManagement.Web.Controllers;

/// <summary>
/// /Setup : checks the database connection and (on request) creates the tables + demo data.
/// Safe to open any time: it only ever adds what is missing.
/// </summary>
public class SetupController : AppController
{
    private readonly DbSetup _setup;
    private readonly bool _enabled;

    public SetupController(Db db, Clock clock, DbSetup setup, IConfiguration config) : base(db, clock)
    {
        _setup = setup;
        // appsettings.json: "EnableSetupPage": false  hides this page (do that when your site is ready)
        _enabled = config.GetValue("EnableSetupPage", true);
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (!_enabled) return NotFound();
        return View(await _setup.GetStatusAsync());
    }

    [HttpPost]
    public async Task<IActionResult> Run()
    {
        if (!_enabled) return NotFound();
        try
        {
            ViewData["Log"] = await _setup.InitializeAsync();
        }
        catch (Exception ex)
        {
            ViewData["SetupError"] = ex.Message;
        }
        return View("Index", await _setup.GetStatusAsync());
    }
}
