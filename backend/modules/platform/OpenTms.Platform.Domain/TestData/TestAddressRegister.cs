using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace OpenTms.Platform.TestData;

/// <summary>
/// Reads the embedded register of real addresses (<c>TestData/Addresses/addresses.csv.gz</c>, built by
/// <c>scripts/testdata/build-address-extract.py</c>; sources and licences in <c>SOURCES.md</c>). Loaded per reset
/// run and not cached: it is only needed while a run is busy.
/// </summary>
public class TestAddressRegister : ITransientDependency
{
    public const string ResourceName = "OpenTms.Platform.TestData.addresses.csv.gz";

    public virtual async Task<IReadOnlyList<TestAddress>> LoadAsync()
    {
        await using var resource = typeof(TestAddressRegister).Assembly.GetManifestResourceStream(ResourceName)
                                   ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");
        await using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        return await ParseAsync(reader);
    }

    /// <summary>
    /// Header <c>country,street,house_number,house_number_suffix,postal_code,city,latitude,longitude</c>; fields
    /// may be quoted (RFC 4180) because Belgian and German street names can contain commas.
    /// </summary>
    public static async Task<IReadOnlyList<TestAddress>> ParseAsync(TextReader reader)
    {
        var addresses = new List<TestAddress>(capacity: 330_000);
        var strings = new Dictionary<string, string>(StringComparer.Ordinal); // street and city names repeat a lot
        await reader.ReadLineAsync(); // header

        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (line.Length == 0)
            {
                continue;
            }

            var f = SplitCsv(line);
            if (f.Count < 8)
            {
                continue;
            }

            addresses.Add(new TestAddress(
                Intern(strings, f[0]),
                Intern(strings, f[1]),
                NullIfEmpty(f[2]),
                NullIfEmpty(f[3]),
                f[4],
                Intern(strings, f[5]),
                double.Parse(f[6], CultureInfo.InvariantCulture),
                double.Parse(f[7], CultureInfo.InvariantCulture)));
        }

        return addresses;
    }

    private static string Intern(Dictionary<string, string> pool, string value)
    {
        if (!pool.TryGetValue(value, out var pooled))
        {
            pool[value] = pooled = value;
        }

        return pooled;
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    private static List<string> SplitCsv(string line)
    {
        var fields = new List<string>(8);
        var current = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }
}
