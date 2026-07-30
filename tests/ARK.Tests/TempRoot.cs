namespace ARK.Tests;

/// <summary>Helper for creating and cleaning up isolated temp directories in tests.</summary>
internal static class TempRoot
{
    public static string Create()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ark-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void Delete(string dir)
    {
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
