using System.Diagnostics;

namespace ProtonNL.Hook;

internal static class GuiLauncher
{
    public static void Start()
    {
        try
        {
            string? dir = Path.GetDirectoryName(typeof(GuiLauncher).Assembly.Location);
            if (string.IsNullOrWhiteSpace(dir))
                return;

            string exe = Path.Combine(dir, "ProtonNL.Gui.exe");
            if (!File.Exists(exe))
            {
                Logger.Write("ProtonNL.Gui.exe not found next to hook: " + exe);
                return;
            }

            ProcessStartInfo psi = new()
            {
                FileName = exe,
                WorkingDirectory = dir,
                UseShellExecute = true
            };
            Process.Start(psi);
            Logger.Write("started GUI " + exe);
        }
        catch (Exception ex)
        {
            Logger.Write("failed to start GUI: " + ex.Message);
        }
    }
}
