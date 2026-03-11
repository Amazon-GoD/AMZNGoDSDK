using System;
using System.Runtime.InteropServices;

public static class InfaticaWrapperMac
{
    #region Fields

    private const string LibName = "infatica_sdk";

    #endregion

    #region Methods

    // C signatures from the cgo header:
    [DllImport(LibName, EntryPoint = "AgentStart", CallingConvention = CallingConvention.Cdecl)]
    private static extern void AgentStartNative(IntPtr partnerIdUtf8);

    [DllImport(LibName, EntryPoint = "AgentStop", CallingConvention = CallingConvention.Cdecl)]
    private static extern void AgentStopNative();

    [DllImport(LibName, EntryPoint = "AgentGetId", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr AgentGetIdNative();

    [DllImport(LibName, EntryPoint = "AgentFreeCString", CallingConvention = CallingConvention.Cdecl)]
    private static extern void AgentFreeCStringNative(IntPtr ptr);

    // Helper to call AgentStart with a managed string (UTF8)
    public static void StartAgent(string partnerId)
    {
        if (partnerId == null) partnerId = string.Empty;
        // Marshal to native UTF8 (C string)
        IntPtr utf8 = StringToHGlobalUtf8(partnerId);
        try
        {
            AgentStartNative(utf8);
        }
        finally
        {
            Marshal.FreeHGlobal(utf8);
        }
    }

    public static void StopAgent()
    {
        AgentStopNative();
    }

    public static string GetAgentId()
    {
        IntPtr p = AgentGetIdNative();
        if (p == IntPtr.Zero) return null;
        try
        {
            string s = PtrToStringUtf8(p);
            return s;
        }
        finally
        {
            // free the CString returned by native code
            AgentFreeCStringNative(p);
        }
    }

    // ----------------- helpers -----------------
    // Marshal a managed string to native UTF8 (malloc'ed via Marshal.AllocHGlobal)
    private static IntPtr StringToHGlobalUtf8(string managed)
    {
        if (managed == null) return IntPtr.Zero;
        // get UTF8 bytes including null terminator
        byte[] utf8Bytes = System.Text.Encoding.UTF8.GetBytes(managed);
        IntPtr native = Marshal.AllocHGlobal(utf8Bytes.Length + 1);
        Marshal.Copy(utf8Bytes, 0, native, utf8Bytes.Length);
        Marshal.WriteByte(native, utf8Bytes.Length, 0); // null terminator
        return native;
    }

    // Convert native UTF8 C string pointer to managed string
    private static string PtrToStringUtf8(IntPtr nativeUtf8)
    {
        if (nativeUtf8 == IntPtr.Zero) return null;
        // find length
        int len = 0;
        while (Marshal.ReadByte(nativeUtf8, len) != 0) ++len;
        if (len == 0) return string.Empty;

        byte[] buffer = new byte[len];
        Marshal.Copy(nativeUtf8, buffer, 0, len);
        return System.Text.Encoding.UTF8.GetString(buffer);
    }

    #endregion
}
