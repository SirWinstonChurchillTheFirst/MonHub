using PokeHub;
using RandoApp;

namespace PokeHub.Tests;

public class SafeFileTests
{
    [Fact]
    public void Write_ReplacesContent_AndLeavesNoTempFile()
    {
        var dir = Directory.CreateTempSubdirectory("pokehub-test-").FullName;
        try
        {
            var path = Path.Combine(dir, "hub.json");
            File.WriteAllText(path, "alt");
            SafeFile.WriteAllText(path, "neu");
            Assert.Equal("neu", File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void FailedWrite_KeepsTheOldFile()
    {
        var dir = Directory.CreateTempSubdirectory("pokehub-test-").FullName;
        try
        {
            var path = Path.Combine(dir, "hub.json");
            File.WriteAllText(path, "alt");
            // the content can't be produced (enumeration throws half way): the target must stay as it was
            static IEnumerable<string> Broken()
            {
                yield return "halb";
                throw new IOException("Datenträger voll");
            }
            Assert.Throws<IOException>(() => SafeFile.WriteAllLines(path, Broken()));
            Assert.Equal("alt", File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ParallelWrites_NeverCollide()
    {
        var dir = Directory.CreateTempSubdirectory("pokehub-test-").FullName;
        try
        {
            var path = Path.Combine(dir, "hub.json");
            var errors = 0;
            Parallel.For(0, 40, i =>
            {
                try { SafeFile.WriteAllText(path, $"Stand {i}"); }
                catch (IOException) { Interlocked.Increment(ref errors); }      // replace while another replaces: allowed to fail …
                catch (UnauthorizedAccessException) { Interlocked.Increment(ref errors); }
            });
            Assert.StartsWith("Stand ", File.ReadAllText(path));               // … but the file is always one whole version
            Assert.Single(Directory.GetFiles(dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
