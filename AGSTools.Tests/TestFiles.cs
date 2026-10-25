namespace AGSTools.Tests;

/// <summary>Creates temp files that are deleted when the test finishes.</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "agstools-tests-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        foreach (string f in Directory.EnumerateFiles(Path))
            System.IO.File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(Path, recursive: true);
    }
}

internal static class TestData
{
    public static string Path(string name) =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "TestData", name);
}
