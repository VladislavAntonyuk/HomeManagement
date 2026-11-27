using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;

namespace HomeManagement.Client;

public partial class Program : GtkMauiApplication
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public static void Main(string[] args)
    {
        if (OperatingSystem.IsWindows())
        {
            UseWindowsGtk();
        }

        var app = new Program();
        app.Run(args);
    }

    private static void UseWindowsGtk()
    {
        var gtkDirectory = Environment.GetEnvironmentVariable("GTK4_BIN") ?? throw new InvalidOperationException("Set GTK4_BIN to the GTK4 bin folder");
        SetEnvironmentVariable("PATH", $"{gtkDirectory};{Environment.GetEnvironmentVariable("PATH")}");
        SetEnvironmentVariable("PANGOCAIRO_BACKEND", "fc");

        var libraries = new Dictionary<string, string>
        {
            ["libgtk-4.so.1"] = "libgtk-4-1.dll",
            ["libcairo.so.2"] = "libcairo-2.dll",
            ["libfontconfig.so.1"] = "libfontconfig-1.dll",
            ["libpangocairo-1.0.so.0"] = "libpangocairo-1.0-0.dll",
            ["libpangoft2-1.0.so.0"] = "libpangoft2-1.0-0.dll",
        };

        AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, name) =>
            libraries.TryGetValue(name, out var library)
                ? NativeLibrary.Load(Path.Combine(gtkDirectory, library))
                : IntPtr.Zero;
    }

    private static void SetEnvironmentVariable(string name, string value)
    {
        if (PutEnvironmentVariable(name, value) != 0)
        {
            throw new InvalidOperationException($"Could not set {name}.");
        }
    }

    [LibraryImport("ucrtbase", EntryPoint = "_wputenv_s", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int PutEnvironmentVariable(string name, string value);
}
