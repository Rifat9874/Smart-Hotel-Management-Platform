using HotelManagement.Web.Data;
using HotelManagement.Web.Models;
using HotelManagement.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelManagement.Web.Controllers;

/// <summary>User_Profile.cs + Edit_Info.cs (customer part) from the WinForms project.</summary>
[Authorize(Roles = "CUSTOMER")]
public class ProfileController : AppController
{
    public ProfileController(Db db, Clock clock) : base(db, clock) { }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var me = await Db.QueryOneAsync("SELECT NAME, PHONE_NUMBER, EMAIL FROM USER_TABLE WHERE EMAIL = @email", new { email = Email });
        if (me == null) return RedirectToAction("Login", "Account");

        var vm = new ProfileVm { Customer = me };

        vm.Bookings = await Db.QueryAsync(
            @"SELECT B.BOOKING_ID, B.ROOM_ID, R.[TYPE], B.CHECK_IN, B.CHECK_OUT, B.BOOKING_DATE, B.BOOKING_STATUS
              FROM BOOKINGS_TABLE B
              JOIN ROOM_TABLE R ON R.ROOM_ID = B.ROOM_ID
              WHERE B.EMAIL = @email
              ORDER BY TRY_CAST(B.BOOKING_ID AS INT) DESC, B.BOOKING_ID DESC", new { email = Email });

        vm.ReviewableBookings = await Db.QueryAsync(
            @"SELECT B.BOOKING_ID, B.ROOM_ID, B.CHECK_IN, B.CHECK_OUT
              FROM BOOKINGS_TABLE B
              WHERE B.EMAIL = @email AND B.BOOKING_STATUS = 'CHECK-OUT'
                AND NOT EXISTS (SELECT 1 FROM REVIEWS_TABLE V WHERE V.BOOKING_ID = B.BOOKING_ID)
              ORDER BY B.CHECK_OUT DESC", new { email = Email });

        vm.MyReviews = await Db.QueryAsync(
            @"SELECT V.BOOKING_ID, V.RATING, V.COMMENTS, V.REVIEW_DATE
              FROM REVIEWS_TABLE V WHERE V.EMAIL = @email ORDER BY V.REVIEW_DATE DESC", new { email = Email });

        return View(vm);
    }

    // ----------------------------------------------------------- edit info
    [HttpGet]
    public async Task<IActionResult> Edit()
    {
        var me = await Db.QueryOneAsync("SELECT NAME, PHONE_NUMBER, EMAIL FROM USER_TABLE WHERE EMAIL = @email", new { email = Email });
        return me == null ? NotFound() : View(me);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(string? name, string? phone, string? password, string? confirm)
    {
        name = (name ?? "").Trim();
        phone = (phone ?? "").Trim();

        string? error = null;
        if (name.Length == 0 || name.Length > 50) error = "Invalid Name";
        else if (!Check.Phone(phone)) error = "Invalid Number (11 digits)";
        else if (!string.IsNullOrEmpty(password) && password != confirm) error = "Passwords do not match!";
        else if (!string.IsNullOrEmpty(password) && !Check.Password(password)) error = "Password must be at least 8 characters!";

        if (error != null)
        {
            ViewData["Error"] = error;
            var me = await Db.QueryOneAsync("SELECT NAME, PHONE_NUMBER, EMAIL FROM USER_TABLE WHERE EMAIL = @email", new { email = Email });
            return View(me);
        }

        await Db.ExecAsync("UPDATE USER_TABLE SET NAME = @name, PHONE_NUMBER = @phone WHERE EMAIL = @email",
            new { name, phone, email = Email });
        if (!string.IsNullOrEmpty(password))
            await Db.ExecAsync("UPDATE USER_TABLE SET PASSWORD = @pw WHERE EMAIL = @email",
                new { pw = PasswordService.Hash(password), email = Email });

        await RefreshNameAsync(name);
        FlashOk("Your information has been updated.");
        return RedirectToAction(nameof(Index));
    }

    // ------------------------------------------------------------- cancel
    [HttpPost]
    public async Task<IActionResult> Cancel(string id)
    {
        // a guest can cancel until check-in; after that the front desk handles it
        var n = await Db.ExecAsync(
            @"UPDATE BOOKINGS_TABLE SET BOOKING_STATUS = 'CANCELLED'
              WHERE BOOKING_ID = @id AND EMAIL = @email AND BOOKING_STATUS IN ('PENDING', 'PAID', 'BOOKED')",
            new { id, email = Email });

        if (n > 0) FlashOk("Booking cancelled successfully!");
        else FlashError("Cannot cancel this booking. It may already be checked in / checked out, or it does not exist.");
        return RedirectToAction(nameof(Index));
    }

    // ------------------------------------------------------------- review
    [HttpPost]
    public async Task<IActionResult> Review(string? bookingId, int rating, string? comments)
    {
        if (string.IsNullOrEmpty(bookingId)) { FlashError("Please select a booking to review."); return RedirectToAction(nameof(Index)); }
        if (string.IsNullOrWhiteSpace(comments)) { FlashError("Please enter your comments."); return RedirectToAction(nameof(Index)); }
        if (rating < 1 || rating > 5) { FlashError("Rating must be between 1 and 5."); return RedirectToAction(nameof(Index)); }

        var eligible = await Db.ScalarAsync<int>(
            @"SELECT COUNT(*) FROM BOOKINGS_TABLE B
              WHERE B.BOOKING_ID = @bookingId AND B.EMAIL = @email AND B.BOOKING_STATUS = 'CHECK-OUT'
                AND NOT EXISTS (SELECT 1 FROM REVIEWS_TABLE V WHERE V.BOOKING_ID = B.BOOKING_ID)",
            new { bookingId, email = Email });
        if (eligible == 0) { FlashError("You can only review your own completed stays, once each."); return RedirectToAction(nameof(Index)); }

        await Db.ExecAsync(
            @"INSERT INTO REVIEWS_TABLE (BOOKING_ID, EMAIL, RATING, COMMENTS, REVIEW_DATE)
              VALUES (@bookingId, @email, @rating, @comments, @now)",
            new { bookingId, email = Email, rating, comments = comments.Trim(), now = Clock.Now });

        FlashOk("Thank you for your review!");
        return RedirectToAction(nameof(Index));
    }
}
