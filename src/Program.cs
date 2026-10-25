using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("Pointing Stick 指点杆开关")]
[assembly: System.Reflection.AssemblyDescription("独立 PnP 与 Synaptics 逻辑指点杆控制")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]

namespace PointingStickSwitch
{
    internal static class Client
    {
        internal static string ProbeNote = "";
        internal static List<Target> List()
        {
            var native = new NativeBackend().Enumerate();
            var list = native.Select(Engine.MakeTarget).Where(x => x != null).ToList();
            try
            {
                var syn = SynClient.Call("LIST"); ProbeNote = syn.Error;
                foreach (var item in syn.Targets)
                {
                    string pnp = Catalog.Canonical(item.Id.Contains("\\") ? item.Id : "HID\\" + item.Id);
                    if (!native.Any(x => Catalog.Match(x) != null && x.Hardware.Any(h => Catalog.Canonical(h) == pnp))) list.Add(item);
                }
            }
            catch (Exception ex) { ProbeNote = ex.Message; }
            return list;
        }
        internal static Target Change(Target target, bool? enabled)
        {
            if (!target.CanSwitch) throw new InvalidOperationException("所选指点杆当前不可操作。");
            // Persist automatic single-device selection as well: removal later must
            // never redirect the desktop shortcut to another connected stick.
            UserSettings.Selected = target.Token;
            if (target.Family == "syncom") return SynClient.Set(target.Token, enabled, true);
            string reply = ServiceHost.Request(enabled.HasValue ? "2|SET|" + target.Token + "|" + (enabled.Value ? "1" : "0") : "2|TOGGLE|" + target.Token);
            return Json.Read<Target>(reply);
        }
    }
    internal static class Program
    {
        internal static string Exe { get { return System.Reflection.Assembly.GetExecutingAssembly().Location; } }
        internal static string DataDir { get { return WindowsIdentity.GetCurrent().IsSystem ? Path.Combine(ServiceHost.InstallDir, "Logs") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PointingStickSwitch"); } }
        internal static bool IsAdmin { get { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); } }
        [STAThread]
        private static int Main(string[] args)
        {
            bool quiet = args.Contains("--quiet");
            string command = args.Length == 0 ? "--ui" : args[0];
            try
            {
                if (command == "--syn-worker")
                {
                    if (IsAdmin || WindowsIdentity.GetCurrent().IsSystem) throw new InvalidOperationException("厂商逻辑接口只能以普通用户权限运行。");
                    Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
                    Console.SetIn(new StreamReader(Console.OpenStandardInput(), Encoding.UTF8));
                    string request = Console.ReadLine();
                    if (request == null || request.Length > 128) return 2;
                    Console.Write(Json.Write(SynWorker.Run(request))); return 0;
                }
                if (command == "--service") { ServiceBase.Run(new ServiceHost()); return 0; }
                if (command == "--prepare-upgrade") { ServiceHost.PrepareUpgrade(); return 0; }
                if (command == "--resume-service") { ServiceHost.ResumeService(); return 0; }
                if (command == "--install")
                {
                    if (!IsAdmin) return Elevate("--install --quiet");
                    ServiceHost.Install("S-1-5-4"); return 0;
                }
                if (command == "--uninstall")
                {
                    if (!IsAdmin) return Elevate("--uninstall --quiet");
                    ServiceHost.Uninstall(); return 0;
                }
                if (command == "--status") { WriteResult(args, Json.Write(Client.List())); return 0; }
                if (command == "--diagnose") { WriteResult(args, Diagnostic()); return 0; }
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                if (command == "--agent") { UserAgent.Run(); return 0; }
                if (command == "--ui") { Application.Run(new MainForm(false)); return 0; }
                if (command == "--layout-test") { LayoutChecks.Run(args[1]); return 0; }
                if (command == "--toggle" || command == "--enable" || command == "--disable")
                {
                    Target selected = Selection.Resolve(Client.List(), UserSettings.Selected);
                    var result = Client.Change(selected, command == "--toggle" ? (bool?)null : command == "--enable");
                    if (!quiet) Application.Run(new ResultForm(result.Name + "\n" + result.State));
                    return 0;
                }
                throw new ArgumentException("支持参数：--ui、--toggle、--enable、--disable、--status、--diagnose、--install、--uninstall。");
            }
            catch (Exception ex)
            {
                Log(ex.ToString()); Console.Error.WriteLine(ex.Message);
                if (!quiet && command != "--syn-worker") MessageBox.Show(ex.Message, "指点杆开关", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 1;
            }
        }
        internal static string Diagnostic()
        {
            var records = new NativeBackend().Enumerate();
            var syn = SynClient.Call("LIST");
            return Json.Write(new { App = "PointingStickSwitch " + AppVersion.Value, OS = Environment.OSVersion.ToString(),
                Process64Bit = Environment.Is64BitProcess, ServiceRunning = ServiceHost.Running,
                Devices = records.Select(d => new { InstanceHash = d.Token, d.Hardware, d.Compatible, d.ClassGuid, d.Name, d.Driver, d.Provider, d.Section, d.Service, d.Status, d.Problem,
                    AncestorHashes = d.Ancestors.Select(DeviceRecord.Hash).ToArray(), Supported = Catalog.Match(d) != null }).ToArray(),
                Synaptics = new { syn.Available, syn.Error, Targets = syn.Targets.Select(t => new {
                    t.Token, InstanceHash = DeviceRecord.Hash(t.Id ?? ""), t.Name, t.Family, t.State,
                    t.Enabled, t.Disabled, t.CanSwitch, t.Driver, t.Evidence, t.Scope }).ToArray() } });
        }
        private static void WriteResult(string[] args, string text)
        {
            int index = Array.IndexOf(args, "--output");
            if (index >= 0 && index + 1 < args.Length) File.WriteAllText(Path.GetFullPath(args[index + 1]), text, new UTF8Encoding(false));
            else Console.WriteLine(text);
        }
        internal static void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(DataDir); string path = Path.Combine(DataDir, "operations.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) File.Delete(path);
                File.AppendAllText(path, DateTimeOffset.Now.ToString("o") + " " + message + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        internal static int Elevate(string arguments)
        {
            try
            {
                using (var p = Process.Start(new ProcessStartInfo(Exe, arguments) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden }))
                { if (p == null) return 1; p.WaitForExit(); return p.ExitCode; }
            }
            catch (Win32Exception ex) { if (ex.NativeErrorCode == 1223) return 1223; throw; }
        }
    }
    internal static class UserAgent
    {
        internal static void Run()
        {
            if (Program.IsAdmin || WindowsIdentity.GetCurrent().IsSystem) { Program.Log("用户状态恢复进程未启动：请从普通用户桌面启动程序。"); return; }
            // COM restoration is deliberately per-user and starts after sign-in.
            // The SYSTEM service never activates vendor COM objects.
            using (var mutex = new Mutex(false, @"Local\PointingStickSwitch.UserAgent"))
            {
                bool owned = false;
                try
                {
                    try { owned = mutex.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
                    if (!owned) return;
                    using (var timer = new System.Windows.Forms.Timer())
                    {
                        bool busy = false; string lastError = "";
                        timer.Interval = 30000;
                        timer.Tick += async delegate
                        {
                            if (busy) return;
                            busy = true;
                            try
                            {
                                await System.Threading.Tasks.Task.Run(delegate
                                {
                                    var errors = new List<string>();
                                    foreach (var saved in UserSettings.ReadLogical())
                                        try { SynClient.Set(saved.Token, saved.Enabled, false); } catch (Exception ex) { errors.Add(ex.Message); }
                                    if (errors.Count > 0) throw new InvalidOperationException(String.Join("\n", errors));
                                });
                                lastError = "";
                            }
                            catch (Exception ex) { if (lastError != ex.Message) Program.Log("用户状态恢复：" + ex.Message); lastError = ex.Message; }
                            finally { busy = false; }
                        };
                        timer.Start(); Application.Run();
                    }
                }
                finally { if (owned) mutex.ReleaseMutex(); }
            }
        }
    }
}
