using System.Globalization;
using System.Text;
using BusinessPilot.Api.Data;
using BusinessPilot.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BusinessPilot.Api.Services;

public sealed record GeneratedInvoicePdf(byte[] Bytes, string FileName);

/// <summary>Creates a small, dependency-free PDF from persisted invoice snapshots.</summary>
public sealed class InvoicePdfService(BusinessPilotDbContext db)
{
    private const double PageWidth = 595.28;
    private const double PageHeight = 841.89;

    public async Task<GeneratedInvoicePdf?> GenerateAsync(Guid organizationId, Guid invoiceId, string userId, CancellationToken ct)
    {
        var isMember = await db.Memberships.AsNoTracking()
            .AnyAsync(x => x.OrganizationId == organizationId && x.UserId == userId, ct);
        if (!isMember) return null;

        var invoice = await db.Invoices.AsNoTracking()
            .Include(x => x.Items).Include(x => x.Payments)
            .FirstOrDefaultAsync(x => x.Id == invoiceId && x.OrganizationId == organizationId, ct);
        if (invoice is null) return null;
        var organization = await db.Organizations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == organizationId, ct);
        if (organization is null) return null;

        var pages = new List<PdfPage>();
        var page = NewPage();
        pages.Add(page);
        var y = 218d;
        DrawInvoiceHeading(page, organization, invoice, 1);
        DrawTableHeading(page, y);
        y += 22;

        foreach (var item in invoice.Items.OrderBy(x => x.Id))
        {
            var descriptionLines = Wrap(item.Description, 48);
            var rowHeight = Math.Max(31, descriptionLines.Count * 12 + 15);
            if (y + rowHeight > 690)
            {
                page = NewPage(); pages.Add(page);
                DrawInvoiceHeading(page, organization, invoice, pages.Count);
                DrawTableHeading(page, 153);
                y = 175;
            }

            page.Line(40, y + rowHeight, 555, y + rowHeight, "0.88 0.91 0.95", 0.6);
            for (var i = 0; i < descriptionLines.Count; i++)
                page.Text(descriptionLines[i], 10, 48, y + 15 + i * 12, "0.08 0.12 0.20");
            page.Text(item.Quantity.ToString("0.###", CultureInfo.InvariantCulture), 9, 352, y + 19, "0.24 0.30 0.40");
            page.Text(Money(item.UnitPrice), 9, 402, y + 19, "0.24 0.30 0.40");
            page.Text($"{item.TaxRate:0.##}%", 9, 466, y + 19, "0.24 0.30 0.40");
            page.Text(Money(item.LineTotal), 9, 508, y + 19, "0.08 0.12 0.20", bold: true);
            y += rowHeight;
        }

        if (y + 190 > 770)
        {
            page = NewPage(); pages.Add(page);
            DrawInvoiceHeading(page, organization, invoice, pages.Count);
            y = 155;
        }

        y += 20;
        page.Line(330, y, 555, y, "0.78 0.83 0.90", 0.8); y += 17;
        TotalRow(page, "Subtotal", invoice.Subtotal, ref y);
        TotalRow(page, "Tax", invoice.TaxTotal, ref y);
        page.Rect(325, y - 4, 230, 32, "0.93 0.96 1.00");
        page.Text("Total", 11, 340, y + 17, "0.06 0.12 0.24", bold: true);
        page.Text(Money(invoice.Total), 11, 470, y + 17, "0.06 0.12 0.24", bold: true);
        y += 39;
        var paid = invoice.Payments.Sum(x => x.Amount);
        TotalRow(page, "Amount paid", paid, ref y);
        TotalRow(page, "Balance due", Math.Max(0, invoice.Total - paid), ref y, bold: true);

        if (invoice.Payments.Count > 0)
        {
            y += 16;
            if (y + 40 > 770)
            {
                page = NewPage(); pages.Add(page);
                DrawInvoiceHeading(page, organization, invoice, pages.Count);
                y = 155;
            }
            page.Text("PAYMENT HISTORY", 8, 40, y, "0.34 0.41 0.53", bold: true);
            y += 20;
            foreach (var payment in invoice.Payments.OrderBy(x => x.PaidOn))
            {
                if (y + 18 > 780)
                {
                    page = NewPage(); pages.Add(page);
                    DrawInvoiceHeading(page, organization, invoice, pages.Count);
                    y = 155;
                    page.Text("PAYMENT HISTORY (CONTINUED)", 8, 40, y, "0.34 0.41 0.53", bold: true);
                    y += 20;
                }
                page.Text($"{payment.PaidOn:dd MMM yyyy}  |  {payment.Method}{(string.IsNullOrWhiteSpace(payment.Reference) ? "" : $"  |  Ref: {payment.Reference}")}", 8, 44, y, "0.24 0.30 0.40");
                page.Text(Money(payment.Amount), 8, 470, y, "0.08 0.12 0.20", bold: true);
                y += 17;
            }
        }

        if (!string.IsNullOrWhiteSpace(invoice.Notes))
        {
            y += 10;
            if (y + 40 > 770)
            {
                page = NewPage(); pages.Add(page);
                DrawInvoiceHeading(page, organization, invoice, pages.Count);
                y = 155;
            }
            page.Text("NOTES", 8, 40, y, "0.34 0.41 0.53", bold: true);
            y += 15;
            var noteLines = Wrap(invoice.Notes, 94);
            foreach (var line in noteLines)
            {
                if (y + 15 > 780)
                {
                    page = NewPage(); pages.Add(page);
                    DrawInvoiceHeading(page, organization, invoice, pages.Count);
                    y = 155;
                    page.Text("NOTES (CONTINUED)", 8, 40, y, "0.34 0.41 0.53", bold: true);
                    y += 15;
                }
                page.Text(line, 9, 40, y, "0.24 0.30 0.40");
                y += 13;
            }
        }

        for (var i = 0; i < pages.Count; i++)
        {
            pages[i].Line(40, 804, 555, 804, "0.88 0.91 0.95", 0.6);
            pages[i].Text("BusinessPilot  |  GST-ready, configurable invoice", 8, 40, 821, "0.44 0.50 0.60");
            pages[i].Text($"Page {i + 1} of {pages.Count}", 8, 495, 821, "0.44 0.50 0.60");
        }

        var safeName = string.Concat(invoice.Number.Select(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' ? ch : '-'));
        return new GeneratedInvoicePdf(BuildPdf(pages), $"invoice-{safeName}");
    }

    private static PdfPage NewPage()
    {
        var page = new PdfPage();
        page.Rect(0, 0, PageWidth, PageHeight, "1 1 1");
        return page;
    }

    private static void DrawInvoiceHeading(PdfPage page, Organization org, Invoice invoice, int pageNumber)
    {
        page.Rect(0, 0, PageWidth, 104, "0.06 0.12 0.24");
        page.Text(org.Name, 18, 40, 42, "1 1 1", bold: true);
        if (!string.IsNullOrWhiteSpace(org.Address))
            page.Text(Wrap(org.Address, 72)[0], 8, 40, 61, "0.82 0.87 0.94");
        if (!string.IsNullOrWhiteSpace(org.Gstin))
            page.Text($"GSTIN: {org.Gstin}", 8, 40, 78, "0.82 0.87 0.94");
        page.Text("INVOICE", 19, 415, 42, "1 1 1", bold: true);
        page.Text(invoice.Number, 10, 415, 62, "0.82 0.87 0.94");
        var status = invoice.Status != "Paid" && invoice.Status != "Cancelled" && invoice.DueDate < DateOnly.FromDateTime(DateTime.UtcNow)
            ? "Overdue" : invoice.Status;
        page.Text($"Status: {status}", 8, 415, 80, "0.82 0.87 0.94");

        if (pageNumber == 1)
        {
            page.Text("BILL TO", 8, 40, 135, "0.34 0.41 0.53", bold: true);
            page.Text(invoice.CustomerNameSnapshot, 12, 40, 156, "0.08 0.12 0.20", bold: true);
            var customerAddress = invoice.CustomerAddressSnapshot;
            if (!string.IsNullOrWhiteSpace(customerAddress))
                page.Text(Wrap(customerAddress, 60)[0], 9, 40, 173, "0.24 0.30 0.40");
            if (!string.IsNullOrWhiteSpace(invoice.CustomerGstinSnapshot))
                page.Text($"GSTIN: {invoice.CustomerGstinSnapshot}", 8, 40, 190, "0.24 0.30 0.40");
            page.Text("ISSUED", 8, 355, 135, "0.34 0.41 0.53", bold: true);
            page.Text(invoice.IssueDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture), 10, 355, 154, "0.08 0.12 0.20", bold: true);
            page.Text("DUE DATE", 8, 455, 135, "0.34 0.41 0.53", bold: true);
            page.Text(invoice.DueDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture), 10, 455, 154, "0.08 0.12 0.20", bold: true);
        }
    }

    private static void DrawTableHeading(PdfPage page, double y)
    {
        page.Rect(40, y - 5, 515, 24, "0.95 0.97 0.99");
        page.Text("DESCRIPTION", 8, 48, y + 11, "0.34 0.41 0.53", bold: true);
        page.Text("QTY", 8, 352, y + 11, "0.34 0.41 0.53", bold: true);
        page.Text("PRICE", 8, 402, y + 11, "0.34 0.41 0.53", bold: true);
        page.Text("TAX", 8, 466, y + 11, "0.34 0.41 0.53", bold: true);
        page.Text("AMOUNT", 8, 508, y + 11, "0.34 0.41 0.53", bold: true);
    }

    private static void TotalRow(PdfPage page, string label, decimal amount, ref double y, bool bold = false)
    {
        page.Text(label, 9, 340, y + 12, "0.24 0.30 0.40", bold);
        page.Text(Money(amount), 9, 470, y + 12, "0.08 0.12 0.20", bold);
        y += 19;
    }

    private static string Money(decimal amount) => $"INR {amount.ToString("N2", CultureInfo.InvariantCulture)}";

    private static List<string> Wrap(string value, int maxLength)
    {
        var words = value.Replace('\r', ' ').Replace('\n', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = new StringBuilder();
        foreach (var word in words)
        {
            if (word.Length > maxLength)
            {
                if (current.Length > 0) { lines.Add(current.ToString()); current.Clear(); }
                for (var offset = 0; offset < word.Length; offset += maxLength)
                    lines.Add(word.Substring(offset, Math.Min(maxLength, word.Length - offset)));
            }
            else if (current.Length == 0) current.Append(word);
            else if (current.Length + word.Length + 1 <= maxLength) current.Append(' ').Append(word);
            else { lines.Add(current.ToString()); current.Clear().Append(word); }
        }
        if (current.Length > 0) lines.Add(current.ToString());
        return lines.Count == 0 ? [""] : lines;
    }

    private static byte[] BuildPdf(IReadOnlyList<PdfPage> pages)
    {
        var objects = new List<byte[]> { Array.Empty<byte>(), Array.Empty<byte>(), Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"), Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>") };
        var pageIds = new List<int>();
        for (var i = 0; i < pages.Count; i++)
        {
            var pageId = objects.Count + 1;
            var contentId = pageId + 1;
            pageIds.Add(pageId);
            objects.Add(Ascii($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Num(PageWidth)} {Num(PageHeight)}] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentId} 0 R >>"));
            var stream = Ascii(pages[i].Content.ToString());
            using var content = new MemoryStream();
            content.Write(Ascii($"<< /Length {stream.Length} >>\nstream\n"));
            content.Write(stream);
            content.Write(Ascii("\nendstream"));
            objects.Add(content.ToArray());
        }
        objects[0] = Ascii("<< /Type /Catalog /Pages 2 0 R >>");
        objects[1] = Ascii($"<< /Type /Pages /Kids [{string.Join(' ', pageIds.Select(id => $"{id} 0 R"))}] /Count {pageIds.Count} >>");

        using var output = new MemoryStream();
        output.Write(Ascii("%PDF-1.4\n% BusinessPilot invoice\n"));
        var offsets = new long[objects.Count + 1];
        for (var i = 0; i < objects.Count; i++)
        {
            offsets[i + 1] = output.Position;
            output.Write(Ascii($"{i + 1} 0 obj\n")); output.Write(objects[i]); output.Write(Ascii("\nendobj\n"));
        }
        var xrefOffset = output.Position;
        output.Write(Ascii($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n"));
        for (var i = 1; i < offsets.Length; i++) output.Write(Ascii($"{offsets[i]:D10} 00000 n \n"));
        output.Write(Ascii($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF"));
        return output.ToArray();
    }

    private static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);
    private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private sealed class PdfPage
    {
        public StringBuilder Content { get; } = new();
        public void Text(string text, double size, double x, double top, string color, bool bold = false)
        {
            var safe = text.Replace("₹", "INR", StringComparison.Ordinal);
            var bytes = Encoding.Latin1.GetBytes(safe.Select(ch => ch <= 255 && !char.IsControl(ch) ? ch : '?').ToArray());
            Content.Append("BT /").Append(bold ? "F2" : "F1").Append(' ').Append(Num(size)).Append(" Tf ").Append(color).Append(" rg 1 0 0 1 ")
                .Append(Num(x)).Append(' ').Append(Num(PageHeight - top)).Append(" Tm <").Append(Convert.ToHexString(bytes)).Append("> Tj ET\n");
        }
        public void Rect(double x, double top, double width, double height, string fill)
            => Content.Append(fill).Append(" rg ").Append(Num(x)).Append(' ').Append(Num(PageHeight - top - height)).Append(' ')
                .Append(Num(width)).Append(' ').Append(Num(height)).Append(" re f\n");
        public void Line(double x1, double top1, double x2, double top2, string color, double width)
            => Content.Append(color).Append(" RG ").Append(Num(width)).Append(" w ").Append(Num(x1)).Append(' ').Append(Num(PageHeight - top1)).Append(" m ")
                .Append(Num(x2)).Append(' ').Append(Num(PageHeight - top2)).Append(" l S\n");
    }
}
