using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using PointingStickSwitch;

internal static class HardwareChecks
{
    private static void Require(bool ok, string text) { if (!ok) throw new Exception(text); }
    private static void WaitFor(string token, bool enabled)
    {
        for (int i = 0; i < 60; i++)
        { var d = Engine.Resolve(new NativeBackend().Enumerate(), token); if (enabled ? d.Enabled : d.Disabled) return; Thread.Sleep(150); }
        throw new Exception("state confirmation timeout");
    }
    private static int Main(string[] args)
    {
        var report = new List<string>(); DeviceRecord target = null; bool passed = false;
        try
        {
            if (!Program.IsAdmin) throw new Exception("Hardware test requires elevated service-restart access");
            var before = new NativeBackend().Enumerate();
            target = before.Single(d => d.Hardware.Any(x => Catalog.Canonical(x) == @"HID\TOSB1008&COL05") && Catalog.Match(d) != null);
            Require(target.Healthy, "target abnormal before test");
            Require(target.Disabled && new RegistryIntentStore().Read().Any(x => x.Token == target.Token && !x.Enabled), "old disabled preference did not migrate");
            foreach (bool enabled in new[] { true, false })
            {
                var answer = Json.Read<Target>(ServiceHost.Request("2|SET|" + target.Token + "|" + (enabled ? "1" : "0")));
                Require(answer.Enabled == enabled && answer.CanSwitch, "service response mismatch");
                WaitFor(target.Token, enabled);
                Require(!Safety.OtherInputsChanged(before, new NativeBackend().Enumerate(), target), "unrelated input changed");
                report.Add("PASS v2 real SYSTEM-service PnP enabled=" + enabled + "; other " + (before.Count - 1) + " inputs unchanged");
            }
            using (var controller = new ServiceController(ServiceHost.Name))
            {
                controller.Stop(); controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                var current = Engine.Resolve(new NativeBackend().Enumerate(), target.Token);
                new NativeBackend().Set(current, true); WaitFor(target.Token, true);
                controller.Start(); controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
                WaitFor(target.Token, false);
                report.Add("PASS service restart reconciles saved disabled intent after external state change");
            }
            Require(!Safety.OtherInputsChanged(before, new NativeBackend().Enumerate(), target), "input changed after restart");
            passed = true;
        }
        catch (Exception ex) { report.Add("FAIL " + ex); }
        finally
        {
            if (target != null)
            {
                try
                {
                    if (!ServiceHost.Running) ServiceHost.ResumeService();
                    ServiceHost.Request("2|SET|" + target.Token + "|" + (target.Enabled ? "1" : "0"));
                    WaitFor(target.Token, target.Enabled); report.Add("RESTORED original enabled=" + target.Enabled);
                }
                catch (Exception ex) { passed = false; report.Add("RESTORE FAILED " + ex); }
            }
            report.Add("RESULT=" + (passed ? "PASS" : "FAIL")); File.WriteAllLines(args[0], report, Encoding.UTF8);
        }
        return passed ? 0 : 1;
    }
}
