using System;
using System.Runtime.InteropServices;
using UnityEngine;

public static class WindowsAudioRouter
{
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    const int Render = 0, Active = 1;
    static readonly string[] savedEndpoints = new string[3];
    static readonly bool[] changedRoles = new bool[3];
    static string questEndpoint;
    static bool hasSnapshot;
    static WindowsAudioRouteMonitor monitor;
    static bool probeWarningLogged;

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, int mask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int context, IntPtr parameters, out IntPtr instance);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }
    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }
    [StructLayout(LayoutKind.Sequential)] struct PropertyKey { public Guid fmtid; public uint pid; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] struct PropVariant
    {
        [FieldOffset(0)] public ushort type;
        [FieldOffset(8)] public IntPtr text;
    }
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant value);

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")] class PolicyConfigClient { }
    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(IntPtr device, IntPtr format);
        [PreserveSig] int GetDeviceFormat(IntPtr device, int defaultFormat, IntPtr format);
        [PreserveSig] int ResetDeviceFormat(IntPtr device);
        [PreserveSig] int SetDeviceFormat(IntPtr device, IntPtr format, IntPtr mix);
        [PreserveSig] int GetProcessingPeriod(IntPtr device, int defaultPeriod, IntPtr period, IntPtr minimum);
        [PreserveSig] int SetProcessingPeriod(IntPtr device, IntPtr period);
        [PreserveSig] int GetShareMode(IntPtr device, IntPtr mode);
        [PreserveSig] int SetShareMode(IntPtr device, IntPtr mode);
        [PreserveSig] int GetPropertyValue(IntPtr device, int fx, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue(IntPtr device, int fx, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        [PreserveSig] int SetEndpointVisibility(IntPtr device, int visible);
    }
    static readonly PropertyKey FriendlyName = new PropertyKey { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };

    static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    static void Check(int hr, string operation)
    {
        if (hr < 0) throw new COMException(operation + " failed", hr);
    }

    static string DefaultId(IMMDeviceEnumerator enumerator, int role)
    {
        IMMDevice device = null;
        try
        {
            Check(enumerator.GetDefaultAudioEndpoint(Render, role, out device), "GetDefaultAudioEndpoint");
            Check(device.GetId(out string id), "GetId");
            return id;
        }
        finally { Release(device); }
    }

    static bool IsActive(IMMDeviceEnumerator enumerator, string id)
    {
        IMMDevice device = null;
        try
        {
            int hr = enumerator.GetDevice(id, out device);
            if (hr < 0 || device == null) return false;
            return device.GetState(out int state) >= 0 && (state & Active) != 0;
        }
        finally { Release(device); }
    }

    static string Name(IMMDevice device)
    {
        IPropertyStore store = null;
        try
        {
            Check(device.OpenPropertyStore(0, out store), "OpenPropertyStore");
            var key = FriendlyName;
            Check(store.GetValue(ref key, out PropVariant value), "GetValue");
            try { return value.type == 31 ? Marshal.PtrToStringUni(value.text) : null; }
            finally { PropVariantClear(ref value); }
        }
        finally { Release(store); }
    }

    static string FindQuest(IMMDeviceEnumerator enumerator, bool verbose)
    {
        IMMDeviceCollection collection = null;
        string match = null;
        int matches = 0;
        try
        {
            Check(enumerator.EnumAudioEndpoints(Render, Active, out collection), "EnumAudioEndpoints");
            Check(collection.GetCount(out uint count), "GetCount");
            for (uint i = 0; i < count; i++)
            {
                IMMDevice device = null;
                try
                {
                    Check(collection.Item(i, out device), "Item");
                    string name = Name(device) ?? "";
                    if (name.IndexOf("Oculus", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Check(device.GetId(out string id), "GetId");
                    if (verbose) Debug.Log($"[WindowsAudioRouter] Quest candidate: {name}; endpoint={id}");
                    match = id;
                    matches++;
                }
                finally { Release(device); }
            }
        }
        finally { Release(collection); }
        if (matches != 1)
        {
            if (verbose) Debug.LogWarning($"[WindowsAudioRouter] Found {matches} Oculus playback endpoints; expected exactly one. No output was changed.");
            return null;
        }
        return match;
    }

    static bool SetAndVerify(IMMDeviceEnumerator enumerator, IPolicyConfig policy, string id, int role)
    {
        int hr = policy.SetDefaultEndpoint(id, role);
        string actual = null;
        try { actual = DefaultId(enumerator, role); }
        catch (Exception ex) { Debug.LogWarning($"[WindowsAudioRouter] Cannot verify role {role}: {ex.Message}"); }
        bool success = hr >= 0 && string.Equals(actual, id, StringComparison.OrdinalIgnoreCase);
        string message = $"[WindowsAudioRouter] role={role}, requested={id}, HRESULT=0x{unchecked((uint)hr):X8}, verifiedDefault={actual ?? "unavailable"}";
        if (success) Debug.Log(message); else Debug.LogWarning(message);
        return success;
    }

    static bool IsOculusEndpoint(IMMDeviceEnumerator enumerator, string id)
{
    if (string.IsNullOrEmpty(id)) return false;
    IMMDevice device = null;
    try
    {
        if (enumerator.GetDevice(id, out device) < 0 || device == null) return false;
        string name = Name(device) ?? "";
        return name.IndexOf("Oculus", StringComparison.OrdinalIgnoreCase) >= 0;
    }
    catch (Exception) { return false; }
    finally { Release(device); }
}

   public static void BeginVRSession()
{
    if (hasSnapshot) return;
    IMMDeviceEnumerator enumerator = null;
    try
    {
        enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        var originals = new string[3];
        for (int role = 0; role < 3; role++) originals[role] = DefaultId(enumerator, role);

        // If the console default is itself the headset, there is no valid desktop device to restore.
        if (IsOculusEndpoint(enumerator, originals[0]))
        {
            Debug.LogWarning("[WindowsAudioRouter] Console default is already an Oculus device; no desktop snapshot taken.");
            return;
        }

        for (int role = 1; role < 3; role++)
        {
            if (IsOculusEndpoint(enumerator, originals[role]))
            {
                Debug.LogWarning($"[WindowsAudioRouter] Role {role} was already Oculus; using the console device as its restore target.");
                originals[role] = originals[0];
            }
        }

        Array.Copy(originals, savedEndpoints, 3);
        hasSnapshot = true;
        Debug.Log("[WindowsAudioRouter] Saved desktop playback defaults before XR startup.");
    }
    catch (Exception ex) { Debug.LogWarning($"[WindowsAudioRouter] Cannot save playback defaults: {ex.Message}"); }
    finally { Release(enumerator); }
}

    public static string ProbeQuestEndpoint()
    {
        IMMDeviceEnumerator enumerator = null;
        try { enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator(); var result = FindQuest(enumerator, false); probeWarningLogged = false; return result; }
        catch (Exception ex)
        {
            if (!probeWarningLogged) Debug.LogWarning($"[WindowsAudioRouter] Cannot inspect playback endpoints: {ex.Message}");
            probeWarningLogged = true;
            return null;
        }
        finally { Release(enumerator); }
    }

    public static bool AreQuestDefaultsActive(string endpoint)
    {
        if (string.IsNullOrEmpty(endpoint)) return false;
        IMMDeviceEnumerator enumerator = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            for (int role = 0; role < 3; role++)
                if (!string.Equals(DefaultId(enumerator, role), endpoint, StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }
        catch (Exception) { return false; }
        finally { Release(enumerator); }
    }

    public static bool SwitchToOculusHeadset()
    {
        IMMDeviceEnumerator enumerator = null;
        IPolicyConfig policy = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            if (!hasSnapshot) { Debug.LogWarning("[WindowsAudioRouter] No desktop snapshot; refusing to change outputs."); return false; }
            string target = FindQuest(enumerator, true);
            if (target == null) return false;
            if (questEndpoint != null && !string.Equals(questEndpoint, target, StringComparison.OrdinalIgnoreCase) && IsActive(enumerator, questEndpoint))
            {
                Debug.LogWarning("[WindowsAudioRouter] A different Quest endpoint is still active; refusing an ambiguous switch.");
                return false;
            }
            questEndpoint = target;
            policy = (IPolicyConfig)new PolicyConfigClient();
            for (int role = 0; role < 3; role++)
            {
                changedRoles[role] = true;
                if (!SetAndVerify(enumerator, policy, target, role))
                {
                    Debug.LogWarning("[WindowsAudioRouter] Quest switch failed; attempting to restore the original outputs.");
                    RestoreDesktopSpeakers(true);
                    return false;
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[WindowsAudioRouter] Quest switch failed: {ex.Message} (0x{unchecked((uint)Marshal.GetHRForException(ex)):X8}).");
            if (hasSnapshot) RestoreDesktopSpeakers(true);
            return false;
        }
        finally { Release(policy); Release(enumerator); }
    }

    public static void RestoreDesktopSpeakers(bool keepSnapshot = false)
{
    // Stop the monitor first so its loop can't switch back to Quest mid-restore.
    if (!keepSnapshot) StopMonitoring();

    if (!hasSnapshot)
    {
        Debug.Log("[WindowsAudioRouter] Restore skipped: no snapshot.");
        return;
    }

    IMMDeviceEnumerator enumerator = null;
    IPolicyConfig policy = null;
    try
    {
        enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        policy = (IPolicyConfig)new PolicyConfigClient();
        for (int role = 0; role < 3; role++)
        {
            string current = DefaultId(enumerator, role);
            Debug.Log($"[WindowsAudioRouter] Restore role={role}: current={current}, saved={savedEndpoints[role]}, quest={questEndpoint}, changed={changedRoles[role]}");

            if (!changedRoles[role]) continue;

            if (string.Equals(current, savedEndpoints[role], StringComparison.OrdinalIgnoreCase))
            {
                changedRoles[role] = false;
                continue;
            }
            if (!string.Equals(current, questEndpoint, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"[WindowsAudioRouter] Role {role} now uses {current}; preserving the externally selected output.");
                changedRoles[role] = false;
                continue;
            }
            if (!IsActive(enumerator, savedEndpoints[role]))
            {
                Debug.LogWarning($"[WindowsAudioRouter] Original endpoint for role {role} is unavailable: {savedEndpoints[role]}.");
                continue;
            }
            if (SetAndVerify(enumerator, policy, savedEndpoints[role], role)) changedRoles[role] = false;
        }
        if (!keepSnapshot && !changedRoles[0] && !changedRoles[1] && !changedRoles[2])
        {
            hasSnapshot = false;
            questEndpoint = null;
            Array.Clear(savedEndpoints, 0, savedEndpoints.Length);
        }
    }
    catch (Exception ex) { Debug.LogWarning($"[WindowsAudioRouter] Restore failed: {ex.Message} (0x{unchecked((uint)Marshal.GetHRForException(ex)):X8})."); }
    finally { Release(policy); Release(enumerator); }
}

    public static void StartMonitoring()
    {
        if (monitor != null) return;
        var owner = new GameObject("Windows Audio Route Monitor");
        UnityEngine.Object.DontDestroyOnLoad(owner);
        monitor = owner.AddComponent<WindowsAudioRouteMonitor>();
    }

    public static void StopMonitoring()
    {
        if (monitor == null) return;
        monitor.StopAllCoroutines();
        monitor.enabled = false;
        UnityEngine.Object.Destroy(monitor.gameObject);
        monitor = null;
    }
#else
    public static void BeginVRSession() { }
    public static bool SwitchToOculusHeadset() { return false; }
    public static void StartMonitoring() { }
    public static void StopMonitoring() { }
    public static void RestoreDesktopSpeakers(bool keepSnapshot = false) { }
#endif
}