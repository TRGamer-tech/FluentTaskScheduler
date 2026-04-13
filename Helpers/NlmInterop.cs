using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace FluentTaskScheduler.Helpers
{
    /// <summary>
    /// Enumerates Windows Network List Manager (NLM) network profiles via direct COM vtable calls.
    /// Bypasses .NET's QI-based COM interop which fails for NLM in .NET 8 without a registered type library.
    /// </summary>
    internal static class NlmNetworkEnumerator
    {
        // All NLM interfaces inherit from IDispatch (which inherits IUnknown).
        // vtable layout: 3x IUnknown + 4x IDispatch = 7 slots before the first custom method.
        private const int SlotBase = 7;

        // INetworkListManager vtable (custom methods start at slot 7)
        // Slot 7: GetNetworks, Slot 8: GetNetwork, Slot 9: get_IsConnected, ...
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetNetworksDelegate(IntPtr @this, int Flags, out IntPtr ppEnum);

        // IEnumNetworks vtable (custom methods start at slot 7)
        // Slot 7: get_Count, Slot 8: get__NewEnum, Slot 9: GetEnumerator, Slot 10: Item
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetCountDelegate(IntPtr @this, out int pVal);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetNewEnumDelegate(IntPtr @this, out IntPtr ppEnumVar);

        // INetwork vtable (custom methods start at slot 7)
        // Slot 7: GetName, Slot 8: SetName, Slot 9: GetDescription, Slot 10: SetDescription, Slot 11: GetNetworkId
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetNameDelegate(IntPtr @this, [MarshalAs(UnmanagedType.BStr)] out string pszName);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetNameDelegate(IntPtr @this, [MarshalAs(UnmanagedType.BStr)] string szName);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetDescriptionDelegate(IntPtr @this, [MarshalAs(UnmanagedType.BStr)] out string pszDesc);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetDescriptionDelegate(IntPtr @this, [MarshalAs(UnmanagedType.BStr)] string szDesc);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetNetworkIdDelegate(IntPtr @this, out Guid pgdGuid);

        // IEnumVARIANT (inherits IUnknown only, custom methods at slot 3)
        // Slot 3: Next, Slot 4: Skip, Slot 5: Reset, Slot 6: Clone
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int EnumVarNextDelegate(IntPtr @this, int celt, IntPtr rgVar, out int pceltFetched);

        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance(
            ref Guid rclsid, IntPtr pUnkOuter, int dwClsContext, ref Guid riid, out IntPtr ppv);

        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr pvReserved, int dwCoInit);

        private static T GetVtableMethod<T>(IntPtr comPtr, int slotIndex) where T : Delegate
        {
            IntPtr vtable = Marshal.ReadIntPtr(comPtr);
            IntPtr slot = Marshal.ReadIntPtr(vtable, slotIndex * IntPtr.Size);
            return Marshal.GetDelegateForFunctionPointer<T>(slot);
        }

        public static List<(string Name, string Id)> GetNetworkProfiles()
        {
            var result = new List<(string, string)>();

            var clsid = new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"); // NetworkListManager
            var riid  = new Guid("DCB00000-570F-4A9B-8D69-199FDBA5723B"); // INetworkListManager

            // CLSCTX_INPROC_SERVER | CLSCTX_LOCAL_SERVER = 5
            int hr = CoCreateInstance(ref clsid, IntPtr.Zero, 5, ref riid, out IntPtr nlmPtr);
            if (hr != 0 || nlmPtr == IntPtr.Zero) return result;

            IntPtr enumPtr = IntPtr.Zero;
            try
            {
                // INetworkListManager::GetNetworks — slot 7
                hr = GetVtableMethod<GetNetworksDelegate>(nlmPtr, SlotBase)(nlmPtr, 3 /* NLM_ENUM_NETWORK_ALL */, out enumPtr);
                if (hr != 0 || enumPtr == IntPtr.Zero) return result;

                // IEnumNetworks::get_Count — slot 7
                hr = GetVtableMethod<GetCountDelegate>(enumPtr, SlotBase)(enumPtr, out int count);
                if (hr != 0 || count == 0) return result;

                // IEnumNetworks::get__NewEnum — slot 8 → returns IEnumVARIANT
                hr = GetVtableMethod<GetNewEnumDelegate>(enumPtr, SlotBase + 1)(enumPtr, out IntPtr enumVarPtr);
                if (hr != 0 || enumVarPtr == IntPtr.Zero) return result;

                try
                {
                    // VARIANT is 16 bytes; allocate one at a time
                    int varSize = 16;
                    IntPtr varBuf = Marshal.AllocCoTaskMem(varSize);

                    try
                    {
                        var enumNext = GetVtableMethod<EnumVarNextDelegate>(enumVarPtr, 3); // IEnumVARIANT::Next at slot 3

                        for (int i = 0; i < count; i++)
                        {
                            // Zero the VARIANT buffer
                            for (int b = 0; b < varSize; b++)
                                Marshal.WriteByte(varBuf, b, 0);

                            hr = enumNext(enumVarPtr, 1, varBuf, out int fetched);
                            if (hr != 0 || fetched == 0) break;

                            // VARIANT layout: vt (2 bytes) at offset 0, then reserved (6 bytes), then value (8 bytes)
                            // For VT_DISPATCH (9) or VT_UNKNOWN (13), the value is a COM pointer at offset 8
                            short vt = Marshal.ReadInt16(varBuf, 0);
                            IntPtr netPtr = Marshal.ReadIntPtr(varBuf, 8);

                            if (netPtr == IntPtr.Zero) continue;

                            try
                            {
                                // INetwork::GetName — slot 7
                                hr = GetVtableMethod<GetNameDelegate>(netPtr, SlotBase)(netPtr, out string netName);
                                if (hr != 0) continue;

                                // INetwork::GetNetworkId — slot 11 (SlotBase + 4)
                                hr = GetVtableMethod<GetNetworkIdDelegate>(netPtr, SlotBase + 4)(netPtr, out Guid netId);
                                if (hr != 0) continue;

                                if (!string.IsNullOrWhiteSpace(netName))
                                    result.Add((netName, netId.ToString("B").ToUpperInvariant()));
                            }
                            finally
                            {
                                // Release the COM pointer inside the VARIANT
                                Marshal.Release(netPtr);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(varBuf);
                    }
                }
                finally
                {
                    Marshal.Release(enumVarPtr);
                }
            }
            finally
            {
                if (enumPtr != IntPtr.Zero) Marshal.Release(enumPtr);
                Marshal.Release(nlmPtr);
            }

            return result;
        }
    }
}
