using System.IO.Compression;
using System.Text;

namespace uUnityBridge.Agent;

// CI-only filesystem QA: uses a fresh temporary directory, never a user app folder.
internal static class DeploymentSelfTests
{
    internal static int Run()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "uub-deploy-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string good = Path.Combine(root, "good.zip");
            using (var zip = ZipFile.Open(good, ZipArchiveMode.Create))
            {
                var exe = zip.CreateEntry("uUnity.exe");
                using (var writer = new StreamWriter(exe.Open(), Encoding.UTF8))
                    writer.Write("test executable placeholder");
                var data = zip.CreateEntry("uUnity_Data/level0");
                using (var writer = new StreamWriter(data.Open(), Encoding.UTF8))
                    writer.Write("fixture data");
            }
            string goodTarget = Path.Combine(root, "good");
            Directory.CreateDirectory(goodTarget);
            ReleaseDeployment.ExtractSafe(good, goodTarget);
            if (!File.Exists(Path.Combine(goodTarget, "uUnity.exe")) ||
                !File.Exists(Path.Combine(goodTarget, "uUnity_Data", "level0")))
                throw new Exception("Valid archive failed extraction.");

            string bad = Path.Combine(root, "traversal.zip");
            using (var zip = ZipFile.Open(bad, ZipArchiveMode.Create))
                zip.CreateEntry("../escape.exe");
            string badTarget = Path.Combine(root, "bad");
            Directory.CreateDirectory(badTarget);
            try
            {
                ReleaseDeployment.ExtractSafe(bad, badTarget);
                throw new Exception("Traversal was allowed.");
            }
            catch (InvalidDataException) { }
            if (File.Exists(Path.Combine(root, "escape.exe")))
                throw new Exception("Archive escaped deployment staging.");

            string symlink = Path.Combine(root, "symlink.zip");
            using (var zip = ZipFile.Open(symlink, ZipArchiveMode.Create))
            {
                var link = zip.CreateEntry("evil-link");
                link.ExternalAttributes = (0xA000 | 511) << 16;
            }
            try
            {
                ReleaseDeployment.ExtractSafe(symlink, badTarget);
                throw new Exception("Symlink was accepted.");
            }
            catch (InvalidDataException) { }

            string duplicate = Path.Combine(root, "duplicate.zip");
            using (var zip = ZipFile.Open(duplicate, ZipArchiveMode.Create))
            {
                zip.CreateEntry("App.exe");
                zip.CreateEntry("app.exe");
            }
            try
            {
                ReleaseDeployment.ExtractSafe(duplicate, badTarget);
                throw new Exception("Case-colliding names were accepted.");
            }
            catch (InvalidDataException) { }

            Console.WriteLine("PASS: ZIP extraction, traversal, symlink, case collisions (temporary CI folder).");
            return 0;
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
