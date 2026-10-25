using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace PointingStickSwitch
{
    internal sealed class SynReply
    {
        public List<Target> Targets = new List<Target>();
        public string Error = "";
        public bool Available;
    }
    internal static class SynIdentity
    {
        internal static bool IsStick(int type) { return type == 4 || type == 5; }
        internal static string Token(int type, int connection, string pnp)
        {
            if (!IsStick(type) || String.IsNullOrWhiteSpace(pnp) || pnp.Length > 2048) return null;
            return DeviceRecord.Hash("SYN|" + type + "|" + connection + "|" + pnp.Trim().ToUpperInvariant());
        }
    }
    // Executes only in an ordinary user's isolated STA process. A vendor COM call
    // cannot hang the UI/service or load a per-user COM registration into SYSTEM.
    internal static class SynWorker
    {
        private const int DeviceType = 268435714, ConnectionType = 268435715, DisableState = 268435825, PnpId = 268436994;
        private sealed class Logical : IDisposable
        {
            internal dynamic Device;
            public void Dispose() { if (Device != null) Marshal.FinalReleaseComObject(Device); }
        }
        internal static SynReply Run(string command)
        {
            var result = new SynReply(); object apiObject = null;
            var devices = new List<Logical>();
            try
            {
                var apiType = Type.GetTypeFromProgID("SynCtrl.SynAPICtrl", false);
                var devType = Type.GetTypeFromProgID("SynCtrl.SynDeviceCtrl", false);
                if (apiType == null || devType == null) return result;
                apiObject = Activator.CreateInstance(apiType); dynamic api = apiObject;
                api.Initialize(); result.Available = true;
                int previous = -1; var seen = new HashSet<int>();
                for (int i = 0; i < 64; i++)
                {
                    int handle = Convert.ToInt32(api.FindDevice(0, 0, previous));
                    if (handle == -1) break;
                    if (!seen.Add(handle)) throw new InvalidOperationException("Synaptics 返回了重复设备，请刷新设备。");
                    previous = handle;
                    var d = new Logical { Device = Activator.CreateInstance(devType) }; devices.Add(d);
                    d.Device.Select(handle);
                }
                if (seen.Count >= 64) throw new InvalidOperationException("Synaptics 设备数量异常。");
                var access = devices.Select(d => new SynAccess {
                    Type = () => Convert.ToInt32(d.Device.GetLongProperty(DeviceType)),
                    Connection = () => Convert.ToInt32(d.Device.GetLongProperty(ConnectionType)),
                    Pnp = () => Convert.ToString(d.Device.GetStringProperty(PnpId)),
                    Read = () => Convert.ToInt32(d.Device.GetLongProperty(DisableState)),
                    Write = value => d.Device.SetLongProperty(DisableState, value)
                }).ToList();
                result = SynPolicy.Run(access, command, Thread.Sleep);
            }
            catch (COMException ex)
            { if (result.Available || ex.ErrorCode != unchecked((int)0x80040154)) result.Error = ex.Message; }
            catch (Exception ex) { result.Error = ex.Message; }
            finally
            {
                foreach (var d in devices) d.Dispose();
                if (apiObject != null) Marshal.FinalReleaseComObject(apiObject);
            }
            return result;
        }
    }
    internal static class SynClient
    {
        internal static SynReply Call(string command)
        {
            var reply = Invoke(Program.Exe, command);
            if (!reply.Available && Environment.Is64BitProcess)
            {
                string helper = Path.Combine(Path.GetDirectoryName(Program.Exe), "SynapticsHost32.exe");
                if (File.Exists(helper)) reply = Invoke(helper, command);
            }
            return reply;
        }
        private static SynReply Invoke(string exe, string command)
        {
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo(exe, "--syn-worker") { UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
                process.Start();
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                process.StandardInput.WriteLine(command); process.StandardInput.Close();
                if (!process.WaitForExit(8000))
                {
                    try { process.Kill(); } catch (InvalidOperationException) { }
                    throw new TimeoutException("厂商接口响应超时，请刷新设备状态。");
                }
                if (process.ExitCode != 0 || !String.IsNullOrEmpty(error.Result)) throw new IOException("厂商接口进程失败：" + error.Result);
                return Json.Read<SynReply>(output.Result);
            }
        }
        internal static Target Set(string token, bool? enabled, bool persist)
        {
            using (var mutex = new Mutex(false, @"Local\PointingStickSwitch.Synaptics"))
            {
                bool owned = false;
                try
                {
                    try { owned = mutex.WaitOne(12000); } catch (AbandonedMutexException) { owned = true; }
                    if (!owned) throw new TimeoutException("另一个指点杆操作正在进行。");
                    var result = new LogicalController(Call, new LogicalStore()).Apply(token, enabled, persist);
                    if (persist)
                    {
                        // Launch failure does not undo an already verified device change.
                        try { using (var agent = Process.Start(new ProcessStartInfo(Program.Exe, "--agent") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden })) { } }
                        catch (Exception ex) { Program.Log("用户恢复进程未启动：" + ex.Message); }
                    }
                    return result;
                }
                finally { if (owned) mutex.ReleaseMutex(); }
            }
        }
    }
}
