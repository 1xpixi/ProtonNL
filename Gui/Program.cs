namespace ProtonNL.Gui;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using Mutex mutex = new(true, @"Local\ProtonNL.Gui", out bool created);
        if (!created)
        {
            MessageBox.Show("ProtonNL is already open.", "ProtonNL", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
