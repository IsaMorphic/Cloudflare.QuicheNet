using GroupedNativeMethodsGenerator;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Cloudflare.Quiche
{
    [GroupedNativeMethods]
    internal static partial class NativeMethods
    {
        public static string? LibraryDirPath { get; set; }

        static NativeMethods()
        {
            NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), ImportResolver);
        }

        private static IntPtr ImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (libraryName != __DllName)
            {
                return IntPtr.Zero;
            }

            string libraryPath;
            if (LibraryDirPath is null)
            {
                string archName;
                switch (RuntimeInformation.ProcessArchitecture)
                {
                    case Architecture.X64:
                        archName = "x64";
                        break;
                    case Architecture.Arm64:
                        archName = "arm64";
                        break;
                    default:
                        return IntPtr.Zero;
                }

                string rid, libName;
                if (OperatingSystem.IsLinux())
                {
                    rid = "linux-" + archName;
                    libName = $"lib{__DllName}.so";
                }
                else if (OperatingSystem.IsWindows())
                {
                    rid = "win-" + archName;
                    libName = $"{__DllName}.dll";
                }
                else if (OperatingSystem.IsMacOS())
                {
                    rid = "osx-" + archName;
                    libName = $"lib{__DllName}.dylib";
                }
                else
                {
                    return IntPtr.Zero;
                }

                libraryPath = Path.Combine("runtimes", rid, "native", libName);
            }
            else 
            {
                string libName;
                if (OperatingSystem.IsLinux())
                {
                    libName = $"lib{__DllName}.so";
                }
                else if (OperatingSystem.IsWindows())
                {
                    libName = $"{__DllName}.dll";
                }
                else if (OperatingSystem.IsMacOS())
                {
                    libName = $"lib{__DllName}.dylib";
                }
                else
                {
                    return IntPtr.Zero;
                }

                libraryPath = Path.Combine(LibraryDirPath, libName);
            }

            if (NativeLibrary.TryLoad(libraryPath, out IntPtr handle))
            {
                return handle;
            }
            else 
            {
                return IntPtr.Zero;
            }
        }
    }
}
