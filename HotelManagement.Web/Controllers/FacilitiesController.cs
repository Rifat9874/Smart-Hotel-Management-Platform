using HotelManagement.Web.Data;
using HotelManagement.Web.Models;
using HotelManagement.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelManagement.Web.Controllers;

/// <summary>Facility_Booking.cs + the facilities list of User_Profile.cs.</summary>
[Authorize(Roles = "CUSTOMER")]
public class FacilitiesController : AppController
{
    public FacilitiesController(Db db, Clock clock) : base(db, clock) { }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var vm = new FacilitiesVm
        {
            MyBookings = await Db.QueryAsync(
                @"SELECT FACILITY_BOOKING_ID, FACILITY_NAME, BOOKING_DATE, GUESTS_COUNT, TOTAL_PRICE, BOOKING_STATUS
                  FROM FACILITIES_BOOKING WHERE EMAIL = @email ORDER BY FACILITY_BOOKING_ID DESC", new { email = Email })
        };
        return View(vm);
    }

    [HttpGet]
    public IActionResult Book(string name)
    {
        var f = FacilityCatalog.Find(name);
        if (f == null) return NotFound();
        ViewData["Today"] = Clock.Today.ToString("yyyy-MM-dd");
        return View(f);
    }

    [HttpPost]
    public async Task<IActionResult> Book(string name, string? date, int units)
    {
        var f = FacilityCatalog.Find(name);
        if (f == null) return NotFound();

        if (!Check.TryDate(date, out var d) || d < Clock.Today)
        {
            FlashError("Please choose today or a future date.");
            return RedirectToAction(nameof(Book), new { name });
        }
        if (units < 1 || units > f.MaxUnits)
        {
            FlashError($"{f.UnitLabel} must be between 1 and {f.MaxUnits}.");
            return RedirectToAction(nameof(Book), new { name });
        }

        await Db.ExecAsync(
            @"INSERT INTO FACILITIES_BOOKING (EMAIL, FACILITY_NAME, BOOKING_DATE, GUESTS_COUNT, TOTAL_PRICE, BOOKING_STATUS, BOOKING_TIME)
              VALUES (@email, @name, @d, @units, @total, 'PENDING', @now)",
            new { email = Email, name = f.Name, d, units, total = f.Price * units, now = Clock.Now });

        FlashOk($"Your {f.Name} booking has been received!");
        return RedirectToAction(nameof(Index));
    }
}
