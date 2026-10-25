using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using PointingStickSwitch;

internal static class RegressionTests
{
    private sealed class Store : ILogicalStore
    {
        internal readonly List<Intent> Values = new List<Intent>();
        internal int Writes, FailOn;
        internal bool FailAfterWrite;
        public List<Intent> Read() { return Values.Select(x => new Intent { Token = x.Token, Enabled = x.Enabled }).ToList(); }
        public void Write(string token, bool? enabled)
        {
            Writes++;
            if (Writes == FailOn && !FailAfterWrite) throw new IOException("storage failure");
            Values.RemoveAll(x => x.Token == token);
            if (enabled.HasValue) Values.Add(new Intent { Token = token, Enabled = enabled.Value });
            if (Writes == FailOn && FailAfterWrite) throw new IOException("flush failure");
        }
    }
    private sealed class Device
    {
        internal int Disabled, Writes, Connection = 2;
        internal bool FailNext, ChangeIdentity;
        internal readonly Store Store = new Store();
        internal string Token { get { return SynIdentity.Token(4, 2, "ACPI\\TEST_STICK"); } }
        internal SynReply Call(string command)
        {
            var access = new SynAccess { Type = () => 4, Connection = () => Connection, Pnp = () => "ACPI\\TEST_STICK", Read = () => Disabled,
                Write = value => { Writes++; Disabled = value; if (ChangeIdentity) Connection++; } };
            var reply = SynPolicy.Run(new List<SynAccess> { access }, command, n => { });
            if (command.StartsWith("SET|") && FailNext) { FailNext = false; reply.Error = "injected post-write failure"; }
            return reply;
        }
        internal LogicalController Controller { get { return new LogicalController(Call, Store); } }
    }
    private sealed class TopologyBackend : IDeviceBackend, IIntentStore
    {
        internal int Reads, Writes;
        internal readonly DeviceRecord Stick = new DeviceRecord { Id = "HID\\TOSB1008&COL05\\TEST", Hardware = new[] { "HID\\TOSB1008&COL05" }, ClassGuid = Safety.Mouse, Status = 8 };
        public List<DeviceRecord> Enumerate()
        {
            var list = new List<DeviceRecord> { Stick };
            if (++Reads >= 2) list.Add(new DeviceRecord { Id = "HID\\KEYBOARD\\TEST", ClassGuid = Safety.Keyboard, Status = 8, Ancestors = new[] { Stick.Id } });
            return list;
        }
        public void Set(DeviceRecord d, bool enabled) { Writes++; }
        public void Wait(int n) { }
        public List<Intent> Read() { return new List<Intent>(); }
        public void Write(List<Intent> values) { }
    }
    internal static void Run(Action<bool, string> assert, Action<Action, string> reject)
    {
        var d = new Device();
        assert(d.Controller.Apply(d.Token, null, true).Disabled, "first logical toggle");
        assert(d.Controller.Apply(d.Token, null, true).Enabled, "second toggle must read fresh state");
        assert(d.Store.Read().Single().Enabled, "toggle persists actual desired state");
        d.Controller.Apply(d.Token, false, false);
        assert(d.Disabled == 0, "stale agent snapshot must not overwrite newer enabled intent");
        d.Store.Values.Clear(); int writes = d.Writes;
        assert(d.Controller.Apply(d.Token, false, false) == null && d.Writes == writes, "removed preference is not restored");

        foreach (bool partial in new[] { false, true })
        {
            d = new Device(); d.Store.FailOn = 1; d.Store.FailAfterWrite = partial;
            reject(() => d.Controller.Apply(d.Token, false, true), "storage failure must be visible");
            assert(d.Writes == 0 && d.Store.Values.Count == 0, "storage failure must undo intent before hardware");
        }
        d = new Device(); d.FailNext = true;
        reject(() => d.Controller.Apply(d.Token, false, true), "post-write failure rejected");
        assert(d.Disabled == 0 && d.Writes == 2 && d.Store.Values.Count == 0, "hardware and preference rollback");
        d = new Device(); d.FailNext = true; d.Store.FailOn = 2;
        reject(() => d.Controller.Apply(d.Token, false, true), "storage rollback failure reported");
        assert(d.Disabled == 0 && d.Writes == 2, "hardware rollback still attempted when storage rollback fails");
        d = new Device(); d.ChangeIdentity = true;
        reject(() => d.Call("SET|" + d.Token + "|0"), "identity changed during confirmation");
        assert(d.Writes == 1, "identity replacement must not receive a rollback write");
        assert(UserSettings.ParseLogicalValue(0, RegistryValueKind.DWord) == false, "valid disabled DWORD");
        assert(UserSettings.ParseLogicalValue(1, RegistryValueKind.DWord), "valid enabled DWORD");
        foreach (object value in new object[] { null, "0", "garbage", 2, -1, new byte[] { 0 }, 1L })
            reject(() => UserSettings.ParseLogicalValue(value, RegistryValueKind.DWord), "corrupt logical state accepted");
        reject(() => UserSettings.ParseLogicalValue(1, RegistryValueKind.String), "wrong registry kind");
        var topology = new TopologyBackend();
        reject(() => new Engine(topology, topology).Apply(topology.Stick.Token, false, true), "new keyboard descendant must prevent disable");
        assert(topology.Writes == 0, "new protected descendant touched hardware");
    }
}
