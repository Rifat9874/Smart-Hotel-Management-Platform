using iText.IO.Font.Constants;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Borders;
using iText.Layout.Element;
using iText.Layout.Properties;

namespace HotelManagement.Web.Services;

/// <summary>Everything the receipt needs. Plain data so it is easy to test.</summary>
public sealed class ReceiptData
{
    public string HotelName { get; init; } = "";
    public string HotelPhone { get; init; } = "";
    public string HotelAddress { get; init; } = "";
    public string HotelEmail { get; init; } = "";

    public string BookingId { get; init; } = "";
    public string GuestName { get; init; } = "";
    public string GuestEmail { get; init; } = "";
    public string RoomId { get; init; } = "";
    public string RoomType { get; init; } = "";
    public string CheckIn { get; init; } = "";
    public string CheckOut { get; init; } = "";
    public int Nights { get; init; }
    public decimal PricePerNight { get; init; }
    public decimal Total { get; init; }
    public string PaymentMethod { get; init; } = "";
    public string PaymentStatus { get; init; } = "PAID";
}

/// <summary>
/// Builds the PAYMENT RECEIPT pdf. This is the web version of Payment.GenerateReceipt()
/// from the WinForms project (same layout, same colours), moved from iTextSharp 5 to iText 9.
/// </summary>
public static class ReceiptPdf
{
    // Colours taken from the original WinForms receipt.
    private static readonly Color Gold = new DeviceRgb(218, 165, 32);
    private static readonly Color Blue = new DeviceRgb(43, 92, 122);

    public static byte[] Build(ReceiptData d)
    {
        using var ms = new MemoryStream();
        var writer = new PdfWriter(ms);
        writer.SetCloseStream(false);

        using (var pdf = new PdfDocument(writer))
        using (var doc = new Document(pdf, PageSize.A4))
        {
            doc.SetMargins(50, 50, 50, 50);

            PdfFont bold = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
            PdfFont regular = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
            PdfFont italic = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_OBLIQUE);

            // ---- header: hotel name (left) + title (right)
            var header = new Table(UnitValue.CreatePercentArray(new float[] { 55, 45 })).UseAllAvailableWidth();
            header.AddCell(NoBorderCell(d.HotelName.ToUpperInvariant(), bold, 20, Gold, TextAlignment.LEFT, 4));
            header.AddCell(NoBorderCell("PAYMENT RECEIPT", bold, 16, Blue, TextAlignment.RIGHT, 4));
            doc.Add(header);

            // ---- gold separator line
            var sep = new Table(1).UseAllAvailableWidth();
            sep.AddCell(new Cell().SetBorder(Border.NO_BORDER).SetHeight(2).SetBackgroundColor(Gold));
            sep.SetMarginBottom(14);
            doc.Add(sep);

            // ---- booking information
            var info = new Table(UnitValue.CreatePercentArray(new float[] { 30, 70 })).UseAllAvailableWidth();
            info.SetMarginBottom(10);
            InfoRow(info, "Receipt Number:", d.BookingId, bold, regular, 10, 5);
            InfoRow(info, "Issue Date:", DateTime.Now.ToString("dd MMMM yyyy HH:mm"), bold, regular, 10, 5);
            InfoRow(info, "Guest Name:", d.GuestName, bold, regular, 10, 5);
            InfoRow(info, "Guest Email:", d.GuestEmail, bold, regular, 10, 5);
            InfoRow(info, "Room:", string.IsNullOrWhiteSpace(d.RoomType) ? d.RoomId : d.RoomId + " - " + d.RoomType, bold, regular, 10, 5);
            InfoRow(info, "Check-in Date:", d.CheckIn, bold, regular, 10, 5);
            InfoRow(info, "Check-out Date:", d.CheckOut, bold, regular, 10, 5);
            doc.Add(info);

            // ---- "PAYMENT DETAILS" bar
            var bar = new Table(1).UseAllAvailableWidth();
            bar.SetMarginTop(15).SetMarginBottom(10);
            bar.AddCell(new Cell()
                .Add(new Paragraph("PAYMENT DETAILS").SetFont(bold).SetFontSize(14).SetFontColor(ColorConstants.WHITE).SetTextAlignment(TextAlignment.CENTER))
                .SetBackgroundColor(Blue).SetBorder(Border.NO_BORDER).SetPadding(8));
            doc.Add(bar);

            // ---- payment rows
            var pay = new Table(UnitValue.CreatePercentArray(new float[] { 40, 60 })).UseAllAvailableWidth();
            InfoRow(pay, "Rate per night:", Money(d.PricePerNight), bold, regular, 11, 6, Gold);
            InfoRow(pay, "Nights:", d.Nights.ToString(), bold, regular, 11, 6, Gold);
            InfoRow(pay, "Total Amount:", Money(d.Total), bold, regular, 11, 6, Gold);
            InfoRow(pay, "Payment Method:", string.IsNullOrWhiteSpace(d.PaymentMethod) ? "N/A" : d.PaymentMethod, bold, regular, 11, 6, Gold);
            InfoRow(pay, "Payment Status:", d.PaymentStatus, bold, regular, 11, 6, Gold);
            doc.Add(pay);

            // ---- big gold total bar
            var total = new Table(1).UseAllAvailableWidth();
            total.SetMarginTop(15);
            total.AddCell(new Cell()
                .Add(new Paragraph("TOTAL PAID: " + Money(d.Total)).SetFont(bold).SetFontSize(16).SetFontColor(ColorConstants.WHITE).SetTextAlignment(TextAlignment.CENTER))
                .SetBackgroundColor(Gold).SetBorder(Border.NO_BORDER).SetPadding(12));
            doc.Add(total);

            // ---- thank you + contact
            doc.Add(new Paragraph("Thank you for choosing " + d.HotelName + "!\nWe look forward to serving you again.")
                .SetFont(italic).SetFontSize(11).SetFontColor(ColorConstants.DARK_GRAY)
                .SetTextAlignment(TextAlignment.CENTER).SetMarginTop(24));

            var contact = new List<string>();
            if (!string.IsNullOrWhiteSpace(d.HotelPhone)) contact.Add("Front Desk: " + d.HotelPhone);
            if (!string.IsNullOrWhiteSpace(d.HotelEmail)) contact.Add("Email: " + d.HotelEmail);
            if (!string.IsNullOrWhiteSpace(d.HotelAddress)) contact.Add("Address: " + d.HotelAddress);
            if (contact.Count > 0)
            {
                doc.Add(new Paragraph(string.Join("\n", contact))
                    .SetFont(regular).SetFontSize(9).SetFontColor(ColorConstants.GRAY)
                    .SetTextAlignment(TextAlignment.CENTER).SetMarginTop(8));
            }
        }

        return ms.ToArray();
    }

    private static string Money(decimal v) => v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " BDT";

    private static Cell NoBorderCell(string text, PdfFont font, float size, Color color, TextAlignment align, float padding)
    {
        return new Cell()
            .Add(new Paragraph(text).SetFont(font).SetFontSize(size).SetFontColor(color).SetTextAlignment(align))
            .SetBorder(Border.NO_BORDER)
            .SetPaddingBottom(padding + 6);
    }

    private static void InfoRow(Table t, string label, string value, PdfFont bold, PdfFont regular, float size, float padding, Color? labelColor = null)
    {
        t.AddCell(new Cell()
            .Add(new Paragraph(label).SetFont(bold).SetFontSize(size).SetFontColor(labelColor ?? Blue))
            .SetBorder(Border.NO_BORDER).SetPadding(padding));
        t.AddCell(new Cell()
            .Add(new Paragraph(value ?? "").SetFont(regular).SetFontSize(size).SetFontColor(ColorConstants.BLACK))
            .SetBorder(Border.NO_BORDER).SetPadding(padding));
    }
}
