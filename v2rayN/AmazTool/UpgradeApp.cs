using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace AmazTool;

internal class UpgradeApp
{
    public static void Upgrade(string fileName)
    {
        Console.WriteLine($"{Resx.Resource.StartUnzipping}\n{fileName}");

        Utils.Waiting(5);

        if (!File.Exists(fileName))
        {
            Console.WriteLine(Resx.Resource.UpgradeFileNotFound);
            return;
        }

        Console.WriteLine(Resx.Resource.TryTerminateProcess);
        try
        {
            var existing = Process.GetProcessesByName(Utils.V2rayN);
            foreach (var pp in existing)
            {
                var path = pp.MainModule?.FileName ?? "";
                if (path.StartsWith(Utils.GetPath(Utils.V2rayN)))
                {
                    pp?.Kill();
                    pp?.WaitForExit(1000);
                }
            }
        }
        catch (Exception ex)
        {
            // Access may be denied without admin right. The user may not be an administrator.
            Console.WriteLine(Resx.Resource.FailedTerminateProcess + ex.StackTrace);
        }

        Console.WriteLine(Resx.Resource.StartUnzipping);
        StringBuilder sb = new();
        try
        {
            var thisAppOldFile = $"{Utils.GetExePath()}.tmp";
            File.Delete(thisAppOldFile);

            using var archive = ZipFile.OpenRead(fileName);
            var archiveRoot = GetArchiveRoot(archive);
            foreach (var entry in archive.Entries)
            {
                try
                {
                    if (entry.Length == 0)
                    {
                        continue;
                    }

                    Console.WriteLine(entry.FullName);

                    var fullName = GetRelativeEntryName(entry.FullName, archiveRoot);
                    if (string.IsNullOrEmpty(fullName))
                    {
                        continue;
                    }

                    var entryOutputPath = GetSafeOutputPath(fullName);
                    if (entryOutputPath is null)
                    {
                        sb.AppendLine($"Unsafe archive entry: {entry.FullName}");
                        continue;
                    }

                    if (string.Equals(Utils.GetExePath(), entryOutputPath, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Move(Utils.GetExePath(), thisAppOldFile);
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(entryOutputPath)!);
                    //In the bin folder, if the file already exists, it will be skipped
                    if (fullName.StartsWith("bin/", StringComparison.OrdinalIgnoreCase)
                        && File.Exists(entryOutputPath))
                    {
                        continue;
                    }

                    if (!TryExtractToFile(entry, entryOutputPath))
                    {
                        sb.AppendLine($"Failed to extract: {entry.FullName}");
                        continue;
                    }

                    Console.WriteLine(entryOutputPath);
                }
                catch (Exception ex)
                {
                    sb.Append(ex.StackTrace);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(Resx.Resource.FailedUpgrade + ex.StackTrace);
            //return;
        }
        if (sb.Length > 0)
        {
            Console.WriteLine(Resx.Resource.FailedUpgrade + sb.ToString());
            //return;
        }

        Console.WriteLine(Resx.Resource.Restartv2rayN);
        Utils.Waiting(2);

        Utils.StartV2RayN();
    }

    internal static string? GetArchiveRoot(ZipArchive archive)
    {
        var paths = archive.Entries
            .Where(entry => entry.Length > 0)
            .Select(entry => entry.FullName.Replace('\\', '/').TrimStart('/'))
            .Where(path => !string.IsNullOrEmpty(path))
            .ToList();
        if (paths.Count == 0 || paths.Any(path => !path.Contains('/')))
        {
            return null;
        }

        var roots = paths
            .Select(path => path[..path.IndexOf('/')])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return roots.Count == 1 ? roots[0] : null;
    }

    internal static string GetRelativeEntryName(string entryName, string? archiveRoot)
    {
        var normalized = entryName.Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrEmpty(archiveRoot))
        {
            return normalized;
        }

        var rootPrefix = $"{archiveRoot}/";
        return normalized.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
            ? normalized[rootPrefix.Length..]
            : normalized;
    }

    internal static string? GetSafeOutputPath(string relativePath)
    {
        var startupPath = Path.GetFullPath(Utils.StartupPath());
        var startupPrefix = startupPath.EndsWith(Path.DirectorySeparatorChar)
            ? startupPath
            : startupPath + Path.DirectorySeparatorChar;
        var outputPath = Path.GetFullPath(Path.Combine(startupPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        return outputPath.StartsWith(startupPrefix, StringComparison.OrdinalIgnoreCase)
            ? outputPath
            : null;
    }

    private static bool TryExtractToFile(ZipArchiveEntry entry, string outputPath)
    {
        var retryCount = 5;
        var delayMs = 1000;

        for (var i = 1; i <= retryCount; i++)
        {
            try
            {
                entry.ExtractToFile(outputPath, true);
                return true;
            }
            catch
            {
                Thread.Sleep(delayMs * i);
            }
        }
        return false;
    }
}
