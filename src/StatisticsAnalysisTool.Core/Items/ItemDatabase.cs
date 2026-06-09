using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace StatisticsAnalysisTool.Core.Items;

public sealed class ItemInfo
{
    public required int Index { get; init; }
    public required string UniqueName { get; init; }
    public required string Name { get; init; }
    public int Tier { get; init; }
    public int Enchantment { get; init; }

    /// <summary>Official Albion render-service icon URL for this item.</summary>
    public string IconUrl => $"https://render.albiononline.com/v1/item/{UniqueName}.png";
}

/// <summary>
/// In-memory Albion item index, loaded once from the bundled <c>Data/items.txt</c>
/// (ao-bin-dumps formatted dump: <c>index : uniqueName : English name</c>). Resolves the numeric
/// item indexes seen in loot/gathering events to names + render icons, and supports name search.
/// </summary>
public sealed class ItemDatabase
{
    private static readonly Lazy<ItemDatabase> LazyInstance = new(Load);
    public static ItemDatabase Instance => LazyInstance.Value;

    private readonly Dictionary<int, ItemInfo> _byIndex;
    private readonly Dictionary<string, ItemInfo> _byUnique;
    private readonly List<ItemInfo> _all;

    private ItemDatabase(List<ItemInfo> items)
    {
        _all = items;
        _byIndex = new Dictionary<int, ItemInfo>(items.Count);
        _byUnique = new Dictionary<string, ItemInfo>(items.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            _byIndex[item.Index] = item;
            _byUnique[item.UniqueName] = item;
        }
    }

    public int Count => _all.Count;

    public ItemInfo? ByIndex(int index) => _byIndex.GetValueOrDefault(index);

    public ItemInfo? ByUniqueName(string? uniqueName)
        => string.IsNullOrWhiteSpace(uniqueName) ? null : _byUnique.GetValueOrDefault(uniqueName);

    public IReadOnlyList<ItemInfo> Search(string query, int limit = 50)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<ItemInfo>();
        }

        var q = query.Trim();

        return _all
            .Where(i => i.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || i.UniqueName.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => Rank(i, q))
            .ThenBy(i => i.Name.Length)
            .ThenBy(i => i.Index)
            .Take(limit)
            .ToList();
    }

    private static int Rank(ItemInfo item, string q)
    {
        if (item.Name.Equals(q, StringComparison.OrdinalIgnoreCase)) return 0;
        if (item.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase)) return 1;
        if (item.UniqueName.StartsWith(q, StringComparison.OrdinalIgnoreCase)) return 2;
        return 3;
    }

    private static ItemDatabase Load()
    {
        var assembly = typeof(ItemDatabase).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("items.txt", StringComparison.OrdinalIgnoreCase));

        var items = new List<ItemInfo>();
        if (resourceName is null)
        {
            return new ItemDatabase(items);
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return new ItemDatabase(items);
        }

        using var reader = new StreamReader(stream);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (TryParse(line, out var item))
            {
                items.Add(item);
            }
        }

        return new ItemDatabase(items);
    }

    private static bool TryParse(string line, out ItemInfo item)
    {
        item = null!;

        var firstColon = line.IndexOf(':');
        if (firstColon < 0)
        {
            return false;
        }

        var secondColon = line.IndexOf(':', firstColon + 1);
        if (secondColon < 0)
        {
            return false;
        }

        if (!int.TryParse(line[..firstColon].Trim(), out var index))
        {
            return false;
        }

        var uniqueName = line[(firstColon + 1)..secondColon].Trim();
        if (uniqueName.Length == 0)
        {
            return false;
        }

        var name = line[(secondColon + 1)..].Trim();

        item = new ItemInfo
        {
            Index = index,
            UniqueName = uniqueName,
            Name = name.Length == 0 ? uniqueName : name,
            Tier = ParseTier(uniqueName),
            Enchantment = ParseEnchantment(uniqueName),
        };
        return true;
    }

    private static int ParseTier(string uniqueName)
        => uniqueName.Length >= 2 && uniqueName[0] is 'T' or 't' && char.IsDigit(uniqueName[1])
            ? uniqueName[1] - '0'
            : 0;

    private static int ParseEnchantment(string uniqueName)
    {
        var at = uniqueName.IndexOf('@');
        return at >= 0 && int.TryParse(uniqueName[(at + 1)..], out var e) ? e : 0;
    }
}
