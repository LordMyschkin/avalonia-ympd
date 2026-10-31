using System.Runtime.InteropServices;

namespace ModernYmpd;

internal static class MacDockIcon
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjC)] private static extern nint objc_getClass(string name);
    [DllImport(ObjC)] private static extern nint sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern nint Send(nint receiver, nint selector, nint argument);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendVoid(nint receiver, nint selector, nint argument);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void SendVoid(nint receiver, nint selector);

    public static void Apply()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var iconPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Resources", "app.icns"));
        if (!File.Exists(iconPath)) return;

        var path = Marshal.StringToCoTaskMemUTF8(iconPath);
        try
        {
            var name = Send(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), path);
            var image = Send(Send(objc_getClass("NSImage"), sel_registerName("alloc")),
                sel_registerName("initWithContentsOfFile:"), name);
            if (image == 0) return;
            var application = Send(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
            SendVoid(application, sel_registerName("setApplicationIconImage:"), image);
            SendVoid(image, sel_registerName("release"));
        }
        finally { Marshal.FreeCoTaskMem(path); }
    }
}
