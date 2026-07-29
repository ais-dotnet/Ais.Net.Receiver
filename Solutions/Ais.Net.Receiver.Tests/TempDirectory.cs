namespace Ais.Net.Receiver.Tests;

/// <summary>
/// Creates a unique temporary directory and deletes it (best effort) on disposal.
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory() =>
        this.Path = Directory.CreateDirectory(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ais-test-" + Guid.NewGuid().ToString("N"))).FullName;

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(this.Path))
            {
                Directory.Delete(this.Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; a leftover temp directory is harmless.
        }
    }
}
