using System;
using System.Runtime.InteropServices;

namespace subphimv1.SoundTouch.NativeLibraryLoader
{
    public class LibraryLoader
    {
        public static IntPtr LoadNativeLibrary(string name)
        {
            return CoreLoadNativeLibrary(name);
        }

        public void FreeNativeLibrary(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
            {
                throw new ArgumentException("Parameter must not be zero.", nameof(handle));
            }

            CoreFreeNativeLibrary(handle);
        }
        public static void CoreFreeNativeLibrary(IntPtr handle)
        {
            FreeLibrary(handle);
        }

        public static IntPtr CoreLoadFunctionPointer(IntPtr handle, string functionName)
        {
            return GetProcAddress(handle, functionName);
        }

        public static IntPtr CoreLoadNativeLibrary(string name)
        {
            return LoadLibrary(name);
        }


        [DllImport("kernel32")]
        public static extern IntPtr LoadLibrary(string fileName);

        [DllImport("kernel32")]
        public static extern IntPtr GetProcAddress(IntPtr module, string procName);

        [DllImport("kernel32")]
        public static extern int FreeLibrary(IntPtr module);
    }
}
