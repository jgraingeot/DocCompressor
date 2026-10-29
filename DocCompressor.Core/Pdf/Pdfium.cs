using System.Runtime.InteropServices;
using SkiaSharp;

namespace DocCompressor.Core.Pdf;

/// <summary>
/// Appels natifs à PDFium (bibliothèque de rendu PDF de Chromium)
/// </summary>
internal static class PdfiumNative
{
    private const string Library = "pdfium";

    public const int FPDF_ANNOT = 0x01;
    public const int FPDFBitmap_BGRA = 4;

    public const int FPDF_ERR_PASSWORD = 4;
    public const int FPDF_ERR_SECURITY = 5;

    /// <summary>
    /// PDFium n'est pas utilisable depuis plusieurs threads à la fois
    /// </summary>
    public static readonly object Sync = new();

    private static bool _initialized;

    public static void EnsureInitialized()
    {
        lock (Sync)
        {
            if (_initialized)
                return;
            FPDF_InitLibrary();
            _initialized = true;
        }
    }

    [DllImport(Library)]
    public static extern void FPDF_InitLibrary();

    [DllImport(Library)]
    public static extern IntPtr FPDF_LoadMemDocument64(IntPtr dataBuffer, UIntPtr size, [MarshalAs(UnmanagedType.LPStr)] string? password);

    [DllImport(Library)]
    public static extern uint FPDF_GetLastError();

    [DllImport(Library)]
    public static extern void FPDF_CloseDocument(IntPtr document);

    [DllImport(Library)]
    public static extern int FPDF_GetPageCount(IntPtr document);

    [DllImport(Library)]
    public static extern IntPtr FPDF_LoadPage(IntPtr document, int pageIndex);

    [DllImport(Library)]
    public static extern void FPDF_ClosePage(IntPtr page);

    [DllImport(Library)]
    public static extern float FPDF_GetPageWidthF(IntPtr page);

    [DllImport(Library)]
    public static extern float FPDF_GetPageHeightF(IntPtr page);

    [DllImport(Library)]
    public static extern IntPtr FPDFBitmap_CreateEx(int width, int height, int format, IntPtr firstScan, int stride);

    [DllImport(Library)]
    public static extern int FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);

    [DllImport(Library)]
    public static extern void FPDFBitmap_Destroy(IntPtr bitmap);

    [DllImport(Library)]
    public static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);
}

/// <summary>
/// Document PDF ouvert avec PDFium, pour le rendu de ses pages en images
/// </summary>
internal sealed class PdfiumDocument : IDisposable
{
    private IntPtr _document;
    private IntPtr _buffer;

    private PdfiumDocument(IntPtr document, IntPtr buffer)
    {
        _document = document;
        _buffer = buffer;
    }

    public static PdfiumDocument Load(byte[] data)
    {
        PdfiumNative.EnsureInitialized();

        // PDFium lit les données au fur et à mesure : elles doivent rester en mémoire non managée
        var buffer = Marshal.AllocHGlobal(data.Length);
        Marshal.Copy(data, 0, buffer, data.Length);

        lock (PdfiumNative.Sync)
        {
            var document = PdfiumNative.FPDF_LoadMemDocument64(buffer, (UIntPtr)data.Length, null);
            if (document != IntPtr.Zero)
                return new PdfiumDocument(document, buffer);

            uint error = PdfiumNative.FPDF_GetLastError();
            Marshal.FreeHGlobal(buffer);
            throw error is PdfiumNative.FPDF_ERR_PASSWORD or PdfiumNative.FPDF_ERR_SECURITY
                ? new CompressionException("Ce PDF est protégé par un mot de passe : retirez la protection avant de le compresser.")
                : new CompressionException("Ce PDF est illisible ou endommagé.");
        }
    }

    public int PageCount
    {
        get
        {
            lock (PdfiumNative.Sync)
                return PdfiumNative.FPDF_GetPageCount(_document);
        }
    }

    /// <summary>
    /// Taille de la page en points (rotation de la page prise en compte)
    /// </summary>
    public (double Width, double Height) GetPageSize(int index)
    {
        lock (PdfiumNative.Sync)
        {
            var page = LoadPage(index);
            try
            {
                return (PdfiumNative.FPDF_GetPageWidthF(page), PdfiumNative.FPDF_GetPageHeightF(page));
            }
            finally
            {
                PdfiumNative.FPDF_ClosePage(page);
            }
        }
    }

    /// <summary>
    /// Rend une page sur fond blanc à la taille demandée, en pixels
    /// </summary>
    public SKBitmap RenderPage(int index, int width, int height)
    {
        // Le fond est rempli de blanc opaque avant le rendu : l'image résultante est opaque
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        lock (PdfiumNative.Sync)
        {
            var page = LoadPage(index);
            var pdfBitmap = PdfiumNative.FPDFBitmap_CreateEx(width, height, PdfiumNative.FPDFBitmap_BGRA, bitmap.GetPixels(), bitmap.RowBytes);
            try
            {
                if (pdfBitmap == IntPtr.Zero)
                    throw new CompressionException("Mémoire insuffisante pour convertir la page en image.");
                PdfiumNative.FPDFBitmap_FillRect(pdfBitmap, 0, 0, width, height, 0xFFFFFFFF);
                PdfiumNative.FPDF_RenderPageBitmap(pdfBitmap, page, 0, 0, width, height, 0, PdfiumNative.FPDF_ANNOT);
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
            finally
            {
                if (pdfBitmap != IntPtr.Zero)
                    PdfiumNative.FPDFBitmap_Destroy(pdfBitmap);
                PdfiumNative.FPDF_ClosePage(page);
            }
        }
        return bitmap;
    }

    private IntPtr LoadPage(int index)
    {
        var page = PdfiumNative.FPDF_LoadPage(_document, index);
        if (page == IntPtr.Zero)
            throw new CompressionException($"Impossible de lire la page {index + 1} du PDF.");
        return page;
    }

    public void Dispose()
    {
        lock (PdfiumNative.Sync)
        {
            if (_document != IntPtr.Zero)
            {
                PdfiumNative.FPDF_CloseDocument(_document);
                _document = IntPtr.Zero;
            }
        }
        if (_buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
        }
    }
}
