using System.IO;
using System.IO.Compression;

namespace PokeHub;

/// <summary>
/// The player's own DS BIOS/firmware dumps – MonHub ships none. They are copied to System\BIOS and switched on in
/// melonDS (see <see cref="HubSetup"/>), which then also boots ROMs whose secure area is still encrypted.
/// Without them those ROMs start in DeSmuME.
/// </summary>
public static class Bios
{
    /// <summary>The DS files melonDS uses, with the sizes they have (DSi files are bigger and are skipped).</summary>
    public static readonly (string Name, long[] Sizes)[] Files =
    [
        ("bios7.bin", [16 * 1024]),
        ("bios9.bin", [4 * 1024]),
        ("firmware.bin", [128 * 1024, 256 * 1024, 512 * 1024]),
    ];

    public static string PathOf(string name) => Path.Combine(HubPaths.Bios, name);

    /// <summary>All three files are there – melonDS is set up to use them.</summary>
    public static bool Installed => Files.All(f => File.Exists(PathOf(f.Name)));

    public static List<string> Missing => Files.Where(f => !File.Exists(PathOf(f.Name))).Select(f => f.Name).ToList();

    /// <summary>
    /// Takes bios7.bin, bios9.bin and firmware.bin from the chosen ZIPs and files, recognised by name and size;
    /// files in a "DSi" folder only count when there is no DS one. Returns the names that were taken.
    /// </summary>
    public static List<string> Import(IEnumerable<string> paths)
    {
        var best = new Dictionary<string, (int Rank, Func<byte[]> Read)>(StringComparer.OrdinalIgnoreCase);
        void Consider(string fullName, long size, Func<byte[]> read)
        {
            var spec = Files.FirstOrDefault(f => f.Name.Equals(Path.GetFileName(fullName), StringComparison.OrdinalIgnoreCase));
            if (spec.Name == null || !spec.Sizes.Contains(size)) return;
            int rank = fullName.Contains("dsi", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            if (!best.TryGetValue(spec.Name, out var known) || rank < known.Rank) best[spec.Name] = (rank, read);
        }

        var zips = new List<ZipArchive>();
        try
        {
            foreach (var path in paths)
            {
                if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    var zip = ZipFile.OpenRead(path);
                    zips.Add(zip);
                    foreach (var entry in zip.Entries)
                    {
                        var e = entry;
                        Consider(e.FullName, e.Length, () =>
                        {
                            using var s = e.Open();
                            using var ms = new MemoryStream();
                            s.CopyTo(ms);
                            return ms.ToArray();
                        });
                    }
                }
                else if (File.Exists(path))
                    Consider(path, new FileInfo(path).Length, () => File.ReadAllBytes(path));
            }
            if (best.Count == 0) return [];
            Directory.CreateDirectory(HubPaths.Bios);
            foreach (var (name, pick) in best)
                File.WriteAllBytes(PathOf(name), pick.Read());
            return best.Keys.ToList();
        }
        finally
        {
            foreach (var zip in zips) zip.Dispose();
        }
    }

    public static void Remove()
    {
        foreach (var f in Files)
            if (File.Exists(PathOf(f.Name))) File.Delete(PathOf(f.Name));
    }
}
