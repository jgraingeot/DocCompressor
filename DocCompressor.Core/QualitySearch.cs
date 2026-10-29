namespace DocCompressor.Core;

/// <summary>
/// Recherche de la meilleure qualité JPEG respectant un budget en octets
/// </summary>
internal static class QualitySearch
{
    /// <summary>
    /// Cherche par dichotomie la qualité la plus haute dans [minQuality, maxQuality]
    /// dont le résultat encodé tient dans le budget. Retourne null si même minQuality est trop gros.
    /// </summary>
    public static (int Quality, byte[] Data)? FindHighest(
        Func<int, byte[]> encode, long budget, int minQuality, int maxQuality, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var low = encode(minQuality);
        if (low.Length > budget)
            return null;

        ct.ThrowIfCancellationRequested();
        var high = encode(maxQuality);
        if (high.Length <= budget)
            return (maxQuality, high);

        // Invariant : lo tient dans le budget, hi non
        int lo = minQuality, hi = maxQuality;
        var best = low;
        while (hi - lo > 2)
        {
            ct.ThrowIfCancellationRequested();
            int mid = (lo + hi) / 2;
            var data = encode(mid);
            if (data.Length <= budget)
            {
                lo = mid;
                best = data;
            }
            else
            {
                hi = mid;
            }
        }
        return (lo, best);
    }
}

/// <summary>
/// Choisit une qualité JPEG commune à plusieurs éléments (pages, images d'un PDF)
/// de façon à ce que leur taille cumulée tienne dans un budget.
/// Chaque élément est encodé une seule fois à chaque qualité candidate.
/// </summary>
internal sealed class QualityPlanner
{
    private readonly int[] _qualities;
    private readonly long _budget;
    private readonly long[] _totals;
    private readonly bool[] _feasible;
    // Pour chaque qualité : l'encodage retenu de chaque élément (null = garder l'original)
    private readonly List<byte[]?>[] _encodings;

    /// <param name="qualities">Qualités candidates, de la meilleure à la moins bonne</param>
    /// <param name="budget">Taille cumulée maximale</param>
    public QualityPlanner(IReadOnlyList<int> qualities, long budget)
    {
        _qualities = qualities.ToArray();
        _budget = budget;
        _totals = new long[_qualities.Length];
        _feasible = Enumerable.Repeat(budget > 0, _qualities.Length).ToArray();
        _encodings = _qualities.Select(_ => new List<byte[]?>()).ToArray();
    }

    /// <summary>
    /// Vrai tant qu'au moins une qualité permet encore de respecter le budget
    /// </summary>
    public bool HasFeasibleQuality => _feasible.Any(f => f);

    /// <summary>
    /// Ajoute un élément. Si originalSize est fourni, l'original est conservé
    /// pour chaque qualité où le réencodage ne serait pas plus petit.
    /// </summary>
    public void AddUnit(Func<int, byte[]> encode, long? originalSize = null)
    {
        for (int i = 0; i < _qualities.Length; i++)
        {
            if (!_feasible[i])
                continue;

            var data = encode(_qualities[i]);
            byte[]? chosen = data;
            long size = data.Length;
            if (originalSize is long original && original <= size)
            {
                chosen = null;
                size = original;
            }

            _totals[i] += size;
            if (_totals[i] > _budget)
            {
                _feasible[i] = false;
                _encodings[i].Clear();
            }
            else
            {
                _encodings[i].Add(chosen);
            }
        }
    }

    /// <summary>
    /// Ajoute un élément qui ne peut pas être réencodé (sa taille compte dans le budget)
    /// </summary>
    public void AddFixed(long size)
    {
        for (int i = 0; i < _qualities.Length; i++)
        {
            if (!_feasible[i])
                continue;

            _totals[i] += size;
            if (_totals[i] > _budget)
            {
                _feasible[i] = false;
                _encodings[i].Clear();
            }
            else
            {
                _encodings[i].Add(null);
            }
        }
    }

    /// <summary>
    /// Meilleure qualité respectant le budget, avec l'encodage de chaque élément
    /// </summary>
    public (int Quality, IReadOnlyList<byte[]?> Encodings)? Best()
    {
        for (int i = 0; i < _qualities.Length; i++)
        {
            if (_feasible[i])
                return (_qualities[i], _encodings[i]);
        }
        return null;
    }
}
