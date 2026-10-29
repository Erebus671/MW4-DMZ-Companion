namespace DmzCompanion.Core.Tests;

internal static class TestFiles
{
    public static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
