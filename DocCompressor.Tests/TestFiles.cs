using System.IO.Compression;
using System.Text;
using DocCompressor.Core.Imaging;
using DocCompressor.Core.Pdf;
using PdfSharp.Pdf;
using SkiaSharp;

namespace DocCompressor.Tests;

/// <summary>
/// Génération des fichiers de test (photos, scans, PDF)
/// </summary>
internal static class TestFiles
{
    /// <summary>
    /// Image ressemblant à une photo : dégradés, formes colorées et grain
    /// </summary>
    public static SKBitmap Photo(int width, int height, int seed = 1)
    {
        var random = new Random(seed);
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(bitmap))
        {
            using var gradient = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(width, height),
                    [new SKColor(30, 90, 160), new SKColor(220, 180, 90), new SKColor(40, 140, 60)], SKShaderTileMode.Clamp),
            };
            canvas.DrawRect(0, 0, width, height, gradient);
            using var paint = new SKPaint { IsAntialias = true };
            for (int i = 0; i < 60; i++)
            {
                paint.Color = new SKColor((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 160);
                canvas.DrawCircle(random.Next(width), random.Next(height), random.Next(20, width / 6), paint);
            }
        }
        AddNoise(bitmap, random, 24);
        return bitmap;
    }

    /// <summary>
    /// Page de texte noir sur blanc, numérisée en couleur (bruit neutre)
    /// </summary>
    public static SKBitmap ScannedText(int width, int height, int seed = 1)
    {
        var random = new Random(seed);
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(250, 250, 248));
            using var ink = new SKPaint { Color = new SKColor(20, 20, 20) };
            int lineHeight = Math.Max(8, height / 60);
            for (int y = lineHeight * 3; y < height - lineHeight * 3; y += lineHeight * 2)
            {
                for (int x = width / 12; x < width - width / 12;)
                {
                    int word = random.Next(lineHeight, lineHeight * 6);
                    canvas.DrawRect(x, y, Math.Min(word, width - width / 12 - x), lineHeight, ink);
                    x += word + lineHeight;
                }
            }
        }
        AddNoise(bitmap, random, 16, neutral: true);
        return bitmap;
    }

    /// <summary>
    /// Ajoute du grain (bruit) à une image BGRA ; neutral = même variation sur les trois canaux
    /// </summary>
    public static unsafe void AddNoise(SKBitmap bitmap, Random random, int amplitude, bool neutral = false)
    {
        byte* pixels = (byte*)bitmap.GetPixels();
        for (int y = 0; y < bitmap.Height; y++)
        {
            byte* row = pixels + (long)y * bitmap.RowBytes;
            for (int x = 0; x < bitmap.Width; x++)
            {
                int common = random.Next(-amplitude, amplitude + 1);
                for (int c = 0; c < 3; c++)
                {
                    int delta = neutral ? common : random.Next(-amplitude, amplitude + 1);
                    row[x * 4 + c] = (byte)Math.Clamp(row[x * 4 + c] + delta, 0, 255);
                }
            }
        }
    }

    public static byte[] Jpeg(SKBitmap bitmap, int quality) => ImageOps.EncodeJpeg(bitmap, quality);

    public static byte[] Png(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>
    /// Ajoute un segment EXIF contenant uniquement l'orientation à un JPEG
    /// </summary>
    public static byte[] WithExifOrientation(byte[] jpeg, ushort orientation)
    {
        var tiff = new List<byte>();
        tiff.AddRange("II*\0"u8.ToArray());
        tiff.AddRange(BitConverter.GetBytes(8u));          // décalage du premier IFD
        tiff.AddRange(BitConverter.GetBytes((ushort)1));   // une entrée
        tiff.AddRange(BitConverter.GetBytes((ushort)0x0112));
        tiff.AddRange(BitConverter.GetBytes((ushort)3));   // SHORT
        tiff.AddRange(BitConverter.GetBytes(1u));
        tiff.AddRange(BitConverter.GetBytes(orientation));
        tiff.AddRange(new byte[2]);
        tiff.AddRange(BitConverter.GetBytes(0u));          // pas d'IFD suivant

        var payload = "Exif\0\0"u8.ToArray().Concat(tiff).ToArray();
        int length = payload.Length + 2;
        var segment = new byte[] { 0xFF, 0xE1, (byte)(length >> 8), (byte)length }.Concat(payload);
        return jpeg.Take(2).Concat(segment).Concat(jpeg.Skip(2)).ToArray();
    }

    /// <summary>
    /// PDF « scanné » : une image JPEG par page A4
    /// </summary>
    public static byte[] ScannedPdf(int pages, int dpi, int quality, bool photo = false)
    {
        int width = (int)(8.27 * dpi), height = (int)(11.69 * dpi);
        var list = new List<ImagePage>();
        for (int i = 0; i < pages; i++)
        {
            using var bitmap = photo ? Photo(width, height, i + 1) : ScannedText(width, height, i + 1);
            list.Add(new ImagePage(Jpeg(bitmap, quality), width, height, false, 595.28, 841.89, new PageBox(0, 0, 595.28, 841.89)));
        }
        return ImagePdfWriter.Write(list);
    }

    /// <summary>
    /// PDF avec du texte (police standard) et une grande image RVB compressée en Flate
    /// </summary>
    public static byte[] TextAndFlateImagePdf(SKBitmap image, bool pngPredictor)
    {
        var document = new PdfDocument();
        var page = document.AddPage();

        var font = new PdfDictionary(document);
        font.Elements.SetName("/Type", "/Font");
        font.Elements.SetName("/Subtype", "/Type1");
        font.Elements.SetName("/BaseFont", "/Helvetica");
        document.Internals.AddObject(font);

        var xObject = new PdfDictionary(document);
        xObject.Elements.SetName("/Type", "/XObject");
        xObject.Elements.SetName("/Subtype", "/Image");
        xObject.Elements.SetInteger("/Width", image.Width);
        xObject.Elements.SetInteger("/Height", image.Height);
        xObject.Elements.SetName("/ColorSpace", "/DeviceRGB");
        xObject.Elements.SetInteger("/BitsPerComponent", 8);
        xObject.Elements.SetName("/Filter", "/FlateDecode");
        if (pngPredictor)
        {
            var parms = new PdfDictionary(document);
            parms.Elements.SetInteger("/Predictor", 15);
            parms.Elements.SetInteger("/Colors", 3);
            parms.Elements.SetInteger("/Columns", image.Width);
            xObject.Elements["/DecodeParms"] = parms;
        }
        xObject.CreateStream(Zlib(RgbBytes(image, pngPredictor)));
        document.Internals.AddObject(xObject);

        var fonts = new PdfDictionary(document);
        fonts.Elements.SetReference("/F1", font);
        var xObjects = new PdfDictionary(document);
        xObjects.Elements.SetReference("/Im1", xObject);
        page.Resources.Elements["/Font"] = fonts;
        page.Resources.Elements["/XObject"] = xObjects;

        page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes(
            "BT /F1 24 Tf 72 780 Td (Bonjour DocCompressor) Tj ET q 451 0 0 600 72 120 cm /Im1 Do Q"));

        using var output = new MemoryStream();
        document.Save(output, false);
        return output.ToArray();
    }

    /// <summary>
    /// PDF dont le poids vient de tracés vectoriels (aucune image) : seule la conversion en images peut le réduire
    /// </summary>
    public static byte[] HeavyVectorPdf(int pages)
    {
        var random = new Random(7);
        var document = new PdfDocument();
        for (int p = 0; p < pages; p++)
        {
            var page = document.AddPage();
            var builder = new StringBuilder("0.2 w\n");
            // Traits courts : le rendu reste rapide malgré le poids du fichier
            for (int i = 0; i < 60_000; i++)
            {
                int x = random.Next(10, 585), y = random.Next(10, 830);
                builder.Append($"{x}.{random.Next(100)} {y}.{random.Next(100)} m ");
                builder.Append($"{x + random.Next(-9, 10)}.{random.Next(100)} {y + random.Next(-9, 10)}.{random.Next(100)} l S\n");
            }
            page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes(builder.ToString()));
        }
        document.Options.CompressContentStreams = false;
        document.Options.NoCompression = true;
        using var output = new MemoryStream();
        document.Save(output, false);
        return output.ToArray();
    }

    /// <summary>
    /// Pixels RVB d'une image, avec en option le prédicteur PNG (filtres Sub, Up, Average et Paeth en alternance)
    /// </summary>
    private static unsafe byte[] RgbBytes(SKBitmap bitmap, bool pngPredictor)
    {
        int rowLength = bitmap.Width * 3;
        var raw = new byte[rowLength * bitmap.Height];
        byte* pixels = (byte*)bitmap.GetPixels();
        for (int y = 0; y < bitmap.Height; y++)
        {
            byte* row = pixels + (long)y * bitmap.RowBytes;
            for (int x = 0; x < bitmap.Width; x++)
            {
                raw[y * rowLength + x * 3] = row[x * 4 + 2];
                raw[y * rowLength + x * 3 + 1] = row[x * 4 + 1];
                raw[y * rowLength + x * 3 + 2] = row[x * 4];
            }
        }
        if (!pngPredictor)
            return raw;

        var output = new byte[(rowLength + 1) * bitmap.Height];
        for (int y = 0; y < bitmap.Height; y++)
        {
            byte type = (byte)(y % 5);
            output[y * (rowLength + 1)] = type;
            for (int i = 0; i < rowLength; i++)
            {
                int current = raw[y * rowLength + i];
                int left = i >= 3 ? raw[y * rowLength + i - 3] : 0;
                int up = y > 0 ? raw[(y - 1) * rowLength + i] : 0;
                int upLeft = y > 0 && i >= 3 ? raw[(y - 1) * rowLength + i - 3] : 0;
                int predicted = type switch
                {
                    1 => left,
                    2 => up,
                    3 => (left + up) / 2,
                    4 => Paeth(left, up, upLeft),
                    _ => 0,
                };
                output[y * (rowLength + 1) + 1 + i] = (byte)(current - predicted);
            }
        }
        return output;
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc)
            return a;
        return pb <= pc ? b : c;
    }

    private static byte[] Zlib(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(data);
        return output.ToArray();
    }
}
