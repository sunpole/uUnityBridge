using System.Text.Json;

namespace uUnityBridge.Agent;

internal static class DeploymentTransactionTests
{
    internal static int Run()
    {
        string temp = Path.Combine(Path.GetTempPath(),
            "uub-transaction-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            const string profile = "ci-test";
            string current = Path.Combine(temp, "current");
            string stateFile = Path.Combine(temp, "state.json");
            DeploymentState first = new("uub.deploy.state.v1",
                "v1.0.0", new string('a', 64), "R000101");
            DeploymentState second = new("uub.deploy.state.v1",
                "v1.1.0", new string('b', 64), "R000102");
            DeploymentState third = new("uub.deploy.state.v1",
                "v1.2.0", new string('c', 64), "R000103");

            string Candidate(string name, string job)
            {
                string folder = Path.Combine(temp, name);
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "dummy.exe"), name);
                DeploymentTransaction.WriteManagedMarker(folder, profile, job);
                return folder;
            }

            DeploymentTransaction.Install(temp,
                Candidate("candidate-1", first.JobId), first, profile, null);
            Assert(File.Exists(Path.Combine(current, "dummy.exe")), "Initial install failed.");
            Assert(!File.Exists(Path.Combine(temp, ".deploy-pending.json")),
                "Initial journal was not cleared.");

            foreach (string checkpoint in new[] { "old_moved", "new_moved" })
            {
                try
                {
                    DeploymentTransaction.Install(temp,
                        Candidate("candidate-" + checkpoint, second.JobId),
                        second, profile, first, step =>
                        {
                            if (step == checkpoint)
                                throw new IOException("Simulated crash during " + checkpoint);
                        });
                    throw new Exception("Fault injection unexpectedly succeeded.");
                }
                catch (IOException) { }
                DeploymentTransaction.RequireMarker(current, profile, first.JobId);
                Assert(File.ReadAllText(Path.Combine(current, "dummy.exe")) ==
                    "candidate-1", "Previous application was not restored.");
                Assert(!File.Exists(Path.Combine(temp, ".deploy-pending.json")),
                    "Recovery journal survived rollback.");
            }

            DeploymentTransaction.Install(temp,
                Candidate("candidate-2", second.JobId),
                second, profile, first);
            DeploymentTransaction.RequireMarker(current, profile, second.JobId);
            Assert(Directory.EnumerateDirectories(Path.Combine(temp, "backups")).Any(),
                "Successful upgrade did not retain backup.");

            try
            {
                DeploymentTransaction.Install(temp,
                    Candidate("candidate-3", third.JobId),
                    third, profile, second, step =>
                    {
                        if (step == "state_committed")
                            throw new IOException("Simulated crash after state commit");
                    });
                throw new Exception("Post-commit fault injection unexpectedly succeeded.");
            }
            catch (IOException) { }
            DeploymentTransaction.Recover(temp, profile);
            DeploymentTransaction.RequireMarker(current, profile, third.JobId);
            var state = JsonSerializer.Deserialize<DeploymentState>(
                File.ReadAllText(stateFile));
            Assert(state?.JobId == third.JobId,
                "Committed version was incorrectly rolled back.");
            Assert(!File.Exists(Path.Combine(temp, ".deploy-pending.json")),
                "Committed journal was not cleared.");

            File.WriteAllText(Path.Combine(temp, ".deploy-pending.json"),
                "{\"schema\":\"uub.deploy.pending.v1\",\"jobId\":\"R000104\"," +
                "\"backupName\":\"../../escape\"}");
            try
            {
                DeploymentTransaction.Recover(temp, profile);
                throw new Exception("Untrusted journal path was accepted.");
            }
            catch (InvalidDataException) { }
            DeploymentTransaction.RequireMarker(current, profile, third.JobId);
            Console.WriteLine("PASS: initial install, upgrade, backup, interrupted swap, " +
                "post-commit recovery, malicious journal refused.");
            return 0;
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    private static void Assert(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }
}
