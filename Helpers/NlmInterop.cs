using System;
using System.Runtime.InteropServices;

namespace FluentTaskScheduler.Helpers
{
    /// <summary>
    /// Minimal COM interop for the Windows Network List Manager (netlistmgr.dll).
    /// Uses InterfaceIsDual to make direct vtable calls, bypassing IDispatch name lookup
    /// which fails in .NET 8 without a registered type library.
    /// </summary>

    [ComImport]
    [Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B")]
    [ClassInterface(ClassInterfaceType.None)]
    internal class NetworkListManagerClass { }

    [ComImport]
    [Guid("DCB00000-570F-4A9B-8D69-199FDBA5723B")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    internal interface INlmNetworkListManager
    {
        [return: MarshalAs(UnmanagedType.Interface)]
        INlmEnumNetworks GetNetworks([In] int Flags); // NLM_ENUM_NETWORK_ALL = 3

        [return: MarshalAs(UnmanagedType.Interface)]
        INlmNetwork GetNetwork([In] Guid gdNetworkId);

        [return: MarshalAs(UnmanagedType.VariantBool)]
        bool IsConnected { get; } // vtable slot 2

        int GetConnectivity(); // vtable slot 3

        [return: MarshalAs(UnmanagedType.VariantBool)]
        bool IsConnectedToInternet { get; } // vtable slot 4
    }

    [ComImport]
    [Guid("DCB00001-570F-4A9B-8D69-199FDBA5723B")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    internal interface INlmEnumNetworks
    {
        int Count { get; } // vtable slot 0: get_Count

        [DispId(-4)] // DISPID_NEWENUM — vtable slot 1: get__NewEnum
        object NewEnum { get; }

        void GetEnumerator(out object ppEnum); // vtable slot 2

        [return: MarshalAs(UnmanagedType.Interface)]
        INlmNetwork Item([MarshalAs(UnmanagedType.Struct), In] object index); // vtable slot 3
    }

    [ComImport]
    [Guid("DCB00002-570F-4A9B-8D69-199FDBA5723B")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    internal interface INlmNetwork
    {
        [return: MarshalAs(UnmanagedType.BStr)]
        string GetName(); // vtable slot 0

        void SetName([MarshalAs(UnmanagedType.BStr), In] string name); // vtable slot 1

        [return: MarshalAs(UnmanagedType.BStr)]
        string GetDescription(); // vtable slot 2

        void SetDescription([MarshalAs(UnmanagedType.BStr), In] string description); // vtable slot 3

        Guid GetNetworkId(); // vtable slot 4

        // Remaining methods not needed for our usage — declared to maintain vtable correctness
        int GetDomainType(); // vtable slot 5
    }
}
