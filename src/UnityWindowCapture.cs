using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace uUnityBridge;

// LOCAL QA ONLY: renders one explicitly approved Unity Editor top-level window.
// PrintWindow targets a validated HWND, not the desktop or another application.
// Nothing is uploaded or written to a public GitHub issue.
internal static class UnityWindowCapture
{
    private const string ExpectedVersion = "6000.6.7f1";
    private const int MaxDimension = 4096;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect bounds);

    internal static string CaptureAndPreview(IWin32Window owner)
    {
        if (Environment.GetEnvironmentVariable("UUB_UNITY_QA") != "1")
            return "BLOCKED: first capture is permitted only in isolated UNITY QA.";
        string approvalFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "uUnityBridge", "unity-approval-qa.json");
        if (!File.Exists(approvalFile) || new FileInfo(approvalFile).Length > 4096)
            return "BLOCKED: grant the Unity folder in QA GUI first.";

        using (var consent = JsonDocument.Parse(File.ReadAllText(approvalFile)))
        {
            var json = consent.RootElement;
            if (json.GetProperty("schema").GetString() != "uub.unity.approval.v1" ||
                json.GetProperty("enabled").ValueKind != JsonValueKind.True ||
                json.GetProperty("projectName").GetString() != "uUnityDemo" ||
                json.GetProperty("editorVersion").GetString() != ExpectedVersion)
                return "BLOCKED: wrong or revoked local Unity permission.";
        }

        Process? selected = null;
        IntPtr hwnd = IntPtr.Zero;
        foreach (var p in Process.GetProcessesByName("Unity"))
        {
            try
            {
                string title = p.MainWindowTitle;
                string executable = p.MainModule?.FileName ?? "";
                bool validEditor = executable.EndsWith(
                    @"\Editor\Unity.exe", StringComparison.OrdinalIgnoreCase) &&
                    executable.Contains(ExpectedVersion, StringComparison.OrdinalIgnoreCase);
                if (!title.Contains("uUnityDemo", StringComparison.OrdinalIgnoreCase) ||
                    !validEditor || p.MainWindowHandle == IntPtr.Zero ||
                    !IsWindowVisible(p.MainWindowHandle) || IsIconic(p.MainWindowHandle))
                    continue;
                if (selected is not null)
                    return "BLOCKED: multiple Unity windows match the project.";
                selected = p;
                hwnd = p.MainWindowHandle;
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or
                InvalidOperationException or UnauthorizedAccessException)
            {
                // Cannot authenticate the executable path: fail this candidate.
            }
        }

        if (selected is null || hwnd == IntPtr.Zero)
            return "BLOCKED: the visible, non-minimized Unity 6.6 uUnityDemo window was not found.";
        if (!GetWindowRect(hwnd, out var rect))
            return "BLOCKED: unable to read the approved window size.";
        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width < 120 || height < 120 || width > MaxDimension || height > MaxDimension)
            return "BLOCKED: invalid Unity window dimensions.";

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            IntPtr hdc = graphics.GetHdc();
            try
            {
                if (!PrintWindow(hwnd, hdc, 2))
                    return "BLOCKED: Unity window capture was unavailable.";
            }
            finally { graphics.ReleaseHdc(hdc); }
        }

        // A local, named QA folder; never send to a public repository.
        string destination = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "uUnityBridge-QA", "captures");
        Directory.CreateDirectory(destination);
        string final = Path.Combine(destination, "latest-unity-window.png");
        string staging = final + ".next";
        if (File.Exists(staging))
            return "BLOCKED: a previous capture is still being saved.";
        bitmap.Save(staging, ImageFormat.Png);
        File.Move(staging, final, overwrite: true);
        string sha;
        using (var input = File.OpenRead(final))
            sha = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();

        using var preview = new Form
        {
            Text = "uUnityBridge · PRIVATE Unity QA screenshot",
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(Math.Min(width + 45, 1100), Math.Min(height + 90, 760)),
            MinimumSize = new Size(520, 360)
        };
        var picture = new PictureBox
        {
            Image = new Bitmap(bitmap), Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(30, 35, 45)
        };
        preview.Controls.Add(picture);
        try { preview.ShowDialog(owner); }
        finally { picture.Image?.Dispose(); }
        return $"LOCAL_ONLY: Unity window captured, {width}x{height}, SHA256 {sha[..16]}… " +
            "Preview opened. Not delivered to ChatGPT.";
    }
}
