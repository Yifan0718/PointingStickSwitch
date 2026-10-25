using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.IO.Pipes;
using System.Security.Principal;
using System.Diagnostics;
using PointingStickSwitch;

internal static class Tests
{
    private static int count;
    private static readonly List<string> report = new List<string>();
    private static void Assert(bool ok, string text) { count++; if (!ok) throw new Exception(text); }
    private static void Reject(Action action, string text) { bool rejected = false; try { action(); } catch (Exception) { rejected = true; } Assert(rejected, text); }
    private static void Test(string name, Action action) { action(); report.Add("PASS " + name); }
    private static DeviceRecord Device(string root = @"HID\TOSB1008&COL05", string instance = "UNIT1", string cls = Safety.Mouse)
    { return new DeviceRecord { Id = root + "\\" + instance, Hardware = new[] { root }, ClassGuid = cls, Status = 8 | 0x2000, Driver = "1.0" }; }
    private static DeviceRecord Clone(DeviceRecord d) { return Json.Read<DeviceRecord>(Json.Write(d)); }
    private sealed class FakeBackend : IDeviceBackend
    {
        internal List<DeviceRecord> Records = new List<DeviceRecord>();
        internal int Writes, Reads, Waits;
        internal bool FailOnce, Ignore, BreakNeighbor;
        internal Action<FakeBackend> OnRead;
        public List<DeviceRecord> Enumerate() { Reads++; if (OnRead != null) OnRead(this); return Records.Select(Clone).ToList(); }
        public void Set(DeviceRecord device, bool enabled)
        {
            Writes++;
            if (FailOnce) { FailOnce = false; throw new IOException("driver veto"); }
            if (Ignore) return;
            var target = Records.Single(d => d.Id == device.Id);
            target.Status = enabled ? 8u | 0x2000u : 0x400u; target.Problem = enabled ? 0u : 22u;
            if (BreakNeighbor && !enabled) { Records[1].Status = 0; Records[1].Problem = 10; }
            if (BreakNeighbor && enabled) { Records[1].Status = 8; Records[1].Problem = 0; }
        }
        public void Wait(int milliseconds) { Waits++; }
    }
    private sealed class MemoryStore : IIntentStore
    {
        internal List<Intent> Values = new List<Intent>();
        internal bool Fail;
        public List<Intent> Read() { return Values.Select(x => new Intent { Token = x.Token, Enabled = x.Enabled }).ToList(); }
        public void Write(List<Intent> list) { if (Fail) throw new IOException("disk failure"); Values = list.Select(x => new Intent { Token = x.Token, Enabled = x.Enabled }).ToList(); }
    }
    private sealed class FakeSyn
    {
        internal int Type = 4, Connection = 2, State, Writes;
        internal string Pnp = @"ACPI\LEN001E";
        internal bool Refuse, Ignore, Unreadable;
        internal Action<int> OnWrite;
        internal SynAccess Access()
        {
            return new SynAccess { Type = () => Type, Connection = () => Connection, Pnp = () => Pnp,
                Read = () => { if (Unreadable) throw new IOException("property unavailable"); return State; },
                Write = value => { Writes++; if (Refuse) throw new IOException("driver veto"); if (!Ignore) State = value; if (OnWrite != null) OnWrite(value); } };
        }
    }
    private static FakeBackend Backend() { return new FakeBackend { Records = new List<DeviceRecord> { Device(), Device(@"HID\VID_1234&PID_5678", "MOUSE") } }; }
    [STAThread]
    private static int Main(string[] args)
    {
        string root = args[0]; string output = args[1];
        try
        {
            Test("167 official INF model rows and aliases; negative class/interface/root/absence cases", delegate
            {
                int rows = 0;
                foreach (var row in File.ReadAllLines(Path.Combine(root, "official-models.csv"), Encoding.UTF8).Skip(1))
                {
                    var fields = row.Trim().Split(','); if (fields.Length < 6) continue;
                    string raw = fields[1], canonical = Catalog.Canonical(raw);
                    var d = Device(canonical, "FIXTURE", fields[3]); d.Hardware = new[] { raw };
                    var match = Catalog.Match(d); Assert(match != null && match.Family == fields[0], "Missed official ID " + raw); rows++;
                    var lower = Clone(d); lower.Id = lower.Id.ToLowerInvariant(); lower.ClassGuid = "{" + lower.ClassGuid + "}"; Assert(Catalog.Match(lower) != null, "case/brace normalization");
                    var absent = Clone(d); absent.Present = false; Assert(Catalog.Match(absent) == null, "absent match");
                    var keyboard = Clone(d); keyboard.ClassGuid = Safety.Keyboard; Assert(Catalog.Match(keyboard) == null, "keyboard matched");
                    var rootMismatch = Clone(d); rootMismatch.Id = @"HID\OTHER\FIXTURE"; Assert(Catalog.Match(rootMismatch) == null, "instance root mismatch accepted");
                    var hwMismatch = Clone(d); hwMismatch.Hardware = new[] { @"HID\MOUSE" }; Assert(Catalog.Match(hwMismatch) == null, "hardware mismatch accepted");
                    var touch = Clone(d); touch.Compatible = new[] { @"HID_DEVICE_UP:000D_U:0005" }; Assert(Catalog.Match(touch) == null, "digitizer accepted");
                    var suffix = Device(canonical + "X", "FIXTURE", fields[3]); Assert(Catalog.Match(suffix) == null, "prefix wildcard accepted");
                    if (canonical.Contains("&COL")) { var pad = Device(canonical.Replace("&COL05", "&COL02").Replace("&COL03", "&COL02"), "PAD", fields[3]); Assert(Catalog.Match(pad) == null, "touchpad collection accepted"); }
                }
                Assert(rows == 167, "fixture count changed; review catalog deliberately");
            });
            Test("legacy shared PS/2, virtual mice and generic AUI testing IDs rejected", delegate
            {
                foreach (string hw in new[] { @"ACPI\LEN001E", @"ACPI\LEN0068", @"ACPI\LEN0071", @"ACPI\LEN008F", @"ACPI\PNP0F13", @"ACPI\SYN1E20", @"ACPI\AUI0000&COL03", @"HID\VID_0488&PID_0005", @"SWD\GENERIC_HID", @"HID\TOSB1008&COL02", @"ACPI\TOSB1008" })
                    foreach (var cls in new[] { Safety.Mouse, Safety.Hid, Safety.Keyboard }) Assert(Catalog.Match(Device(hw, "UNIT", cls)) == null, "unsafe shared root " + hw);
            });
            Test("external keyboards: only exact product mouse collections, never keyboard/USB parent", delegate
            {
                foreach (var pid in new[] { "6009", "6047", "6048", "60EE", "60E1" })
                {
                    var d = Device(@"HID\VID_17EF&PID_" + pid + "&MI_01&COL02"); d.Compatible = new[] { "HID_DEVICE_SYSTEM_MOUSE" };
                    Assert(Catalog.Match(d) != null, "external mouse leaf");
                    d.ClassGuid = Safety.Keyboard; Assert(Catalog.Match(d) == null, "external keyboard leaf");
                    d.ClassGuid = Safety.Mouse; d.Compatible = new string[0]; Assert(Catalog.Match(d) == null, "usage not established");
                    d.Compatible = new[] { "HID_DEVICE_SYSTEM_MOUSE" }; d.Id = d.Id.Replace("HID", "USB"); Assert(Catalog.Match(d) == null, "USB parent accepted");
                }
            });
            Test("Synaptics logical types and stable identity; reject mouse/touchpad/keyboard", delegate
            {
                for (int type = -10; type < 100; type++) Assert((SynIdentity.Token(type, 2, "ACPI\\LEN001E") != null) == (type == 4 || type == 5), "logical type gate");
                Assert(SynIdentity.Token(4, 2, "") == null, "empty stable ID");
                Assert(SynIdentity.Token(4, 2, "abc") == SynIdentity.Token(4, 2, "ABC"), "case-stable identity");
                Assert(SynIdentity.Token(4, 2, "abc") != SynIdentity.Token(4, 7, "abc"), "different connection conflated");
            });
            Test("protocol rejects arbitrary paths, scripts, bad lengths, case, control characters, unknown versions", delegate
            {
                string token = Device().Token;
                Assert(Wire.Parse("2|LIST").Command == "LIST", "list parse");
                Assert(Wire.Parse("2|SET|" + token + "|0").Enabled == false, "disable parse");
                foreach (string bad in new[] { "", "STATUS", "1|LIST", "2|LIST\n", "2|SET|C:\\Windows|0", "2|EXEC|cmd", "2|SET|" + token + "|false", "2|SET|" + token.ToLowerInvariant() + "|0", "2|TOGGLE|" + token + "|1" }) Reject(() => Wire.Parse(bad), "bad command accepted");
                var random = new Random(442);
                for (int i = 0; i < 10000; i++)
                {
                    string noise = new string(Enumerable.Range(0, random.Next(0, 200)).Select(x => (char)random.Next(0, 128)).ToArray());
                    Reject(() => Wire.Parse(noise), "fuzz command accepted");
                }
            });
            Test("Synaptics shared PS/2 policy: touchpad preservation, both stick types, duplicates, veto, timeout and collateral rollback", delegate
            {
                foreach (int type in new[] { 4, 5 }) foreach (int connection in new[] { 2, 3, 4, 5, 6, 7 })
                {
                    var stick = new FakeSyn { Type = type, Connection = connection };
                    var pad = new FakeSyn { Type = 2, Pnp = "TOUCHPAD" };
                    var devices = new List<SynAccess> { stick.Access(), pad.Access() };
                    string token = SynIdentity.Token(type, connection, stick.Pnp);
                    var reply = SynPolicy.Run(devices, "SET|" + token + "|0", ms => { });
                    Assert(reply.Targets.Single().Disabled && pad.State == 0 && pad.Writes == 0, "shared pad changed");
                    SynPolicy.Run(devices, "SET|" + token + "|1", ms => { });
                    Assert(stick.State == 0 && pad.Writes == 0, "logical enable");
                }
                {
                    var stick = new FakeSyn(); var other = new FakeSyn(); string token = SynIdentity.Token(4, 2, stick.Pnp);
                    var devices = new List<SynAccess> { stick.Access(), other.Access() };
                    Assert(SynPolicy.Run(devices, "LIST", ms => { }).Targets.All(x => !x.CanSwitch), "ambiguous logical list");
                    Reject(() => SynPolicy.Run(devices, "SET|" + token + "|0", ms => { }), "duplicate logical write"); Assert(stick.Writes + other.Writes == 0, "duplicate wrote");
                }
                foreach (int scenario in Enumerable.Range(0, 4))
                {
                    var stick = new FakeSyn { Refuse = scenario == 0, Ignore = scenario == 1 }; var pad = new FakeSyn { Type = 2, Pnp = "PAD", Unreadable = scenario == 3 };
                    if (scenario == 2) stick.OnWrite = value => pad.State = value;
                    string token = SynIdentity.Token(4, 2, stick.Pnp);
                    Reject(() => SynPolicy.Run(new List<SynAccess> { stick.Access(), pad.Access() }, "SET|" + token + "|0", ms => { }), "logical fault accepted");
                    Assert(stick.State == 0 && pad.State == 0 && pad.Writes == 0, "logical rollback or pad write");
                    if (scenario == 3) Assert(stick.Writes == 0, "unreadable pad did not block");
                }
            });
            Test("single target, multiple targets, missing selection and no fallback", delegate
            {
                var a = Engine.MakeTarget(Device()); var b = Engine.MakeTarget(Device(instance: "UNIT2"));
                Assert(Selection.Resolve(new List<Target> { a }, "") == a, "unique selection");
                Reject(() => Selection.Resolve(new List<Target> { a, b }, ""), "ambiguous selection");
                Reject(() => Selection.Resolve(new List<Target> { b }, a.Token), "missing target fell back");
                Assert(Selection.Resolve(new List<Target> { a, b }, a.Token) == a, "explicit selection");
            });
            Test("round trip, per-device persistence, idempotence and unrelated mouse preservation", delegate
            {
                var b = Backend(); var s = new MemoryStore(); var e = new Engine(b, s); string token = b.Records[0].Token;
                Assert(e.Apply(token, false, true).Disabled, "disable failed"); Assert(b.Records[1].Enabled, "mouse changed");
                e.Apply(token, false, true); Assert(b.Writes == 1, "idempotent operation wrote");
                Assert(e.Apply(token, true, true).Enabled && s.Values.Single().Enabled, "enable/persist failed");
            });
            Test("driver veto, timeout and rollback leave no failed saved intent", delegate
            {
                foreach (bool ignore in new[] { false, true })
                {
                    var b = Backend(); b.FailOnce = !ignore; b.Ignore = ignore; var s = new MemoryStore(); var e = new Engine(b, s);
                    Reject(() => e.Apply(b.Records[0].Token, false, true), "veto ignored");
                    Assert(s.Values.Count == 0 && b.Records[0].Enabled, "rollback failed");
                }
            });
            Test("persistence failure prevents hardware write", delegate
            {
                var b = Backend(); var s = new MemoryStore { Fail = true }; var e = new Engine(b, s);
                Reject(() => e.Apply(b.Records[0].Token, false, true), "disk failure ignored"); Assert(b.Writes == 0, "hardware modified without durable intent");
            });
            Test("corrupt saved state cannot default a missing boolean to disabled", delegate
            {
                string token = Device().Token;
                foreach (string malformed in new[] { "null", "{}", "[{}]", "[{\"Token\":\"" + token + "\"}]", "[{\"Token\":\"" + token + "\",\"Enabled\":0}]", "[{\"Token\":\"BAD\",\"Enabled\":false}]" })
                    Reject(() => RegistryIntentStore.Parse(malformed), "corrupt saved intent accepted");
                Assert(RegistryIntentStore.Parse(Json.Write(new List<Intent> { new Intent { Token = token, Enabled = false } })).Single().Enabled == false, "valid saved intent rejected");
            });
            Test("hot unplug and driver replacement before actuation are rejected", delegate
            {
                foreach (bool replace in new[] { false, true })
                {
                    var b = Backend(); var s = new MemoryStore(); string token = b.Records[0].Token;
                    b.OnRead = current => { if (current.Reads == 2) { if (replace) current.Records[0].Driver = "2.0"; else current.Records.RemoveAt(0); } };
                    Reject(() => new Engine(b, s).Apply(token, false, true), "changed target accepted"); Assert(b.Writes == 0 && s.Values.Count == 0, "changed target modified");
                }
            });
            Test("protect keyboard descendants and rollback if another input stops", delegate
            {
                var b = Backend(); var s = new MemoryStore(); b.Records[1].ClassGuid = Safety.Keyboard; b.Records[1].Ancestors = new[] { b.Records[0].Id };
                Reject(() => new Engine(b, s).Apply(b.Records[0].Token, false, true), "keyboard child disabled"); Assert(b.Writes == 0, "unsafe topology wrote");
                b = Backend(); b.BreakNeighbor = true; s = new MemoryStore();
                Reject(() => new Engine(b, s).Apply(b.Records[0].Token, false, true), "neighbor change ignored");
                Assert(b.Records.All(x => x.Enabled) && s.Values.Count == 0, "neighbor rollback failed");
            });
            Test("reboot/wake restore and reconnect by identity; keep disconnected state; never switch another device", delegate
            {
                var b = Backend(); var s = new MemoryStore(); var e = new Engine(b, s); string token = b.Records[0].Token;
                e.Apply(token, false, true); var original = Clone(b.Records[0]); b.Records[0].Status = 8; b.Records[0].Problem = 0;
                var restarted = new Engine(b, s); Assert(restarted.Restore().Count == 0 && b.Records[0].Disabled, "restart restore");
                b.Records.RemoveAt(0); b.Records.Add(Device(instance: "OTHER"));
                Assert(restarted.Restore().Count == 1 && b.Records.All(x => x.Enabled), "missing redirect");
                b.Records.Add(original); Assert(restarted.Restore().Count == 0 && b.Records.Single(x => x.Token == token).Disabled, "reconnect restore");
            });
            Test("uninstall restores all managed devices and rejects disconnected disabled devices", delegate
            {
                var b = Backend(); var s = new MemoryStore(); var e = new Engine(b, s); string token = b.Records[0].Token;
                e.Apply(token, false, true); e.ReleaseAll(); Assert(b.Records[0].Enabled && s.Values.Count == 0, "release all");
                e.Apply(token, false, true); b.Records.RemoveAt(0); Reject(() => e.ReleaseAll(), "lost disabled device forgotten"); Assert(s.Values.Count == 1, "intent lost");
            });
            Test("service shutdown cancels remaining restoration before another hardware write", delegate
            {
                var b = Backend(); var s = new MemoryStore(); var e = new Engine(b, s);
                s.Values.Add(new Intent { Token = b.Records[0].Token, Enabled = false });
                e.Restore(() => true); Assert(b.Writes == 0 && b.Records[0].Enabled, "cancelled restore wrote hardware");
                e.Restore(() => false); Assert(b.Writes == 1 && b.Records[0].Disabled, "non-cancelled restore skipped");
            });
            Test("100 concurrent toggles serialize with reconciliation", delegate
            {
                var b = Backend(); var s = new MemoryStore(); var e = new Engine(b, s); string token = b.Records[0].Token;
                Parallel.For(0, 100, i => { e.Apply(token, null, true); e.Restore(); });
                Assert(b.Records[0].Enabled && s.Values.Single().Enabled, "concurrent toggle parity");
            });
            Test("2000 randomized enable/disable/toggle/reboot operations preserve model and unrelated inputs", delegate
            {
                var r = new Random(901); var b = Backend(); var s = new MemoryStore(); var e = new Engine(b, s); string token = b.Records[0].Token; bool expected = true;
                for (int i = 0; i < 2000; i++)
                {
                    int op = r.Next(4);
                    if (op < 3) { bool? desired = op == 0 ? (bool?)null : op == 1; expected = desired ?? !expected; e.Apply(token, desired, true); }
                    else { b.Records[0].Status = 8; b.Records[0].Problem = 0; e.Restore(); }
                    Assert(b.Records[0].Enabled == expected && b.Records[1].Enabled, "state machine divergence " + i);
                }
            });
            Test("real Windows named pipe transport: ACL, client/server PID, UTF-8, invalid commands, stalled client recovery", delegate
            {
                var b = Backend(); var s = new MemoryStore(); var e = new Engine(b, s);
                using (var host = new ServiceHost())
                {
                    string pipeName = "PointingStickSwitch.Test." + Guid.NewGuid().ToString("N");
                    Type type = typeof(ServiceHost); var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    type.GetField("listenPipe", flags).SetValue(host, pipeName);
                    type.GetField("engine", flags).SetValue(host, e);
                    type.GetField("allowedSid", flags).SetValue(host, WindowsIdentity.GetCurrent().User);
                    Exception workerError = null;
                    var worker = new Thread(() => { try { type.GetMethod("Serve", flags).Invoke(host, null); } catch (Exception ex) { workerError = ex; } }); worker.IsBackground = true; worker.Start();
                    uint pid = (uint)Process.GetCurrentProcess().Id;
                    try
                    {
                        string list = ServiceHost.Exchange(pipeName, pid, "2|LIST"); Assert(list.Contains("Dynabook"), "pipe list response");
                        string answer = ServiceHost.Exchange(pipeName, pid, "2|SET|" + b.Records[0].Token + "|0");
                        Assert(Json.Read<Target>(answer).Disabled && b.Records[1].Enabled, "pipe set response");
                        Reject(() => ServiceHost.Exchange(pipeName, pid + 1, "2|LIST"), "wrong server PID accepted");
                        Reject(() => ServiceHost.Exchange(pipeName, pid, "2|EXEC|cmd.exe"), "pipe command validation");
                        using (var stalled = new NamedPipeClientStream(".", pipeName, System.IO.Pipes.PipeAccessRights.ReadData | System.IO.Pipes.PipeAccessRights.WriteData | System.IO.Pipes.PipeAccessRights.Synchronize, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification, HandleInheritability.None))
                        { stalled.Connect(3000); Thread.Sleep(3300); }
                        Assert(ServiceHost.Exchange(pipeName, pid, "2|LIST").Contains("已关闭"), "stalled client blocked following clients");
                        using (var huge = new NamedPipeClientStream(".", pipeName, System.IO.Pipes.PipeAccessRights.ReadData | System.IO.Pipes.PipeAccessRights.WriteData | System.IO.Pipes.PipeAccessRights.Synchronize, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification, HandleInheritability.None))
                        { huge.Connect(3000); byte[] bytes = Encoding.ASCII.GetBytes(new string('A', 300) + "\n"); huge.Write(bytes, 0, bytes.Length); }
                        Assert(ServiceHost.Exchange(pipeName, pid, "2|LIST").Contains("Dynabook"), "oversized request broke listener");
                    }
                    finally { type.GetMethod("OnStop", flags).Invoke(host, null); worker.Join(5000); }
                    Assert(!worker.IsAlive && workerError == null, "pipe listener shutdown");
                }
            });
            Test("real Windows SetupAPI/ConfigManager enumeration, read-only", delegate
            {
                var records = new NativeBackend().Enumerate();
                Assert(records.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == records.Count, "duplicate native instance");
                Assert(records.All(x => x.Id.Contains("\\") && x.Hardware != null), "invalid native metadata");
                report.Add("ENV process=" + (Environment.Is64BitProcess ? "x64" : "x86") + " inputs=" + records.Count + " supported=" + records.Count(x => Catalog.Match(x) != null));
            });
            Test("2.0.1 logical transactions, malformed settings, topology and identity regressions", () => RegressionTests.Run(Assert, Reject));
            report.Add("ASSERTIONS=" + count); report.Add("RESULT=PASS");
            File.WriteAllLines(output, report, Encoding.UTF8); Console.WriteLine(String.Join(Environment.NewLine, report)); return 0;
        }
        catch (Exception ex) { report.Add("FAIL " + ex); File.WriteAllLines(output, report, Encoding.UTF8); Console.Error.WriteLine(ex); return 1; }
    }
}
