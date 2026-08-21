using ProtonNL.Hook;

internal class StartupHook
{
    public static void Initialize()
    {
        Entry.Initialize(IntPtr.Zero, 0);
    }
}
