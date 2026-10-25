using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using TimeoutException = System.TimeoutException;

namespace PointingStickSwitch
{
    internal sealed class ServiceHost : ServiceBase
    {
        internal const string Name = "AccuPointSwitch";
        private const string PipeName = "PointingStickSwitch.Local.v2";
        private readonly string listenPipe = PipeName;
        internal static readonly string InstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Name);
        private static string InstalledExe { get { return Path.Combine(InstallDir, "TrackPointSwitch.exe"); } }
        private static string ImagePath { get { return "\"" + InstalledExe + "\" --service"; } }
        private static string ServiceKey { get { return @"SYSTEM\CurrentControlSet\Services\" + Name; } }
        private volatile bool stopping;
        private NamedPipeServerStream activePipe;
        private Thread worker;
        private SecurityIdentifier allowedSid;
        private readonly object gate = new object();
        private readonly object operationGate = new object();
        private Timer restoreTimer;
        private string lastRestoreError = "";
        private readonly Engine engine = new Engine(new NativeBackend(), new RegistryIntentStore());

        internal ServiceHost() { ServiceName = Name; CanStop = true; CanHandlePowerEvent = true; AutoLog = false; }
        internal static bool Installed { get { using (var key = Registry.LocalMachine.OpenSubKey(ServiceKey)) return key != null; } }
        internal static string InstallUserSid
        {
            get
            {
                using (var key = Registry.LocalMachine.OpenSubKey(ServiceKey + @"\Parameters"))
                {
                    string existing = Convert.ToString(key == null ? null : key.GetValue("AllowedUserSid"));
                    return String.IsNullOrEmpty(existing) ? WindowsIdentity.GetCurrent().User.Value : existing;
                }
            }
        }
        internal static bool Running
        {
            get { try { using (var service = new ServiceController(Name)) return service.Status == ServiceControllerStatus.Running; } catch { return false; } }
        }

        protected override void OnStart(string[] args)
        {
            using (var key = Registry.LocalMachine.OpenSubKey(ServiceKey + @"\Parameters"))
                allowedSid = new SecurityIdentifier(Convert.ToString(key == null ? null : key.GetValue("AllowedUserSid")));
            new RegistryIntentStore().Migrate();
            stopping = false;
            worker = new Thread(Serve) { IsBackground = true, Name = "AccuPoint pipe listener" };
            worker.Start();
            restoreTimer = new Timer(RestoreSavedState, null, 1500, 30000);
        }
        protected override void OnStop()
        {
            stopping = true;
            if (restoreTimer != null) restoreTimer.Dispose();
            lock (gate) { if (activePipe != null) activePipe.Dispose(); }
            if (worker != null) worker.Join(20000);
            // Timer.Dispose alone does not wait for an already-running callback.
            // Do not report Stopped while a restore can still change hardware.
            lock (operationGate) { }
        }

        protected override bool OnPowerEvent(PowerBroadcastStatus status)
        {
            if (status == PowerBroadcastStatus.ResumeAutomatic || status == PowerBroadcastStatus.ResumeSuspend || status == PowerBroadcastStatus.ResumeCritical)
            {
                lastRestoreError = "";
                if (restoreTimer != null) restoreTimer.Change(1500, 30000);
            }
            return true;
        }

        private void RestoreSavedState(object unused)
        {
            lock (operationGate)
            {
                if (stopping) return;
                try
                {
                    string errors = String.Join("\n", engine.Restore(() => stopping));
                    if (errors != lastRestoreError && errors.Length > 0) Program.Log("状态恢复：" + errors);
                    lastRestoreError = errors;
                }
                catch (Exception ex) { if (lastRestoreError != ex.Message) Program.Log(ex.Message); lastRestoreError = ex.Message; }
            }
        }

        private void Serve()
        {
            while (!stopping)
            {
                try
                {
                    var acl = new PipeSecurity();
                    acl.SetAccessRuleProtection(true, false);
                    // Deny remote network logons; grant only the selected local user and privileged local accounts.
                    acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
                    acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
                    acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
                    // Exclude CreateNewInstance: clients may exchange messages but cannot create a server pipe.
                    acl.AddAccessRule(new PipeAccessRule(allowedSid, PipeAccessRights.ReadData | PipeAccessRights.WriteData | PipeAccessRights.ReadAttributes | PipeAccessRights.Synchronize, AccessControlType.Allow));
                    using (var pipe = new NamedPipeServerStream(listenPipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous, 4096, 4096, acl))
                    {
                        lock (gate) { if (stopping) return; activePipe = pipe; }
                        pipe.WaitForConnection();
                        string request = ReadLine(pipe, 128, 3000);
                        string reply;
                        try
                        {
                            lock (operationGate)
                            {
                            if (stopping) throw new OperationCanceledException("服务正在停止。");
                            var parsed = Wire.Parse(request);
                            reply = parsed.Command == "LIST" ? Json.Write(engine.List()) : Json.Write(engine.Apply(parsed.Token, parsed.Enabled, true));
                            }
                            reply = "OK\n" + reply;
                        }
                        catch (Exception ex) { reply = "ERROR\n" + ex.Message; Program.Log(ex.ToString()); }
                        byte[] bytes = Encoding.UTF8.GetBytes(reply);
                        AwaitIO(pipe, pipe.WriteAsync(bytes, 0, bytes.Length), 3000);
                    }
                }
                catch (Exception ex) { if (!stopping) { Program.Log("Service: " + ex.Message); Thread.Sleep(150); } }
                finally { lock (gate) activePipe = null; }
            }
        }

        private static string ReadLine(PipeStream pipe, int max, int timeout)
        {
            using (var buffer = new MemoryStream())
            {
                var timer = Stopwatch.StartNew(); var one = new byte[1];
                while (buffer.Length <= max)
                {
                    int remaining = timeout - (int)timer.ElapsedMilliseconds;
                    if (remaining <= 0) throw new TimeoutException("IPC read timeout");
                    var read = pipe.ReadAsync(one, 0, 1);
                    AwaitIO(pipe, read, remaining);
                    int count = read.Result;
                    if (count == 0) throw new IOException("Incomplete IPC request");
                    if (one[0] == 10) return Encoding.UTF8.GetString(buffer.ToArray());
                    buffer.WriteByte(one[0]);
                }
            }
            throw new IOException("IPC request too long");
        }

        private static void AwaitIO(PipeStream pipe, Task operation, int timeout)
        {
            // Do not dispose IAsyncResult.AsyncWaitHandle before its IO completion
            // callback: Framework PipeStream can otherwise crash the whole process.
            if (operation.Wait(timeout)) return;
            pipe.Dispose();
            operation.ContinueWith(t => GC.KeepAlive(t.Exception), TaskContinuationOptions.OnlyOnFaulted);
            throw new TimeoutException("服务通信超时；请刷新状态后重试。");
        }

        internal static string Request(string command)
        {
            if (!Running) throw new InvalidOperationException("指点杆后台服务未运行。请在控制面板点击“安装 / 修复免 UAC 服务”。");
            return Exchange(PipeName, ServicePid(), command);
        }
        internal static string Exchange(string name, uint expectedPid, string command)
        {
            using (var pipe = new NamedPipeClientStream(".", name, PipeAccessRights.ReadData | PipeAccessRights.WriteData | PipeAccessRights.Synchronize, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification, HandleInheritability.None))
            {
                pipe.Connect(2500);
                uint serverPid;
                if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out serverPid) || serverPid != expectedPid)
                    throw new InvalidOperationException("服务端身份校验失败。");
                byte[] request = Encoding.ASCII.GetBytes(command + "\n");
                AwaitIO(pipe, pipe.WriteAsync(request, 0, request.Length), 3000);
                using (var output = new MemoryStream())
                {
                    byte[] data = new byte[4096]; var timer = Stopwatch.StartNew();
                    while (output.Length < 262144)
                    {
                        int remaining = 25000 - (int)timer.ElapsedMilliseconds;
                        if (remaining <= 0) throw new TimeoutException("服务响应超时，请刷新设备状态。");
                        var read = pipe.ReadAsync(data, 0, data.Length);
                        AwaitIO(pipe, read, remaining);
                        int count = read.Result;
                        if (count == 0)
                        {
                            string response = Encoding.UTF8.GetString(output.ToArray());
                            if (response.StartsWith("OK\n", StringComparison.Ordinal)) return response.Substring(3);
                            throw new InvalidOperationException(response.StartsWith("ERROR\n", StringComparison.Ordinal) ? response.Substring(6) : "无效的服务响应。");
                        }
                        output.Write(data, 0, count);
                    }
                    throw new IOException("服务响应过长。");
                }
            }
        }

        private static void VerifyExisting()
        {
            using (var key = Registry.LocalMachine.OpenSubKey(ServiceKey))
            {
                if (key == null) return;
                if (!String.Equals(Convert.ToString(key.GetValue("ImagePath")), ImagePath, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("已存在同名服务，请检查其安装路径。");
            }
        }

        internal static void Install(string userSid)
        {
            if (!Program.IsAdmin) throw new InvalidOperationException("安装服务需要管理员权限。");
            var sid = new SecurityIdentifier(userSid);
            if (!sid.IsAccountSid() && !sid.IsWellKnown(WellKnownSidType.InteractiveSid)) throw new ArgumentException("无效的用户 SID。");
            VerifyExisting();
            if (Directory.Exists(InstallDir) && (File.GetAttributes(InstallDir) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("安装目录不能是重解析点。");
            if (File.Exists(InstalledExe) && (File.GetAttributes(InstalledExe) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("程序文件不能是重解析点。");
            if (Installed)
            {
                using (var service = new ServiceController(Name))
                {
                    if (service.Status != ServiceControllerStatus.Stopped) { service.Stop(); service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(25)); }
                }
            }
            var acl = new DirectorySecurity();
            var admin = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            acl.SetOwner(admin); acl.SetAccessRuleProtection(true, false);
            foreach (var identity in new[] { admin, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
                acl.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            Directory.CreateDirectory(InstallDir, acl); Directory.SetAccessControl(InstallDir, acl);
            if (!String.Equals(Program.Exe, InstalledExe, StringComparison.OrdinalIgnoreCase)) File.Copy(Program.Exe, InstalledExe, true);
            if (!String.Equals(Program.Exe, InstalledExe, StringComparison.OrdinalIgnoreCase) && File.Exists(Program.Exe + ".config"))
                File.Copy(Program.Exe + ".config", InstalledExe + ".config", true);
            // Explicit file ACL prevents a pre-existing writable file from becoming a privileged service binary.
            var fileAcl = new FileSecurity(); fileAcl.SetOwner(admin); fileAcl.SetAccessRuleProtection(true, false);
            fileAcl.AddAccessRule(new FileSystemAccessRule(admin, FileSystemRights.FullControl, AccessControlType.Allow));
            fileAcl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
            fileAcl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute, AccessControlType.Allow));
            File.SetAccessControl(InstalledExe, fileAcl);
            foreach (string name in new[] { "TrackPointSwitch.exe.config", "SynapticsHost32.exe", "SynapticsHost32.exe.config" })
            {
                string source = Path.Combine(Path.GetDirectoryName(Program.Exe), name);
                string destination = Path.Combine(InstallDir, name);
                if (File.Exists(destination) && (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0) throw new IOException("程序组件不能是重解析点。");
                if (!String.Equals(source, destination, StringComparison.OrdinalIgnoreCase)) File.Copy(source, destination, true);
                File.SetAccessControl(destination, fileAcl);
            }
            IntPtr scm = OpenSCManagerW(null, null, 3);
            if (scm == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                if (!Installed)
                {
                    IntPtr service = CreateServiceW(scm, Name, "Pointing Stick 指点杆开关", 0xF01FF, 0x10, 2, 1, ImagePath, null, IntPtr.Zero, null, null, null);
                    if (service == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                    CloseServiceHandle(service);
                }
                using (var parameters = Registry.LocalMachine.CreateSubKey(ServiceKey + @"\Parameters"))
                {
                    parameters.SetValue("AllowedUserSid", sid.Value, RegistryValueKind.String);

                }
                using (var key = Registry.LocalMachine.OpenSubKey(ServiceKey, true)) key.SetValue("Description", "独立指点杆接口控制与状态恢复；日常操作无需 UAC。", RegistryValueKind.String);
                using (var service = new ServiceController(Name)) { service.Start(); service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15)); }
                new RegistryIntentStore().Migrate();
                string status = Request("2|LIST"); Program.Log("服务安装成功\r\n" + status);
                using (var startup = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"))
                    startup.SetValue("PointingStickSwitch", "\"" + InstalledExe + "\" --agent", RegistryValueKind.String);
            }
            finally { CloseServiceHandle(scm); }
        }

        internal static void Uninstall()
        {
            if (!Program.IsAdmin) throw new InvalidOperationException("卸载服务需要管理员权限。");
            VerifyExisting();
            if (!Installed) return;
            if (UserSettings.ReadLogical().Exists(x => !x.Enabled))
                throw new InvalidOperationException("请先从普通用户桌面打开控制面板，启用 Synaptics 逻辑指点杆，再运行卸载。");
            // Stop the reconciler before releasing persisted devices, preventing a
            // concurrent timer from re-disabling one during uninstall.
            if (Running) using (var controller = new ServiceController(Name))
            { controller.Stop(); controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(25)); }
            try
            {
            new Engine(new NativeBackend(), new RegistryIntentStore()).ReleaseAll();
            using (var service = new ServiceController(Name))
            {
                if (service.Status != ServiceControllerStatus.Stopped) { service.Stop(); service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(25)); }
            }
            CloseInstalledApplications();
            IntPtr scm = OpenSCManagerW(null, null, 1);
            if (scm == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                IntPtr service = OpenServiceW(scm, Name, 0x10000);
                if (service == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                try { if (!DeleteService(service)) throw new Win32Exception(Marshal.GetLastWin32Error()); }
                finally { CloseServiceHandle(service); }
            }
            finally { CloseServiceHandle(scm); }
            }
            catch
            {
                if (Installed) try { ResumeService(); } catch (Exception ex) { Program.Log("恢复卸载前服务：" + ex.Message); }
                throw;
            }
            using (var startup = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                if (startup != null) startup.DeleteValue("PointingStickSwitch", false);
        }

        internal static void PrepareUpgrade()
        {
            if (!Program.IsAdmin) throw new InvalidOperationException("升级服务需要管理员权限。");
            VerifyExisting();
            bool wasRunning = Running;
            try
            {
                if (Installed) using (var service = new ServiceController(Name))
                {
                    if (service.Status != ServiceControllerStatus.Stopped)
                    { service.Stop(); service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(25)); }
                }
                CloseInstalledApplications();
            }
            catch
            {
                // If the preparation fails before the installer takes over, leave the
                // previously running service available to the existing client.
                if (wasRunning) try { ResumeService(); } catch (Exception ex) { Program.Log("恢复升级前服务：" + ex.Message); }
                throw;
            }
        }

        private static void CloseInstalledApplications()
        {
            // The installer runs this helper from its private temporary directory,
            // so Restart Manager can close the installed GUI without stopping this helper.
            if (String.Equals(Program.Exe, InstalledExe, StringComparison.OrdinalIgnoreCase) || !File.Exists(InstalledExe)) return;
            uint session; var key = new StringBuilder(33);
            int code = RmStartSession(out session, 0, key);
            if (code != 0) throw new Win32Exception(code);
            try
            {
                code = RmRegisterResources(session, 1, new[] { InstalledExe }, 0, IntPtr.Zero, 0, null);
                if (code != 0) throw new Win32Exception(code);
                // Only our installed executable is registered. The control panel
                // has no unsaved documents; permit closing hidden/unresponsive
                // copies after Restart Manager's normal shutdown timeout.
                code = RmShutdown(session, 1, IntPtr.Zero);
                if (code != 0) throw new InvalidOperationException("请关闭指点杆控制面板后重试。Restart Manager: " + code);
            }
            finally { RmEndSession(session); }
        }

        internal static void ResumeService()
        {
            if (!Program.IsAdmin) throw new InvalidOperationException("启动服务需要管理员权限。");
            VerifyExisting();
            if (!Installed || Running) return;
            using (var service = new ServiceController(Name))
            { service.Start(); service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15)); }
        }

        private static uint ServicePid()
        {
            IntPtr scm = OpenSCManagerW(null, null, 1);
            if (scm == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                IntPtr service = OpenServiceW(scm, Name, 4);
                if (service == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                try
                {
                    ServiceStatusProcess status; int needed;
                    if (!QueryServiceStatusEx(service, 0, out status, Marshal.SizeOf(typeof(ServiceStatusProcess)), out needed)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    return status.ProcessId;
                }
                finally { CloseServiceHandle(service); }
            }
            finally { CloseServiceHandle(scm); }
        }
        [StructLayout(LayoutKind.Sequential)] private struct ServiceStatusProcess
        { internal uint Type, State, Controls, Win32Exit, ServiceExit, Checkpoint, WaitHint, ProcessId, Flags; }
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr OpenSCManagerW(string machine, string database, uint access);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr OpenServiceW(IntPtr manager, string name, uint access);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateServiceW(IntPtr manager, string name, string display, uint access, uint type, uint start, uint error, string path, string group, IntPtr tag, string dependencies, string account, string password);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool DeleteService(IntPtr service);
        [DllImport("advapi32.dll")] private static extern bool CloseServiceHandle(IntPtr handle);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool QueryServiceStatusEx(IntPtr service, int level, out ServiceStatusProcess status, int size, out int needed);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] private static extern int RmStartSession(out uint session, uint flags, StringBuilder key);
        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)] private static extern int RmRegisterResources(uint session, uint fileCount, string[] files, uint appCount, IntPtr apps, uint serviceCount, string[] services);
        [DllImport("rstrtmgr.dll")] private static extern int RmShutdown(uint session, uint flags, IntPtr callback);
        [DllImport("rstrtmgr.dll")] private static extern int RmEndSession(uint session);
    }
}
