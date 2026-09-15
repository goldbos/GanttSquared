using System.IO;
using System.Text;
using System.Windows.Media.Imaging;

namespace GanttSquared;

/// <summary>
/// Writes a single-page PDF wrapping one JPEG image, with no external PDF library. The chart
/// is already rendered to a RenderTargetBitmap for PNG export; this reuses that bitmap, encodes
/// it as JPEG, and hand-writes the handful of PDF objects (catalog, page, image XObject, and a
/// content stream that paints it) needed for a valid single-page file. A real xref table with
/// exact byte offsets is written rather than a fake/approximate one, so it opens cleanly without
/// relying on a reader's error-recovery pass.
/// </summary>
internal static class PdfExport
{
    public static void WriteSinglePageImagePdf(BitmapSource image, string filePath)
    {
        // JpegBitmapEncoder doesn't accept the Pbgra32 (alpha) format the chart is rendered in;
        // JPEG has no alpha channel anyway, so flatten to opaque Bgr24 first.
        var opaque = new FormatConvertedBitmap(image, System.Windows.Media.PixelFormats.Bgr24, null, 0);
        var jpegEncoder = new JpegBitmapEncoder { QualityLevel = 92 };
        jpegEncoder.Frames.Add(BitmapFrame.Create(opaque));
        using var jpegStream = new MemoryStream();
        jpegEncoder.Save(jpegStream);
        var jpegBytes = jpegStream.ToArray();

        var pixelWidth = image.PixelWidth;
        var pixelHeight = image.PixelHeight;
        // PDF user space is in points (1/72"); the chart was rendered at 96 DPI, so convert
        // pixel dimensions down to points to give the page a sane physical size instead of
        // being ~33% oversized at 1px = 1pt.
        var pageWidth = pixelWidth * 72.0 / 96.0;
        var pageHeight = pixelHeight * 72.0 / 96.0;

        var contentStream = $"q {pageWidth:0.##} 0 0 {pageHeight:0.##} 0 0 cm /Im0 Do Q";
        var contentBytes = Encoding.ASCII.GetBytes(contentStream);

        using var file = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        var offsets = new long[6];

        void WriteAscii(string text) => file.Write(Encoding.ASCII.GetBytes(text));

        WriteAscii("%PDF-1.4\n");

        offsets[1] = file.Position;
        WriteAscii("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        offsets[2] = file.Position;
        WriteAscii("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        offsets[3] = file.Position;
        WriteAscii($"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {pageWidth:0.##} {pageHeight:0.##}] " +
                    "/Resources << /XObject << /Im0 5 0 R >> >> /Contents 4 0 R >>\nendobj\n");

        offsets[4] = file.Position;
        WriteAscii($"4 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n");
        file.Write(contentBytes);
        WriteAscii("\nendstream\nendobj\n");

        offsets[5] = file.Position;
        WriteAscii($"5 0 obj\n<< /Type /XObject /Subtype /Image /Width {pixelWidth} /Height {pixelHeight} " +
                    $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpegBytes.Length} >>\nstream\n");
        file.Write(jpegBytes);
        WriteAscii("\nendstream\nendobj\n");

        var xrefOffset = file.Position;
        WriteAscii("xref\n0 6\n0000000000 65535 f \n");
        for (var i = 1; i <= 5; i++)
            WriteAscii($"{offsets[i]:0000000000} 00000 n \n");

        WriteAscii("trailer\n<< /Size 6 /Root 1 0 R >>\n");
        WriteAscii($"startxref\n{xrefOffset}\n%%EOF");
    }
}
