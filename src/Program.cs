namespace uUnityBridge;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        // Prevent two current agent instances from applying a task concurrently.
        using var instance = new Mutex(initiallyOwned: true, name: @"Local\uUnityBridge-agent", createdNew: out bool first);
        if (!first)
        {
            MessageBox.Show("uUnityBridge уже запущен. Используйте существующее окно.",
                "uUnityBridge", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
