using System.IO.Compression;
using DocCompressor.Core.Imaging;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using SkiaSharp;

namespace DocCompressor.Core.Pdf;

/// <summary>
/// Compression d'un PDF en recompressant les images qu'il contient.
/// Le texte, les polices et les dessins vectoriels sont conservés.
/// </summary>
internal static class PdfImageRecompressor
{
    // Résolution maximale des images (par rapport à la page) et qualité JPEG minimale tolérée
    private static readonly (int Dpi, int MinQuality)[] Levels =
        [(200, 60), (150, 55), (120, 45), (100, 40), (85, 35), (72, 30)];

    private static readonly int[] Qualities = [85, 78, 70, 62, 55, 48, 40, 34, 30];

    // En dessous, une image ne vaut pas la peine d'être recompressée
    private const int MinImageBytes = 16_000;
    private const long MaxImagePixels = 80_000_000;

    /// <summary>
    /// Image du PDF pouvant être recompressée
    /// </summary>
    private sealed class Candidate
    {
        public required PdfObjectID Id { get; init; }
        public required byte[] RawData { get; init; }
        public required bool IsJpeg { get; init; }
        public required bool IsFlate { get; init; }
        public required int Predictor { get; init; }
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required int Components { get; init; }
        public required double PageLongSide { get; init; }
    }

    /// <summary>
    /// Dimensions et mode (gris ou couleur) retenus pour une image à un niveau de compression donné
    /// </summary>
    private readonly record struct Resized(int Width, int Height, bool Gray);

    /// <summary>
    /// Retourne null si la recompression des images ne suffit pas à atteindre la taille visée
    /// </summary>
    public static CompressionOutcome? TryCompress(byte[] data, long maxBytes, CancellationToken ct)
    {
        List<Candidate> candidates;
        long baseSize;

        // Analyse, et mesure de la taille du document sans les images recompressables
        using (var document = Open(data))
        {
            var images = FindImages(document);
            candidates = images.Select(pair => Analyze(pair.Key, pair.Value.Dictionary, pair.Value.PageLongSide))
                .OfType<Candidate>()
                .ToList();

            foreach (var candidate in candidates)
                images[candidate.Id].Dictionary.Stream.Value = [];

            baseSize = Save(document).Length;
        }

        if (candidates.Count == 0)
        {
            // Pas d'image à recompresser : la réécriture du fichier peut suffire
            var rewritten = Rewrite(data, [], []);
            return rewritten.Length <= maxBytes
                ? new CompressionOutcome(rewritten, ".pdf", CompressionMethod.PdfImagesRecompressed, "PDF optimisé (structure allégée)")
                : null;
        }

        // Marge pour les champs /Length qui s'allongent et les petites variations d'écriture
        long budget = maxBytes - baseSize - 1024 - 16L * candidates.Count;

        foreach (var (dpi, minQuality) in Levels)
        {
            if (budget <= 0)
                return null;

            var planner = new QualityPlanner(Qualities.Where(q => q >= minQuality).ToArray(), budget);
            var resized = new Resized[candidates.Count];

            for (int i = 0; i < candidates.Count && planner.HasFeasibleQuality; i++)
            {
                ct.ThrowIfCancellationRequested();
                var candidate = candidates[i];
                using var bitmap = Decode(candidate);
                if (bitmap == null)
                {
                    planner.AddFixed(candidate.RawData.Length);
                    continue;
                }

                // Résolution plafonnée en supposant que l'image occupe au plus toute la page
                double maxLongSide = candidate.PageLongSide / 72.0 * dpi;
                double scale = Math.Min(1.0, maxLongSide / Math.Max(bitmap.Width, bitmap.Height));
                var (width, height) = ImageOps.Scale(bitmap.Width, bitmap.Height, scale);
                bool gray = candidate.Components == 1 || ImageOps.IsGrayscale(bitmap);
                resized[i] = new Resized(width, height, gray);

                using var scaled = scale < 1 ? ImageOps.Resize(bitmap, width, height) : null;
                using var grayBitmap = gray ? ImageOps.ToGray(scaled ?? bitmap) : null;
                var target = grayBitmap ?? scaled ?? bitmap;
                planner.AddUnit(q =>
                {
                    ct.ThrowIfCancellationRequested();
                    return ImageOps.EncodeJpeg(target, q);
                }, candidate.RawData.Length);
            }

            if (planner.Best() is not var (quality, encodings))
                continue;

            var result = Rewrite(data, candidates, Enumerable.Range(0, candidates.Count)
                .Where(i => encodings[i] != null)
                .Select(i => (candidates[i].Id, encodings[i]!, resized[i]))
                .ToList());

            if (result.Length <= maxBytes && IsReadable(result))
            {
                int count = encodings.Count(e => e != null);
                string images = count > 1 ? $"{count} images recompressées" : "1 image recompressée";
                return new CompressionOutcome(result, ".pdf", CompressionMethod.PdfImagesRecompressed,
                    $"PDF optimisé : {images}, qualité {quality}, {dpi} ppp max (texte conservé)");
            }

            // Estimation trop optimiste : on resserre le budget pour les niveaux suivants
            budget -= Math.Max(1024, result.Length - maxBytes);
        }

        return null;
    }

    private static PdfDocument Open(byte[] data)
    {
        var document = PdfReader.Open(new MemoryStream(data), PdfDocumentOpenMode.Modify);
        document.Options.NoCompression = false;
        document.Options.CompressContentStreams = true;
        document.Options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression;
        document.Options.UseFlateDecoderForJpegImages = PdfUseFlateDecoderForJpegImages.Never;

        // Données inutiles à l'affichage : vignettes de pages et données privées d'applications (Illustrator...)
        foreach (var page in document.Pages)
        {
            page.Elements.Remove("/Thumb");
            page.Elements.Remove("/PieceInfo");
        }
        return document;
    }

    private static byte[] Save(PdfDocument document)
    {
        using var output = new MemoryStream();
        document.Save(output, false);
        return output.ToArray();
    }

    /// <summary>
    /// Réécrit le PDF d'origine en remplaçant les images indiquées par leur version JPEG
    /// </summary>
    private static byte[] Rewrite(byte[] data, List<Candidate> candidates, List<(PdfObjectID Id, byte[] Jpeg, Resized Size)> replacements)
    {
        using var document = Open(data);
        var images = FindImages(document);
        foreach (var (id, jpeg, size) in replacements)
        {
            var dictionary = images[id].Dictionary;
            var elements = dictionary.Elements;
            dictionary.Stream.Value = jpeg;
            elements.SetName("/Filter", "/DCTDecode");
            elements.Remove("/DecodeParms");
            elements.SetInteger("/Width", size.Width);
            elements.SetInteger("/Height", size.Height);
            elements.SetInteger("/BitsPerComponent", 8);
            elements.SetInteger("/Length", jpeg.Length);
            var original = candidates.First(c => c.Id.Equals(id));
            if (size.Gray && original.Components != 1)
                elements.SetName("/ColorSpace", "/DeviceGray");
        }
        return Save(document);
    }

    /// <summary>
    /// Vérifie que le PDF produit s'ouvre correctement avec PDFium
    /// </summary>
    private static bool IsReadable(byte[] pdf)
    {
        try
        {
            using var document = PdfiumDocument.Load(pdf);
            return document.PageCount > 0;
        }
        catch (CompressionException)
        {
            return false;
        }
    }

    /// <summary>
    /// Recense les images utilisées par les pages (y compris dans les formulaires XObject imbriqués)
    /// </summary>
    private static Dictionary<PdfObjectID, (PdfDictionary Dictionary, double PageLongSide)> FindImages(PdfDocument document)
    {
        var images = new Dictionary<PdfObjectID, (PdfDictionary Dictionary, double PageLongSide)>();
        var visitedForms = new HashSet<PdfObjectID>();

        void Visit(PdfDictionary? resources, double pageLongSide)
        {
            var xObjects = resources?.Elements.GetDictionary("/XObject");
            if (xObjects == null)
                return;

            foreach (var key in xObjects.Elements.Keys)
            {
                if (xObjects.Elements[key] is not PdfReference { Value: PdfDictionary dictionary } reference)
                    continue;

                var subtype = dictionary.Elements.GetName("/Subtype");
                if (subtype == "/Image")
                {
                    if (!images.TryGetValue(reference.ObjectID, out var known) || known.PageLongSide < pageLongSide)
                        images[reference.ObjectID] = (dictionary, pageLongSide);
                }
                else if (subtype == "/Form" && visitedForms.Add(reference.ObjectID))
                {
                    Visit(dictionary.Elements.GetDictionary("/Resources"), pageLongSide);
                }
            }
        }

        foreach (var page in document.Pages)
            Visit(page.Elements.GetDictionary("/Resources"), Math.Max(page.Width.Point, page.Height.Point));

        return images;
    }

    /// <summary>
    /// Détermine si une image peut être recompressée sans risque, et en extrait les caractéristiques
    /// </summary>
    private static Candidate? Analyze(PdfObjectID id, PdfDictionary dictionary, double pageLongSide)
    {
        var elements = dictionary.Elements;
        if (elements.GetBoolean("/ImageMask") || elements.ContainsKey("/Mask") || elements.ContainsKey("/Decode"))
            return null;
        if (elements.GetInteger("/BitsPerComponent") != 8)
            return null;

        int width = elements.GetInteger("/Width"), height = elements.GetInteger("/Height");
        if (width <= 0 || height <= 0 || (long)width * height > MaxImagePixels)
            return null;

        // Un masque avec /Matte doit garder les dimensions de l'image
        if (elements.GetDictionary("/SMask")?.Elements.ContainsKey("/Matte") == true)
            return null;

        var raw = dictionary.Stream?.Value;
        if (raw == null || raw.Length < MinImageBytes)
            return null;

        var filter = Resolve(elements["/Filter"]);
        if (filter is PdfArray { Elements.Count: 1 } filters)
            filter = Resolve(filters.Elements[0]);
        string filterName = filter switch
        {
            null => "",
            PdfName name => name.Value,
            _ => "?",
        };
        if (filterName is not ("" or "/FlateDecode" or "/DCTDecode"))
            return null;

        int components = ColorComponents(elements["/ColorSpace"]);
        if (components == 0)
            return null;

        int predictor = 1;
        var parms = Resolve(elements["/DecodeParms"]);
        if (parms is PdfArray { Elements.Count: 1 } parmsArray)
            parms = Resolve(parmsArray.Elements[0]);
        if (filterName == "/FlateDecode" && parms is PdfDictionary parmsDictionary)
        {
            var p = parmsDictionary.Elements;
            predictor = p.ContainsKey("/Predictor") ? p.GetInteger("/Predictor") : 1;
            if (predictor is > 1 and < 10)
                return null; // Prédicteur TIFF : non pris en charge
            if (predictor >= 10 &&
                ((p.ContainsKey("/Colors") ? p.GetInteger("/Colors") : 1) != components ||
                 (p.ContainsKey("/BitsPerComponent") ? p.GetInteger("/BitsPerComponent") : 8) != 8 ||
                 (p.ContainsKey("/Columns") ? p.GetInteger("/Columns") : 1) != width))
                return null;
        }

        return new Candidate
        {
            Id = id,
            RawData = raw,
            IsJpeg = filterName == "/DCTDecode",
            IsFlate = filterName == "/FlateDecode",
            Predictor = predictor,
            Width = width,
            Height = height,
            Components = components,
            PageLongSide = pageLongSide,
        };
    }

    /// <summary>
    /// Nombre de composantes d'un espace colorimétrique pris en charge (gris ou RVB), 0 sinon
    /// </summary>
    private static int ColorComponents(PdfItem? item)
    {
        item = Resolve(item);
        if (item is PdfName name)
        {
            return name.Value switch
            {
                "/DeviceGray" => 1,
                "/DeviceRGB" => 3,
                _ => 0,
            };
        }

        if (item is PdfArray { Elements.Count: 2 } array &&
            Resolve(array.Elements[0]) is PdfName { Value: "/ICCBased" } &&
            Resolve(array.Elements[1]) is PdfDictionary profile)
        {
            int n = profile.Elements.GetInteger("/N");
            return n is 1 or 3 ? n : 0;
        }
        return 0;
    }

    private static PdfItem? Resolve(PdfItem? item) => item is PdfReference reference ? reference.Value : item;

    /// <summary>
    /// Décode une image du PDF en bitmap BGRA ; null si le décodage échoue
    /// </summary>
    private static SKBitmap? Decode(Candidate candidate)
    {
        try
        {
            if (candidate.IsJpeg)
            {
                using var skData = SKData.CreateCopy(candidate.RawData);
                using var codec = SKCodec.Create(skData);
                if (codec == null || codec.Info.Width != candidate.Width || codec.Info.Height != candidate.Height)
                    return null;
                var info = new SKImageInfo(candidate.Width, candidate.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
                var jpeg = new SKBitmap(info);
                var result = codec.GetPixels(info, jpeg.GetPixels());
                if (result is SKCodecResult.Success or SKCodecResult.IncompleteInput)
                    return jpeg;
                jpeg.Dispose();
                return null;
            }

            var pixels = candidate.IsFlate ? Inflate(candidate.RawData) : candidate.RawData;
            if (candidate.Predictor >= 10)
                pixels = PngUnpredict(pixels, candidate.Width * candidate.Components, candidate.Components);

            long expected = (long)candidate.Width * candidate.Height * candidate.Components;
            if (pixels.Length < expected)
                return null;
            return ToBitmap(pixels, candidate.Width, candidate.Height, candidate.Components);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    private static byte[] Inflate(byte[] data)
    {
        using var input = new ZLibStream(new MemoryStream(data), CompressionMode.Decompress);
        using var output = new MemoryStream(data.Length * 4);
        input.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>
    /// Annule les prédicteurs PNG (un octet de type de filtre en tête de chaque ligne)
    /// </summary>
    private static byte[] PngUnpredict(byte[] data, int rowLength, int bytesPerPixel)
    {
        int rows = data.Length / (rowLength + 1);
        var output = new byte[rows * rowLength];
        for (int row = 0; row < rows; row++)
        {
            int src = row * (rowLength + 1);
            int dst = row * rowLength;
            int prev = dst - rowLength;
            byte type = data[src++];
            for (int i = 0; i < rowLength; i++)
            {
                int left = i >= bytesPerPixel ? output[dst + i - bytesPerPixel] : 0;
                int up = row > 0 ? output[prev + i] : 0;
                int upLeft = row > 0 && i >= bytesPerPixel ? output[prev + i - bytesPerPixel] : 0;
                int predicted = type switch
                {
                    1 => left,
                    2 => up,
                    3 => (left + up) / 2,
                    4 => Paeth(left, up, upLeft),
                    _ => 0,
                };
                output[dst + i] = (byte)(data[src + i] + predicted);
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

    private static unsafe SKBitmap ToBitmap(byte[] pixels, int width, int height, int components)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        byte* dst = (byte*)bitmap.GetPixels();
        int rowBytes = bitmap.RowBytes;
        fixed (byte* src = pixels)
        {
            for (int y = 0; y < height; y++)
            {
                byte* s = src + (long)y * width * components;
                byte* d = dst + (long)y * rowBytes;
                for (int x = 0; x < width; x++, d += 4)
                {
                    if (components == 1)
                    {
                        d[0] = d[1] = d[2] = s[x];
                    }
                    else
                    {
                        d[0] = s[x * 3 + 2];
                        d[1] = s[x * 3 + 1];
                        d[2] = s[x * 3];
                    }
                    d[3] = 255;
                }
            }
        }
        return bitmap;
    }
}
