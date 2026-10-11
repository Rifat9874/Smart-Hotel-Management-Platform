using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using HotelManagement.Web.Data;
using HotelManagement.Web.Models;
using HotelManagement.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace HotelManagement.Web.Controllers;

/// <summary>Payment.cs + Pending_Payment.cs from the WinForms project.</summary>
[Authorize(Roles = "CUSTOMER")]
public class PaymentController : AppController
{
    private readonly HotelInfo _hotel;

    public PaymentController(Db db, Clock clock, IOptions<HotelInfo> hotel) : base(db, clock)
    {
        _hotel = hotel.Value;
    }

    private const string BookingSql = @"
        SELECT B.BOOKING_ID, B.ROOM_ID, B.CHECK_IN, B.CHECK_OUT, B.BOOKING_STATUS, B.EMAIL, U.NAME, R.[TYPE], R.PPN
        FROM BOOKINGS_TABLE B
        JOIN USER_TABLE U ON U.EMAIL = B.EMAIL
        JOIN ROOM_TABLE R ON R.ROOM_ID = B.ROOM_ID
        WHERE B.BOOKING_ID = @id AND B.EMAIL = @email";

    private static int NightsOf(Row b) =>
        Math.Max(1, ((b.Date("CHECK_OUT") ?? DateTime.Today) - (b.Date("CHECK_IN") ?? DateTime.Today)).Days);

    // ------------------------------------------------------------- pay form
    [HttpGet]
    public async Task<IActionResult> Pay(string id)
    {
        var b = await Db.QueryOneAsync(BookingSql, new { id, email = Email });
        if (b == null) return NotFound();

        var status = b.Str("BOOKING_STATUS").ToUpperInvariant();
        if (status == "CANCELLED") { FlashError("This booking was cancelled."); return RedirectToAction("Index", "Profile"); }
        if (status != "PENDING") return RedirectToAction(nameof(Success), new { id });

        return View(BuildVm(b, "", null));
    }

    private static PayVm BuildVm(Row b, string method, string? error)
    {
        var nights = NightsOf(b);
        return new PayVm
        {
            Booking = b,
            Nights = nights,
            Price = b.Int("PPN"),
            Total = (decimal)b.Int("PPN") * nights,
            Method = method,
            Error = error
        };
    }

    // ------------------------------------------- simulated OTP (no real SMS)
    [HttpPost, EnableRateLimiting("auth")]
    public IActionResult SendOtp(string id, string? mobile)
    {
        if (!Regex.IsMatch(mobile ?? "", @"^01[3-9]\d{8}$"))
            return Json(new { ok = false, message = "Please enter a valid mobile number first (11 digits starting with 013-019)." });

        var code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(CultureInfo.InvariantCulture);
        var expires = DateTime.UtcNow.AddMinutes(5).Ticks;
        HttpContext.Session.SetString("otp", $"{id}|{mobile}|{code}|{expires}");
        return Json(new { ok = true, otp = code, message = $"OTP sent to {mobile}. (This is a simulation - the code is shown here.)" });
    }

    private bool OtpIsValid(string id, string mobile, string? typed)
    {
        var raw = HttpContext.Session.GetString("otp");
        if (string.IsNullOrEmpty(raw) || string.IsNullOrEmpty(typed)) return false;
        var p = raw.Split('|');
        if (p.Length != 4) return false;
        if (p[0] != id || p[1] != mobile) return false;
        if (!long.TryParse(p[3], out var ticks) || DateTime.UtcNow.Ticks > ticks) return false;
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(p[2]), System.Text.Encoding.UTF8.GetBytes(typed.Trim()));
    }

    // ----------------------------------------------------------- pay (POST)
    [HttpPost]
    public async Task<IActionResult> Pay(string id, string? method,
        string? cardNumber, string? cardExpiry, string? cvv,
        string? mobile, string? trxId, string? otp,
        string? bankName, string? bankAccount, string? bankTrx)
    {
        var b = await Db.QueryOneAsync(BookingSql, new { id, email = Email });
        if (b == null) return NotFound();
        if (!b.Str("BOOKING_STATUS").Equals("PENDING", StringComparison.OrdinalIgnoreCase))
            return RedirectToAction(nameof(Success), new { id });

        method = (method ?? "").Trim();
        if (!Lists.PaymentMethods.Contains(method))
            return View(BuildVm(b, method, "Please select a payment method."));

        var today = Clock.Today;
        string cardText;       // goes into PAYMENT.CARD_NUMBER
        string codeText;       // goes into PAYMENT.CODE
        DateTime expDate = today;

        switch (method)
        {
            case "Credit/Debit Card":
            {
                var digits = Regex.Replace(cardNumber ?? "", @"[\s-]", "");
                if (!Regex.IsMatch(digits, @"^\d{16}$"))
                    return View(BuildVm(b, method, "Please enter a valid 16-digit card number."));

                if (!DateTime.TryParseExact((cardExpiry ?? "") + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exp)
                    || exp.AddMonths(1) <= today)
                    return View(BuildVm(b, method, "Card expiration date cannot be in the past."));

                if (!Regex.IsMatch(cvv ?? "", @"^\d{3,4}$"))
                    return View(BuildVm(b, method, "Please enter a valid CVV (3 or 4 digits)."));

                // The CVV and the full card number are checked but NEVER stored.
                cardText = "**** **** **** " + digits[^4..];
                codeText = "***";
                expDate = exp;
                break;
            }
            case "bKash":
            case "Rocket":
            case "Nagad":
            {
                mobile = (mobile ?? "").Trim();
                if (!Regex.IsMatch(mobile, @"^01[3-9]\d{8}$"))
                    return View(BuildVm(b, method, "Please enter a valid mobile number (11 digits starting with 013-019)."));
                if (string.IsNullOrWhiteSpace(trxId))
                    return View(BuildVm(b, method, "Please enter your transaction ID."));
                if (!Regex.IsMatch(otp ?? "", @"^\d{6}$"))
                    return View(BuildVm(b, method, "Please enter a valid 6-digit OTP code."));
                if (!OtpIsValid(id, mobile, otp))
                    return View(BuildVm(b, method, "OTP verification failed. Press \"Send OTP\" and use the new code."));

                cardText = $"{method}: {mobile}";
                codeText = trxId.Trim();
                break;
            }
            default: // Bank Transfer
            {
                if (string.IsNullOrWhiteSpace(bankName))
                    return View(BuildVm(b, method, "Please enter bank name."));
                if (string.IsNullOrWhiteSpace(bankAccount))
                    return View(BuildVm(b, method, "Please enter bank account number."));
                if (string.IsNullOrWhiteSpace(bankTrx))
                    return View(BuildVm(b, method, "Please enter bank transaction ID."));

                var acct = bankAccount.Trim();
                var shownAcct = acct.Length > 4 ? "****" + acct[^4..] : acct;
                cardText = $"Bank: {bankName.Trim()}, Acc: {shownAcct}";
                codeText = bankTrx.Trim();
                break;
            }
        }

        // PAYMENT.CARD_NUMBER and CODE are NVARCHAR(50)
        if (cardText.Length > 50) cardText = cardText[..50];
        if (codeText.Length > 50) codeText = codeText[..50];

        var bill = b.Int("PPN") * NightsOf(b);

        await using (var tx = await Db.BeginAsync())
        {
            // the UPDATE only succeeds once, so a double click cannot create two payments
            var changed = await tx.ExecAsync(
                @"UPDATE BOOKINGS_TABLE SET BOOKING_STATUS = 'PAID'
                  WHERE BOOKING_ID = @id AND EMAIL = @email AND BOOKING_STATUS = 'PENDING'", new { id, email = Email });
            if (changed == 0)
            {
                await tx.RollbackAsync();
                return RedirectToAction(nameof(Success), new { id });
            }

            var pid = await tx.ScalarAsync<int>(NextNumericIdSql("PAYMENT", "P_ID"));
            await tx.ExecAsync(
                @"INSERT INTO PAYMENT (P_ID, BOOKING_ID, CARD_NUMBER, EXP_DATE, CODE, BILL)
                  VALUES (@pid, @id, @card, @exp, @code, @bill)",
                new { pid = pid.ToString(CultureInfo.InvariantCulture), id, card = cardText, exp = expDate, code = codeText, bill });
            await tx.CommitAsync();
        }

        HttpContext.Session.Remove("otp");
        return RedirectToAction(nameof(Success), new { id });
    }

    // -------------------------------------------------------------- success
    [HttpGet]
    public async Task<IActionResult> Success(string id)
    {
        var b = await Db.QueryOneAsync(BookingSql, new { id, email = Email });
        if (b == null) return NotFound();
        if (b.Str("BOOKING_STATUS").Equals("PENDING", StringComparison.OrdinalIgnoreCase))
            return RedirectToAction(nameof(Pay), new { id });
        return View(BuildVm(b, "", null));
    }

    // -------------------------------------------------------------- receipt
    [HttpGet]
    public async Task<IActionResult> Receipt(string id)
    {
        var b = await Db.QueryOneAsync(BookingSql, new { id, email = Email });
        if (b == null) return NotFound();

        var status = b.Str("BOOKING_STATUS").ToUpperInvariant();
        if (status == "PENDING" || status == "CANCELLED")
        {
            FlashError("A receipt is only available for paid bookings.");
            return RedirectToAction("Index", "Profile");
        }

        var pay = await Db.QueryOneAsync(
            "SELECT TOP 1 CARD_NUMBER, BILL FROM PAYMENT WHERE BOOKING_ID = @id ORDER BY TRY_CAST(P_ID AS INT) DESC", new { id });

        var nights = NightsOf(b);
        var total = pay != null ? pay.Int("BILL") : b.Int("PPN") * nights;

        var data = new ReceiptData
        {
            HotelName = _hotel.Name,
            HotelPhone = _hotel.Phone,
            HotelAddress = _hotel.Address,
            HotelEmail = _hotel.Email,
            BookingId = b.Str("BOOKING_ID"),
            GuestName = b.Str("NAME"),
            GuestEmail = b.Str("EMAIL"),
            RoomId = b.Str("ROOM_ID"),
            RoomType = b.Str("TYPE"),
            CheckIn = b.DateStr("CHECK_IN"),
            CheckOut = b.DateStr("CHECK_OUT"),
            Nights = nights,
            PricePerNight = b.Int("PPN"),
            Total = total,
            PaymentMethod = MethodFromStored(pay?.Str("CARD_NUMBER")),
            PaymentStatus = "PAID"
        };

        var bytes = ReceiptPdf.Build(data);
        return File(bytes, "application/pdf", $"Hotel_Receipt_{id}_{Clock.Now:yyyyMMdd}.pdf");
    }

    /// <summary>PAYMENT has no "method" column, so it is read back from what was stored.</summary>
    private static string MethodFromStored(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return "N/A";
        if (stored.StartsWith("bKash", StringComparison.OrdinalIgnoreCase)) return "bKash";
        if (stored.StartsWith("Rocket", StringComparison.OrdinalIgnoreCase)) return "Rocket";
        if (stored.StartsWith("Nagad", StringComparison.OrdinalIgnoreCase)) return "Nagad";
        if (stored.StartsWith("Bank", StringComparison.OrdinalIgnoreCase)) return "Bank Transfer";
        return "Credit/Debit Card";
    }
}
