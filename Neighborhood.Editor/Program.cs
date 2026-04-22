namespace Neighborhood.Editor;

/// <summary>
/// Editor.exe entry point -- stub until UI is implemented.
/// </summary>
internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new EditorForm());
    }
}
