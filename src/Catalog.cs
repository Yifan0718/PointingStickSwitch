using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PointingStickSwitch
{
    internal static partial class Catalog
    {
        // The star in ACPI HID hardware IDs is literal syntax, never a wildcard.
        internal static string Canonical(string id)
        {
            string value = (id ?? "").ToUpperInvariant();
            if (Regex.IsMatch(value, @"^\*(?:LEN|IBM)[0-9A-F]{4}$")) return "ACPI\\" + value.Substring(1);
            value = value.Replace(@"HID\*", @"HID\");
            return Regex.Replace(value, @"^(HID|ACPI)\\VEN_([A-Z0-9]{3,4})&DEV_([A-F0-9]{4})", "$1\\$2$3");
        }
        private static bool InSet(DeviceRecord d, HashSet<string> ids)
        {
            int slash = d.Id.LastIndexOf('\\');
            if (slash <= 0) return false;
            string root = Canonical(d.Id.Substring(0, slash));
            return d.Hardware.Any(h => Canonical(h) == root && ids.Any(x => Canonical(x) == root));
        }
        internal static Profile Match(DeviceRecord d)
        {
            if (!d.Present || Safety.Protected(d)) return null;
            string cls = Safety.GuidText(d.ClassGuid);
            if (cls == Safety.Mouse)
            {
                if (InSet(d, dynabookIds)) return new Profile("dynabook", "Dynabook / Toshiba AccuPoint", "厂商 PSTICK INF 映射；TOSB1008 在本机验证", "独立 Col05 指点杆及此接口的按键");
                if (InSet(d, hpIds)) return new Profile("hp", "HP ELAN PointStick", "HP sp136026 官方驱动映射 · 规则测试通过", "独立 Col05 指点杆及此接口的按键");
                if (InSet(d, lenovoIds)) return new Profile("lenovo", "ThinkPad TrackPoint", "Lenovo n3fel03w 官方驱动映射 · 规则测试通过", "独立 TrackPoint · Windows PnP 控制");
                if (InSet(d, ibmlegacyIds)) return new Profile("ibmlegacy", "IBM / Lenovo PS/2 TrackPoint", "Lenovo 7bg686ww / IBM3780 · 规则测试通过", "早期独立 TrackPoint · Windows PnP 控制");
                // The product has a pointing stick and keyboard. Only the mouse TLC
                // is eligible; the USB/Bluetooth parent and keyboard TLC never are.
                string product = ExternalProduct(d);
                if (product != null) return new Profile("lenovo-keyboard", product, "Linux hid-lenovo 产品定义 · 规则测试通过", "TrackPoint 鼠标集合；独立滚轮集合由驱动管理");
            }
            if (cls == Safety.Hid && InSet(d, dellIds)) return new Profile("dell", "Dell ALPS PointStick", "Dell 05V45 PointStick Col03 · 规则测试通过", "独立 Col03 指点杆接口");
            return null;
        }
        private static string ExternalProduct(DeviceRecord d)
        {
            if (!d.Id.StartsWith("HID\\", StringComparison.OrdinalIgnoreCase) ||
                !d.Compatible.Any(x => Safety.Same(x, @"HID_DEVICE_SYSTEM_MOUSE") || Safety.Same(x, @"HID_DEVICE_UP:0001_U:0002"))) return null;
            foreach (string pid in new[] { "6009", "6047", "6048", "60EE", "60E1" })
            {
                string pattern = @"^HID\\VID_17EF&PID_" + pid + @"(?:&(?!.*(?:UP:000D|U:0006))[^\\]*)?$";
                // Bluetooth hardware IDs may encode the vendor-ID source separately.
                string bluetooth = @"^HID\\\{00001124-0000-1000-8000-00805F9B34FB\}_VID&000217EF_PID&" + pid + @"(?:&[^\\]*)?$";
                int split = d.Id.LastIndexOf('\\');
                if (split < 0) continue;
                string instanceRoot = d.Id.Substring(0, split);
                if (!(Regex.IsMatch(instanceRoot, pattern, RegexOptions.IgnoreCase) || Regex.IsMatch(instanceRoot, bluetooth, RegexOptions.IgnoreCase))) continue;
                if (d.Hardware.Any(x => Regex.IsMatch(x, pattern, RegexOptions.IgnoreCase) || Regex.IsMatch(x, bluetooth, RegexOptions.IgnoreCase)))
                    return "Lenovo TrackPoint Keyboard · " + pid;
            }
            return null;
        }
    }
}
