using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace PointingStickSwitch
{
    internal sealed class NativeBackend : IDeviceBackend
    {
        public List<DeviceRecord> Enumerate()
        {
            var results = new List<DeviceRecord>();
            IntPtr set = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, 6);
            if (set == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                for (uint index = 0; ; index++)
                {
                    var info = new DevInfo { Size = (uint)Marshal.SizeOf(typeof(DevInfo)) };
                    if (!SetupDiEnumDeviceInfo(set, index, ref info))
                    { int error = Marshal.GetLastWin32Error(); if (error == 259) break; throw new Win32Exception(error); }
                    string cls = info.ClassGuid.ToString();
                    if (cls != Safety.Mouse && cls != Safety.Keyboard && cls != Safety.Hid) continue;
                    var id = new StringBuilder(4096); uint needed;
                    if (!SetupDiGetDeviceInstanceIdW(set, ref info, id, (uint)id.Capacity, out needed)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    uint status, problem;
                    uint code = CM_Get_DevNode_Status(out status, out problem, info.DevInst, 0);
                    if (code == 0xD) continue; // disappeared during enumeration
                    Check(code, "读取设备状态");
                    using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\" + id))
                    {
                        if (key == null) continue;
                        var d = new DeviceRecord { Id = id.ToString(), ClassGuid = cls, Status = status, Problem = problem,
                            Name = Convert.ToString(key.GetValue("FriendlyName", key.GetValue("DeviceDesc", ""))),
                            Hardware = key.GetValue("HardwareID") as string[] ?? new string[0],
                            Compatible = key.GetValue("CompatibleIDs") as string[] ?? new string[0], Service = Convert.ToString(key.GetValue("Service")) };
                        using (var driver = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\" + Convert.ToString(key.GetValue("Driver"))))
                        {
                            if (driver != null) { d.Driver = Convert.ToString(driver.GetValue("DriverVersion")); d.Provider = Convert.ToString(driver.GetValue("ProviderName")); d.Section = Convert.ToString(driver.GetValue("InfSection")); }
                        }
                        var ancestry = new List<string>(); uint node = info.DevInst, parent;
                        while (ancestry.Count < 32 && CM_Get_Parent(out parent, node, 0) == 0)
                        {
                            var parentId = new StringBuilder(4096);
                            Check(CM_Get_Device_IDW(parent, parentId, parentId.Capacity, 0), "读取设备拓扑");
                            if (ancestry.Contains(parentId.ToString())) throw new InvalidOperationException("设备拓扑异常。");
                            ancestry.Add(parentId.ToString()); node = parent;
                        }
                        d.Ancestors = ancestry.ToArray(); results.Add(d);
                    }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return results;
        }
        public void Set(DeviceRecord device, bool enabled)
        {
            // Re-enumerate immediately before the privileged call; never accept a
            // client-supplied DEVINST or an arbitrary registry/driver path.
            var snapshot = Enumerate();
            var live = Engine.Resolve(snapshot, device.Token);
            if (live.Identity != device.Identity) throw new InvalidOperationException("设备身份已改变。");
            if (!enabled) Safety.CheckTopology(live, snapshot);
            uint node; Check(CM_Locate_DevNodeW(out node, live.Id, 0), "定位指点杆");
            Check(enabled ? CM_Enable_DevNode(node, 0) : CM_Disable_DevNode(node, 4 | 8), enabled ? "启用指点杆" : "关闭指点杆");
        }
        public void Wait(int milliseconds) { Thread.Sleep(milliseconds); }
        internal static void Check(uint result, string operation)
        {
            if (result == 0) return;
            string text = result == 0x33 ? "需要管理员服务" : result == 0x17 ? "驱动拒绝禁用；请使用厂商指点杆设置" :
                result == 0x22 ? "Windows 要求重启，请保存工作后重启电脑" : "Windows 设备调用失败";
            throw new InvalidOperationException(operation + "：" + text + " (CONFIGRET 0x" + result.ToString("X") + ")");
        }
        [StructLayout(LayoutKind.Sequential)] private struct DevInfo { internal uint Size; internal Guid ClassGuid; internal uint DevInst; internal IntPtr Reserved; }
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr SetupDiGetClassDevsW(IntPtr guid, string enumerator, IntPtr parent, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref DevInfo info);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiGetDeviceInstanceIdW(IntPtr set, ref DevInfo info, StringBuilder id, uint size, out uint needed);
        [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern uint CM_Locate_DevNodeW(out uint node, string id, uint flags);
        [DllImport("cfgmgr32.dll")] private static extern uint CM_Get_DevNode_Status(out uint status, out uint problem, uint node, uint flags);
        [DllImport("cfgmgr32.dll")] private static extern uint CM_Get_Parent(out uint parent, uint node, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern uint CM_Get_Device_IDW(uint node, StringBuilder id, int size, uint flags);
        [DllImport("cfgmgr32.dll")] private static extern uint CM_Disable_DevNode(uint node, uint flags);
        [DllImport("cfgmgr32.dll")] private static extern uint CM_Enable_DevNode(uint node, uint flags);
    }
}
