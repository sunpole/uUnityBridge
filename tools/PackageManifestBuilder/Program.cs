using System.Text;
using System.Text.Json;
using uUnityBridge;

// CI-only tool, never invoked from a public PATCH or by the installed updater.
// Input directory is the trusted dotnet publish output of the same workflow.
if (args.Length != 4)
{
    Console.Error.WriteLine("Usage: PackageManifestBuilder <publish-dir> <x.y.z> <compact|portable> <output-json>");
    return 2;
}
try
{
    string source = Path.GetFullPath(args[0]);
    string output = Path.GetFullPath(args[3]);
    if (output.StartsWith(source.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
        StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("Manifest output cannot be inside the scanned publish directory.");

    var manifest = PackageFiles.CreateFromDirectory(source, args[1], args[2]);
    var json = JsonSerializer.Serialize(manifest,
        new JsonSerializerOptions { WriteIndented = true });
    PackageFiles.Parse(json);
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    File.WriteAllText(output, json, new UTF8Encoding(false));
    Console.WriteLine($"PASS manifest {manifest.Version} {manifest.Profile}: {manifest.Files.Count} files, " +
        $"{manifest.Files.Sum(f => f.Bytes)} bytes. Output: {output}");
    return 0;
}
catch (Exception e)
{
    Console.Error.WriteLine("FAIL: " + e.Message);
    return 1;
}
