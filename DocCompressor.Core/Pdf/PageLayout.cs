namespace DocCompressor.Core.Pdf;

/// <summary>
/// Position d'une image sur une page, en points (origine en bas à gauche)
/// </summary>
internal readonly record struct PageBox(double X, double Y, double Width, double Height);

/// <summary>
/// Mise en page des images sur des pages A4, avec 1 cm de marge
/// </summary>
internal static class PageLayout
{
    public const double A4Width = 595.28;
    public const double A4Height = 841.89;
    private const double Margin = 28.35; // 1 cm

    /// <summary>
    /// Une image par page : A4 portrait ou paysage selon l'image, image centrée
    /// </summary>
    public static (double PageWidth, double PageHeight, PageBox Box) Single(int pixelWidth, int pixelHeight)
    {
        bool landscape = pixelWidth > pixelHeight;
        double pageWidth = landscape ? A4Height : A4Width;
        double pageHeight = landscape ? A4Width : A4Height;
        var area = new PageBox(Margin, Margin, pageWidth - 2 * Margin, pageHeight - 2 * Margin);
        return (pageWidth, pageHeight, Fit(pixelWidth, pixelHeight, area));
    }

    /// <summary>
    /// Deux images sur une page A4 portrait, l'une au-dessus de l'autre (recto et verso d'une carte)
    /// </summary>
    public static PageBox[] Pair(IReadOnlyList<(int Width, int Height)> pixelSizes)
    {
        double slotWidth = A4Width - 2 * Margin;
        double slotHeight = (A4Height - 3 * Margin) / 2;
        var top = new PageBox(Margin, A4Height - Margin - slotHeight, slotWidth, slotHeight);
        var bottom = new PageBox(Margin, Margin, slotWidth, slotHeight);
        return pixelSizes.Select((size, i) => Fit(size.Width, size.Height, i == 0 ? top : bottom)).ToArray();
    }

    /// <summary>
    /// Plus grand rectangle de mêmes proportions que l'image, centré dans la zone
    /// </summary>
    private static PageBox Fit(int pixelWidth, int pixelHeight, PageBox area)
    {
        double scale = Math.Min(area.Width / pixelWidth, area.Height / pixelHeight);
        double width = pixelWidth * scale, height = pixelHeight * scale;
        return new PageBox(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height);
    }
}
