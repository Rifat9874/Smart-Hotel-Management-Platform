using HotelManagement.Web.Data;
using HotelManagement.Web.Models;
using HotelManagement.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelManagement.Web.Controllers;

/// <summary>Reciptionist.cs + Update_Booking_Status_RC.cs + QuickCheckOutForm.cs.</summary>
[Authorize(Roles = "RECEPTIONIST")]
public class ReceptionController : AppController
{
    public ReceptionController(Db db, Clock clock) : base(db, clock) { }

    private const string BookingListSql = @"
        SELECT B.BOOKING_ID, B.ROOM_ID, B.EMAIL, U.NAME, B.CHECK_IN, B.CHECK_OUT, B.BOOKING_STATUS
        FROM BOOKINGS_TABLE B
        JOIN USER_TABLE U ON U.EMAIL = B.EMAIL";

    [HttpGet]
    public async Task<IActionResult> Index(string? status)
    {
        // only a known status word is accepted as a filter
        var filter = Lists.BookingStatuses.FirstOrDefault(s => s.Equals(status, StringComparison.OrdinalIgnoreCase)) ?? "";

        var vm = new ReceptionVm
        {
            Status = filter,
            ReceptionistName = User.FullName(),
            Bookings = await Db.QueryAsync(
                BookingListSql + @"
                WHERE (@status = '' OR B.BOOKING_STATUS = @status)
                ORDER BY B.CHECK_IN DESC, TRY_CAST(B.BOOKING_ID AS INT) DESC", new { status = filter })
        };
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Booking(string id)
    {
        var b = await Db.QueryOneAsync(
            @"SELECT B.BOOKING_ID, B.ROOM_ID, B.EMAIL, U.NAME, U.PHONE_NUMBER, B.CHECK_IN, B.CHECK_OUT,
                     B.BOOKING_DATE, B.BOOKING_STATUS, R.[TYPE], R.PPN
              FROM BOOKINGS_TABLE B
              JOIN USER_TABLE U ON U.EMAIL = B.EMAIL
              JOIN ROOM_TABLE R ON R.ROOM_ID = B.ROOM_ID
              WHERE B.BOOKING_ID = @id", new { id });
        return b == null ? NotFound() : View(b);
    }

    [HttpPost]
    public async Task<IActionResult> SetStatus(string id, string? status)
    {
        var allowed = new[] { "BOOKED", "CHECK-IN", "CHECK-OUT" };
        status = (status ?? "").Trim().ToUpperInvariant();
        if (!allowed.Contains(status)) { FlashError("Please choose a valid status."); return RedirectToAction(nameof(Booking), new { id }); }

        var n = await Db.ExecAsync("UPDATE BOOKINGS_TABLE SET BOOKING_STATUS = @status WHERE BOOKING_ID = @id", new { status, id });
        if (n > 0) FlashOk($"Booking {id} is now {status}.");
        else FlashError("Booking not found.");
        return RedirectToAction(nameof(Booking), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Remove(string id)
    {
        await using var tx = await Db.BeginAsync();
        await tx.ExecAsync("DELETE FROM PAYMENT WHERE BOOKING_ID = @id", new { id });
        await tx.ExecAsync("DELETE FROM REVIEWS_TABLE WHERE BOOKING_ID = @id", new { id });
        var n = await tx.ExecAsync("DELETE FROM BOOKINGS_TABLE WHERE BOOKING_ID = @id", new { id });
        await tx.CommitAsync();

        if (n > 0) FlashOk($"Booking {id} was removed.");
        else FlashError("Booking not found.");
        return RedirectToAction(nameof(Index));
    }

    // ------------------------------------------------------------ check-out
    [HttpGet]
    public async Task<IActionResult> QuickCheckOut()
    {
        var rows = await Db.QueryAsync(
            BookingListSql + @"
            WHERE B.BOOKING_STATUS = 'CHECK-IN'
            ORDER BY B.CHECK_OUT, TRY_CAST(B.BOOKING_ID AS INT)");
        return View(rows);
    }

    [HttpPost]
    public async Task<IActionResult> CheckOut(string id, string? back)
    {
        var n = await Db.ExecAsync(
            "UPDATE BOOKINGS_TABLE SET BOOKING_STATUS = 'CHECK-OUT' WHERE BOOKING_ID = @id AND BOOKING_STATUS = 'CHECK-IN'", new { id });
        if (n > 0) FlashOk($"Booking {id} has been checked out.");
        else FlashError("Only guests who are currently checked in can be checked out.");
        return back == "quick" ? RedirectToAction(nameof(QuickCheckOut)) : RedirectToAction(nameof(Index));
    }
}
