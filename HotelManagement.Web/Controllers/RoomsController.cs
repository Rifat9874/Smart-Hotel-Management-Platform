using HotelManagement.Web.Data;
using HotelManagement.Web.Models;
using HotelManagement.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HotelManagement.Web.Controllers;

/// <summary>Rooms.cs + Room_Details.cs + Confirm_Bookings.cs from the WinForms project.</summary>
public class RoomsController : AppController
{
    public RoomsController(Db db, Clock clock) : base(db, clock) { }

    // ------------------------------------------------------------ room list
    [HttpGet]
    public async Task<IActionResult> Index(string? checkIn, string? checkOut, string? q, string? sort, string? show)
    {
        var today = Clock.Today;
        var ci = Check.TryDate(checkIn, out var c1) ? c1 : today;
        var co = Check.TryDate(checkOut, out var c2) ? c2 : ci.AddDays(1);

        var vm = new RoomListVm { Search = (q ?? "").Trim(), Sort = sort ?? "", Show = show ?? "" };
        if (co <= ci)
        {
            vm.Error = "Check-out date must be after check-in date.";
            co = ci.AddDays(1);
        }
        vm.CheckIn = ci;
        vm.CheckOut = co;

        // only fixed words are ever put into the SQL text; user values go in as parameters
        var orderBy = sort switch
        {
            "alpha" => "R.[TYPE]",
            "low" => "R.PPN ASC",
            "high" => "R.PPN DESC",
            _ => "R.ROOM_ID"
        };

        var like = "%" + vm.Search.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%";

        var sql = $@"
            SELECT R.ROOM_ID, R.[TYPE], R.PPN, R.[DESCRIPTION], R.PICTURE,
                   CASE WHEN EXISTS (
                        SELECT 1 FROM BOOKINGS_TABLE B
                        WHERE B.ROOM_ID = R.ROOM_ID
                          AND B.BOOKING_STATUS NOT IN ({Lists.InactiveStatuses})
                          AND @ci < B.CHECK_OUT AND @co > B.CHECK_IN)
                        THEN 0 ELSE 1 END AS IS_AVAILABLE
            FROM ROOM_TABLE R
            WHERE (@q = '' OR R.[TYPE] LIKE @like)
            ORDER BY {orderBy}";

        var rooms = await Db.QueryAsync(sql, new { ci, co, q = vm.Search, like });
        if (vm.Show == "available") rooms = rooms.Where(r => r.Int("IS_AVAILABLE") == 1).ToList();
        else if (vm.Show == "booked") rooms = rooms.Where(r => r.Int("IS_AVAILABLE") == 0).ToList();
        vm.Rooms = rooms;

        return View(vm);
    }

    // --------------------------------------------------------- room details
    [HttpGet]
    public async Task<IActionResult> Details(string id, string? checkIn, string? checkOut)
    {
        var room = await Db.QueryOneAsync(
            @"SELECT ROOM_ID, [TYPE], PPN, [DESCRIPTION], PICTURE, COMPLIMENTARY, FEATURES
              FROM ROOM_TABLE WHERE ROOM_ID = @id", new { id });
        if (room == null) return NotFound();

        var today = Clock.Today;
        var ci = Check.TryDate(checkIn, out var c1) && c1 >= today ? c1 : today;
        var co = Check.TryDate(checkOut, out var c2) && c2 > ci ? c2 : ci.AddDays(1);

        var vm = new RoomDetailsVm { Room = room, CheckIn = ci, CheckOut = co, Today = today };

        vm.Images.Add(RoomImages.Full(room.Str("PICTURE")));
        var extra = await Db.QueryOneAsync("SELECT IMAGE_1, IMAGE_2, IMAGE_3 FROM ADDITIONAL_PICTURES WHERE ROOM_ID = @id", new { id });
        if (extra != null)
        {
            foreach (var col in new[] { "IMAGE_1", "IMAGE_2", "IMAGE_3" })
            {
                var f = RoomImages.FileName(extra.Str(col));
                if (f.Length > 0) vm.Images.Add(RoomImages.Full(f));
            }
        }

        // "These dates are already taken" list (from Confirm_Bookings.cs)
        vm.BookedRanges = await Db.QueryAsync(
            $@"SELECT CHECK_IN, CHECK_OUT FROM BOOKINGS_TABLE
               WHERE ROOM_ID = @id AND BOOKING_STATUS NOT IN ({Lists.InactiveStatuses}) AND CHECK_OUT >= @today
               ORDER BY CHECK_IN", new { id, today });

        vm.Available = !vm.BookedRanges.Any(b => ci < b.Date("CHECK_OUT") && co > b.Date("CHECK_IN"));
        return View(vm);
    }

    // ---------------------------------------------------------------- book
    [HttpPost, Authorize(Roles = "CUSTOMER")]
    public async Task<IActionResult> Book(string id, string? checkIn, string? checkOut)
    {
        if (!Check.TryDate(checkIn, out var ci) || !Check.TryDate(checkOut, out var co))
        {
            FlashError("Please choose valid check-in and check-out dates.");
            return RedirectToAction(nameof(Details), new { id });
        }
        if (ci < Clock.Today)
        {
            FlashError("Check-in date cannot be in the past.");
            return RedirectToAction(nameof(Details), new { id });
        }
        if (co <= ci)
        {
            FlashError("Check-out date must be after check-in date.");
            return RedirectToAction(nameof(Details), new { id, checkIn, checkOut });
        }
        if ((co - ci).TotalDays > 60)
        {
            FlashError("Online bookings are limited to 60 nights.");
            return RedirectToAction(nameof(Details), new { id });
        }

        string newId;
        await using (var tx = await Db.BeginAsync())
        {
            var ppn = await tx.ScalarAsync<int?>("SELECT PPN FROM ROOM_TABLE WHERE ROOM_ID = @id", new { id });
            if (ppn == null) return NotFound();

            // lock + check + insert in one transaction so two guests cannot grab the same room
            var clash = await tx.ScalarAsync<int>(
                $@"SELECT COUNT(*) FROM BOOKINGS_TABLE WITH (UPDLOCK, HOLDLOCK)
                   WHERE ROOM_ID = @id AND BOOKING_STATUS NOT IN ({Lists.InactiveStatuses})
                     AND @ci < CHECK_OUT AND @co > CHECK_IN", new { id, ci, co });
            if (clash > 0)
            {
                await tx.RollbackAsync();
                FlashError("This room is already booked for the selected dates.");
                return RedirectToAction(nameof(Details), new { id, checkIn, checkOut });
            }

            var next = await tx.ScalarAsync<int>(NextNumericIdSql("BOOKINGS_TABLE", "BOOKING_ID"));
            newId = next.ToString();
            var nights = Math.Max(1, (co - ci).Days);

            await tx.ExecAsync(
                @"INSERT INTO BOOKINGS_TABLE (BOOKING_ID, EMAIL, ROOM_ID, CHECK_IN, CHECK_OUT, BOOKING_DATE, BOOKING_STATUS, TOTAL_AMOUNT)
                  VALUES (@bid, @email, @id, @ci, @co, @bd, 'PENDING', @total)",
                new
                {
                    bid = newId,
                    email = Email,
                    id,
                    ci,
                    co,
                    bd = Clock.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                    total = (decimal)(ppn.Value * nights)
                });
            await tx.CommitAsync();
        }

        return RedirectToAction("Pay", "Payment", new { id = newId });
    }
}
