using System.Globalization;
using System.Reflection;
using System.Text;
using Elio.Application.Invoices;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace Elio.Infrastructure.Invoices;

public sealed class InvoicePdfGenerator : IInvoicePdfGenerator
{
    private static readonly object FontLock = new();
    public InvoicePdfGenerator()
    {
        lock (FontLock) GlobalFontSettings.FontResolver ??= new InvoiceFontResolver();
    }
    public byte[] Generate(InvoiceDto invoice)
    {
        if (invoice.Lifecycle != "Finalized" || invoice.InvoiceNumber is null || invoice.SellerName is null || invoice.FinalizedAtUtc is null)
            throw new ArgumentException("An issued invoice snapshot is required.");
        var document = new Document();
        document.Info.Title = invoice.InvoiceNumber;
        document.Info.Author = invoice.SellerName;
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = "Elio Sans"; normal.Font.Size = 9; normal.Font.Color = Color.FromRgb(45, 45, 54);
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(5);
        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.TopMargin = Unit.FromCentimeter(1.8);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(1.8);
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromCentimeter(2);
        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Font.Size = 8; footer.Format.Font.Color = Colors.Gray;
        footer.AddText(invoice.InvoiceNumber + "  |  " + invoice.Currency + "  |  Page "); footer.AddPageField();
        footer.AddText(" of "); footer.AddNumPagesField();

        var seller = section.AddParagraph(invoice.SellerName);
        seller.Format.Font.Size = 16; seller.Format.Font.Bold = true; seller.Format.SpaceAfter = Unit.FromPoint(22);
        var heading = section.AddParagraph("Invoice");
        heading.Format.Font.Size = 30; heading.Format.Font.Color = Color.FromRgb(83, 80, 105);
        var number = section.AddParagraph(invoice.InvoiceNumber);
        number.Format.Font.Size = 13; number.Format.Font.Bold = true; number.Format.SpaceAfter = Unit.FromPoint(18);
        section.AddParagraph($"Issued: {invoice.IssueDate:yyyy-MM-dd}  |  Due: {invoice.DueDate:yyyy-MM-dd}  |  Currency: {invoice.Currency}");
        var billTo = section.AddParagraph("BILL TO");
        billTo.Format.Font.Size = 8; billTo.Format.Font.Bold = true; billTo.Format.SpaceBefore = Unit.FromPoint(12);
        section.AddParagraph(invoice.ClientName).Format.Font.Bold = true;
        section.AddParagraph(invoice.ClientEmail);
        if (!string.IsNullOrWhiteSpace(invoice.ClientPhone)) section.AddParagraph(invoice.ClientPhone);
        if (!string.IsNullOrWhiteSpace(invoice.BillingAddress)) section.AddParagraph(BreakLongWords(invoice.BillingAddress));
        section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(8);

        var table = section.AddTable();
        table.Borders.Color = Color.FromRgb(225, 225, 228); table.Borders.Width = 0;
        foreach (var width in new[] { 6.5, 1.8, 4.2, 4.5 }) table.AddColumn(Unit.FromCentimeter(width));
        table.Format.Font.Size = 8;
        var header = table.AddRow(); header.HeadingFormat = true; header.Shading.Color = Color.FromRgb(244, 244, 246);
        header.Format.Font.Bold = true; header.TopPadding = header.BottomPadding = Unit.FromPoint(8);
        var labels = new[] { "Description", "Quantity", "Unit price", "Amount" };
        for (var i = 0; i < 4; i++) { header.Cells[i].AddParagraph(labels[i]); if (i > 0) header.Cells[i].Format.Alignment = ParagraphAlignment.Right; }
        foreach (var line in invoice.Lines.OrderBy(x => x.SortOrder))
        {
            // Bounded continuation rows let even the longest supported description span pages safely.
            var parts = DescriptionParts(line.Description).ToArray();
            for (var part = 0; part < parts.Length; part++)
            {
                var row = table.AddRow(); row.TopPadding = row.BottomPadding = Unit.FromPoint(7);
                row.Borders.Bottom.Width = part == parts.Length - 1 ? 0.5 : 0;
                row.Cells[0].AddParagraph(BreakLongWords(parts[part]));
                if (part == 0)
                {
                    row.Cells[1].AddParagraph(line.Quantity.ToString("0.####", CultureInfo.InvariantCulture));
                    row.Cells[2].AddParagraph(Money(line.UnitPrice, invoice.Currency));
                    row.Cells[3].AddParagraph(Money(line.LineTotal, invoice.Currency));
                }
                for (var i = 1; i < 4; i++) row.Cells[i].Format.Alignment = ParagraphAlignment.Right;
            }
        }
        var subtotal = section.AddParagraph("Subtotal   " + Money(invoice.Subtotal, invoice.Currency));
        subtotal.Format.Alignment = ParagraphAlignment.Right; subtotal.Format.SpaceBefore = Unit.FromPoint(18); subtotal.Format.KeepWithNext = true;
        var total = section.AddParagraph("Total   " + Money(invoice.Total, invoice.Currency) + " " + invoice.Currency);
        total.Format.Alignment = ParagraphAlignment.Right; total.Format.Font.Size = 15; total.Format.Font.Bold = true;
        AddNote(section, "Notes", invoice.Notes);
        AddNote(section, "Payment instructions", invoice.PaymentInstructions);
        var issued = section.AddParagraph("Finalized " + invoice.FinalizedAtUtc.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture));
        issued.Format.SpaceBefore = Unit.FromPoint(22); issued.Format.Font.Size = 8; issued.Format.Font.Color = Colors.Gray;

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        // Stable metadata; fonts/layout depend only on this snapshot and the pinned renderer.
        pdf.Info.CreationDate = pdf.Info.ModificationDate = invoice.FinalizedAtUtc.Value.UtcDateTime;
        pdf.Internals.FirstDocumentID = pdf.Internals.SecondDocumentID = Encoding.Latin1.GetString(invoice.Id.ToByteArray());
        // PDFsharp assigns random subset tags. Keep each embedded face's tag stable across renders.
        foreach (var dictionary in pdf.Internals.GetAllObjects().OfType<PdfDictionary>())
        foreach (var key in new[] { "/BaseFont", "/FontName" })
        {
            var name = dictionary.Elements.GetName(key);
            if (name.Length > 8 && name[7] == '+')
                dictionary.Elements.SetName(key, (name.Contains("Bold", StringComparison.Ordinal) ? "/ELIOBD+" : "/ELIORG+") + name[8..]);
        }
        using var stream = new MemoryStream(); pdf.Save(stream, false);
        var bytes = stream.ToArray();
        SetMetadataId(bytes, "DocumentID", invoice.Id);
        SetMetadataId(bytes, "InstanceID", invoice.Version);
        return bytes;
    }
    private static void SetMetadataId(byte[] bytes, string field, Guid value)
    {
        // PDFsharp 6.2.4 replaces XMP during Save with random UUIDs. Replace only its fixed-size
        // UUID payloads after serialization, preserving stream lengths and PDF cross-reference offsets.
        var prefix = Encoding.ASCII.GetBytes($"<xmpMM:{field}>uuid:");
        var offset = bytes.AsSpan().LastIndexOf(prefix);
        if (offset < 0 || offset + prefix.Length + 36 > bytes.Length)
            throw new InvalidOperationException("The PDF renderer metadata format changed.");
        var target = bytes.AsSpan(offset + prefix.Length, 36);
        if (!Guid.TryParseExact(Encoding.ASCII.GetString(target), "D", out _))
            throw new InvalidOperationException("The PDF renderer metadata identifier is invalid.");
        Encoding.ASCII.GetBytes(value.ToString("D")).CopyTo(target);
    }
    private static string Money(decimal value, string currency) => (currency == "PHP" ? "₱ " : "$ ") + value.ToString("N2", CultureInfo.InvariantCulture);
    private static void AddNote(Section section, string title, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var heading = section.AddParagraph(title); heading.Format.Font.Bold = true;
        heading.Format.SpaceBefore = Unit.FromPoint(18); heading.Format.KeepWithNext = true;
        section.AddParagraph(BreakLongWords(text));
    }
    private static IEnumerable<string> DescriptionParts(string text)
    {
        var part = new StringBuilder(); var newlines = 0;
        foreach (var character in text)
        {
            part.Append(character); if (character == '\n') newlines++;
            if ((part.Length >= 320 && char.IsWhiteSpace(character)) || part.Length >= 400 || newlines >= 6)
            { yield return part.ToString(); part.Clear(); newlines = 0; }
        }
        if (part.Length > 0) yield return part.ToString();
    }
    private static string BreakLongWords(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"\S{28,}",
        match => string.Join("\u200b", Enumerable.Range(0, (match.Length + 19) / 20).Select(i => match.Value.Substring(i * 20, Math.Min(20, match.Length - i * 20)))));
}

internal sealed class InvoiceFontResolver : IFontResolver
{
    public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) => new(bold ? "NotoSans-Bold" : "NotoSans-Regular");
    public byte[] GetFont(string faceName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Elio.Infrastructure.Invoices.Fonts.{faceName}.ttf")
            ?? throw new InvalidOperationException("The bundled invoice font is missing.");
        using var buffer = new MemoryStream(); stream.CopyTo(buffer); return buffer.ToArray();
    }
}
