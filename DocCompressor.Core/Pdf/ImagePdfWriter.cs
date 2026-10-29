using System.Globalization;
using System.Text;

namespace DocCompressor.Core.Pdf;

/// <summary>
/// Page d'un PDF composé uniquement d'images JPEG
/// </summary>
/// <param name="Jpeg">Image encodée en JPEG</param>
/// <param name="PixelWidth">Largeur de l'image en pixels</param>
/// <param name="PixelHeight">Hauteur de l'image en pixels</param>
/// <param name="Gray">Vrai si le JPEG est en niveaux de gris</param>
/// <param name="PageWidth">Largeur de la page en points (1/72 de pouce)</param>
/// <param name="PageHeight">Hauteur de la page en points</param>
/// <param name="ImageBox">Position et taille de l'image sur la page, en points (origine en bas à gauche)</param>
internal sealed record ImagePage(
    byte[] Jpeg, int PixelWidth, int PixelHeight, bool Gray,
    double PageWidth, double PageHeight, PageBox ImageBox);

/// <summary>
/// Écriture minimale d'un PDF dont chaque page est une image JPEG
/// </summary>
internal static class ImagePdfWriter
{
    /// <summary>
    /// Majoration de la taille du PDF par rapport à la somme des JPEG (structure du fichier)
    /// </summary>
    public static long EstimateOverhead(int pageCount) => 600 + 450L * pageCount;

    /// <summary>
    /// Place une image sur une page A4 (portrait ou paysage selon l'image), centrée avec 1 cm de marge
    /// </summary>
    public static byte[] WriteImageOnA4(byte[] jpeg, int pixelWidth, int pixelHeight, bool gray)
    {
        var (pageWidth, pageHeight, box) = PageLayout.Single(pixelWidth, pixelHeight);
        return Write([new ImagePage(jpeg, pixelWidth, pixelHeight, gray, pageWidth, pageHeight, box)]);
    }

    /// <summary>
    /// Écrit un PDF dont chaque page contient une image
    /// </summary>
    public static byte[] Write(IReadOnlyList<ImagePage> pages)
    {
        // Objets : 1 catalogue, 2 arbre des pages, 3 informations, puis 3 objets par page (page, contenu, image)
        int objectCount = 3 + 3 * pages.Count;
        var offsets = new long[objectCount + 1];
        using var output = new MemoryStream(pages.Sum(p => p.Jpeg.Length) + (int)EstimateOverhead(pages.Count));

        void Ascii(string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            output.Write(bytes, 0, bytes.Length);
        }

        void BeginObject(int number)
        {
            offsets[number] = output.Position;
            Ascii($"{number} 0 obj\n");
        }

        Ascii("%PDF-1.4\n");
        output.Write([0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A]); // commentaire binaire : signale un fichier binaire

        BeginObject(1);
        Ascii("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        BeginObject(2);
        var kids = string.Join(" ", Enumerable.Range(0, pages.Count).Select(i => $"{PageObject(i)} 0 R"));
        Ascii($"<< /Type /Pages /Kids [{kids}] /Count {pages.Count} >>\nendobj\n");

        BeginObject(3);
        Ascii("<< /Producer (DocCompressor) >>\nendobj\n");

        for (int i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            int pageObj = PageObject(i), contentObj = pageObj + 1, imageObj = pageObj + 2;

            BeginObject(pageObj);
            Ascii($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Num(page.PageWidth)} {Num(page.PageHeight)}] " +
                  $"/Resources << /XObject << /Im0 {imageObj} 0 R >> >> /Contents {contentObj} 0 R >>\nendobj\n");

            var (x, y, w, h) = page.ImageBox;
            var content = Encoding.ASCII.GetBytes($"q {Num(w)} 0 0 {Num(h)} {Num(x)} {Num(y)} cm /Im0 Do Q");
            BeginObject(contentObj);
            Ascii($"<< /Length {content.Length} >>\nstream\n");
            output.Write(content);
            Ascii("\nendstream\nendobj\n");

            BeginObject(imageObj);
            Ascii($"<< /Type /XObject /Subtype /Image /Width {page.PixelWidth} /Height {page.PixelHeight} " +
                  $"/ColorSpace /{(page.Gray ? "DeviceGray" : "DeviceRGB")} /BitsPerComponent 8 " +
                  $"/Filter /DCTDecode /Length {page.Jpeg.Length} >>\nstream\n");
            output.Write(page.Jpeg);
            Ascii("\nendstream\nendobj\n");
        }

        long xref = output.Position;
        var table = new StringBuilder();
        table.Append($"xref\n0 {objectCount + 1}\n0000000000 65535 f \n");
        for (int i = 1; i <= objectCount; i++)
            table.Append(offsets[i].ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        table.Append($"trailer\n<< /Size {objectCount + 1} /Root 1 0 R /Info 3 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        Ascii(table.ToString());

        return output.ToArray();
    }

    private static int PageObject(int pageIndex) => 4 + 3 * pageIndex;

    private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
