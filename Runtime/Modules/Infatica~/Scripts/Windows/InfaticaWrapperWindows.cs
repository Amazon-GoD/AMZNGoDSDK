using System;
using System.Runtime.InteropServices;

namespace InfaticaSDK.Core
{
    public static class InfaticaWrapperWindows
    {
        #region Fields

        private const string DLL_NAME = "infatica_agent";

        #endregion

        #region Methods

        // Declare the functions from the DLL
        [DllImport(DLL_NAME, CallingConvention = CallingConvention.StdCall)]
        private static extern void Start([MarshalAs(UnmanagedType.LPStr)] string partnerID);

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.StdCall)]
        private static extern void Stop();

        [DllImport(DLL_NAME, CallingConvention = CallingConvention.StdCall)]
        private static extern IntPtr GetId();

        // Helper to call GetId as string
        public static string GetAgentId()
        {
            IntPtr ptr = GetId();
            return Marshal.PtrToStringAnsi(ptr);
        }

        // Public wrappers
        public static void StartAgent(string partnerID) => Start(partnerID);
        public static void StopAgent() => Stop();

        #endregion
    }
}

