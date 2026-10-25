# 指点杆开关 2.0：兼容性与验证

这是 Windows 多接口指点杆开关，不是所有设备均已实测的“万能驱动”。程序不按电脑商品名限制安装。日版、海外版、更名机型只要使用同一已确认接口，就走相同的识别规则。未知硬件可以安装、查看与导出诊断，但不能关闭未知接口。

## 已实现的控制路径

| 设备族 | 识别依据与控制方式 | 证据与验证等级 |
|---|---|---|
| Dynabook / Toshiba AccuPoint | TOSB1002/1004/1005/1006/1007/1008/1009/100A、DNBK1003/1004 的 HID Col05；独立 PnP 禁用 | 本机 X30L-J / TOSB1008 实测；其他 ID 来源于 Synaptics 19.5.32.154 驱动 PSTICK 映射，模拟验证 |
| ThinkPad 较新独立 TrackPoint | 官方 EPD.inf 中实际列出的 59 个 LEN03xx ACPI ID；含 ELAN、ALPS、Synaptics、JYT 变体 | Lenovo n3fel03w 31.21.60.1；逐条正反例测试，未逐机实测；不按连续 ID 范围匹配 |
| IBM / Lenovo 早期独立 TrackPoint | IBM3780，来自 TP4TRACK.INF | 4.73.1 官方驱动，涉及 X60/X61/X200/X201 等系列；只证明接口归属，Windows 10/11 下能否 PnP 禁用仍待实机 |
| HP PointStick | ELAN0720/0721/0744/0745/0747/0748/074F/0750 的 Col05 | HP sp136026 32.2.6.1；包含部分 EliteBook / ZBook，逐条模拟验证，未实机 |
| Dell PointStick | 20 个 DELLxxxx 的 Col03；HIDClass 类型 | Dell 05V45 / ALPS 10.3201.101.312；Latitude 5400/5410/5411、5500/5510/5511、部分 Precision 等驱动映射，未实机；名称可能仍为 Touchpad |
| Lenovo 外接 TrackPoint 键盘 | VID 17EF，PID 6009/6047/6048/60EE/60E1；同时要求 HID 鼠标类与鼠标用途 | Linux hid-lenovo 产品定义，Windows 规则模拟验证；不禁用 USB/蓝牙父节点或键盘集合；蓝牙 ID 形态及重连待实机 |
| 旧式 Synaptics 逻辑指点杆，品牌不限 | 现有厂商 SynCOM 接口报告 DeviceIBMCompatibleStick=4 或 DeviceStyk=5；只写该逻辑设备 DisableState | 本机 Synaptics HIDPD 实际开关及读回通过。PS/2、SMBus、I2C、RMIHID 等连接类型通过策略模拟，不能据此宣称对应驱动实测 |

`tests/official-models.csv` 保存 167 条官方 INF 模型行的事实字段（含架构重复项和硬件 ID 别名），对应 98 个规范化独立接口标识；不等于 167 台实测电脑。外接键盘另计 5 个产品 ID。[`sources.json`](sources.json) 记录 INF 哈希、版本和来源。

## 为什么有些设备只能诊断

旧 ALPS DualPoint、部分 Synaptics UltraNav 会把触摸板和指点杆复用到同一个 PS/2 设备。关闭 ACPI 父节点或通用 PNP0F13 会影响整个指向设备。程序不这样做。只有厂商逻辑接口明确暴露独立指点杆时，才尝试该路径。

Fujitsu LifeBook QuickPoint、HP/Compaq EasyPoint IV、Sony VAIO 的历史型号，以及 TEX Shinobi/Shura/Yoda、PFU HHKB Studio 都属于相关产品范围，但商标、红点外观、USB 鼠标用途不能证明独立控制能力。当前没有经过验证的专用适配器时，保留诊断模式；HHKB 同时有手势板，更不能把整个鼠标接口直接视为单独指点杆。未安装兼容厂商驱动、BIOS 已禁用、驱动拒绝 PnP 禁用，也可能无法切换。

没有注入键盘钩子、拦截全部鼠标、安装内核过滤驱动或修改未知驱动注册表开关。程序不能保证每种设备的独立按键、中键滚动都会跟着停止；界面会显示控制范围。

## 权限、状态与多个设备

- 安装/修复/卸载需要一次 Windows 管理员授权。普通切换通过权限受限的本地服务管道，或者普通用户的 SynCOM 子进程完成。
- PnP 由 LocalSystem 服务控制。只接受固定协议与已识别设备的 SHA-256 标识；不接受程序路径、脚本、注册表路径或任意 DEVINST。拒绝网络登录访问；客户端校验服务进程 PID。
- 按设备实例记录期望状态。开机、唤醒及运行中每约 30 秒核对一次，设备断开时保留记录。更换 USB 端口或配对后，Windows 可能分配新实例，此时需重新选择，不会按商品名误认。
- SynCOM 在普通用户的隔离 STA 进程中调用，8 秒超时。32 位 COM 可由 x86 辅助进程探测；本机仅有可用的 64 位注册，因此 32 位真实厂商调用仍未验证。服务从不加载用户 COM。
- SynCOM 状态按用户保存。安装后登录启动普通用户恢复进程；登录后最多约 30 秒恢复，不能保证登录屏幕之前生效。多个用户同时操作同一物理设备时，最后一次硬件写入生效，不声称跨用户偏好隔离。
- 多设备需要先选择；已选设备消失时停止操作，不自动选择另一个。正常操作使用显式目标状态，并串行处理切换、保存与恢复。
- 新版沿用旧版服务名及安装目录以便升级；只把旧版 DesiredEnabled 迁移到唯一的 TOSB1008，不把旧偏好套用到其他品牌。
- 卸载会恢复已记录且连接着的 PnP 设备；有断开的已关闭设备时要求先连接。卸载前请在各用户账户恢复其 Synaptics 逻辑指点杆。当前用户仍有关闭记录时，卸载会停止并说明原因。

## 不依赖其他物理机的验证

测试直接编译生产代码，通过设备后端替换重放硬件记录和注入故障；并不是另写一套“总能成功”的模拟程序。

1. 逐条重放官方 INF 硬件映射，验证别名和大小写，再注入键盘类别、触摸板用途、错误集合、错误实例根、离线状态和 ID 后缀。
2. 拒绝旧共享 PS/2 ID、Dell 通用虚拟鼠标和测试 ID、USB/蓝牙父节点、键盘集合。
3. 10,000 个畸形协议输入；2,000 次随机启用/禁用/切换/重启恢复；100 次并发切换与恢复。
4. 注入驱动拒绝、没有状态变化、存储失败、操作前热拔出、驱动身份变化、其他输入设备停止，检查保存回滚与硬件恢复。
5. SynCOM 使用同一生产控制策略模拟类型 4/5、六类连接、共享触摸板、重复身份、属性不可读、拒绝写入、超时与连带修改。任何情况下都不会向触摸板写 DisableState。
6. 在真实 Windows 命名管道上运行生产服务循环，测试普通用户 ACL、错误服务 PID、UTF-8、非法命令、不发送完整请求、超长请求和继续服务。此测试发现并修复了旧 .NET 异步等待句柄在超时时可能令整个进程崩溃的问题。
7. 同一测试套件在本机 x64 和 x86 进程分别运行，并真实调用 SetupAPI / ConfigManager 只读枚举核对结构体与设备元数据。
8. WinForms 按 100%、125%、150%、200%、250%、300% 放大字体与控件，在 620/360 逻辑宽度下检查文字测量、重叠、窄屏按钮换行；另做 12 组只放大字体、保持窗口尺寸的验证。内容超出屏幕时可以滚动。发现并移除了大字体时重叠的原生下拉框。属于渲染/几何模拟，并非在多台显示器上实际切换 DPI。

2.0.0 历史结果在 `evidence/v2.0.0/`，每套 13,643 项断言；2.0.1 回归结果另见 `evidence/v2.0.1/`。测试数量不是实机数量。本机 Synaptics 新控制路径的关闭、启用和读回已通过。新版服务实际 PnP 往返未改变其余 15 个输入设备，服务重启能恢复保存状态。安装升级、卸载、重装及普通用户无 UAC 切换以对应报告为准。测试最后恢复原来的 PnP 关闭状态。

这些测试可以排除识别、状态机、权限边界与大量失败处理错误；无法模拟未知厂商内核驱动、EC/固件、实际指针位移、睡眠固件和所有设备的蓝牙重连。资料支持与模拟通过的设备仍需要用户在该设备上核对指点杆、触摸板、键盘和按键的实际行为。不能把虚拟机上的通用 PS/2 鼠标当成对应品牌指点杆的实测替代品。

## 复现

源码为 .NET Framework 4.8，无第三方运行时依赖，安装包不分发厂商驱动。

```powershell
# Windows PowerShell / PowerShell，先安装 .NET Framework 4.8
.\src\build.ps1
.\tests\run-tests.ps1
.\scripts\verify.ps1
.\artifacts\bin\TrackPointSwitch.exe --diagnose --output .\diagnostic.json --quiet
# Inno Setup 6 编译器路径由调用者提供
.\src\build-installer.ps1 -IsccPath 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
```

设备诊断默认以哈希替代完整实例后缀；型号、硬件 ID、驱动版本等仍包含在文件中。没有自动上传。新增型号应附经检查并脱敏的诊断、厂商驱动出处、独立接口证据及正反例测试，不能只添加名称关键词。

## 主要资料

- [Microsoft：CM_Disable_DevNode 与 Windows 10 持久化标记](https://learn.microsoft.com/en-us/windows/win32/api/cfgmgr32/nf-cfgmgr32-cm_disable_devnode)
- [Microsoft：HID 顶层集合](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/top-level-collections)；[键盘与鼠标集合](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/keyboard-and-mouse-hid-client-drivers)
- [Lenovo EPD 31.21.60.1](https://support.lenovo.com/us/en/downloads/ds558259-elan-trackpoint-driver-for-windows-11-version-21h2-or-later-10-version-20h2-or-later-thinkpad-p16-gen-1-type-21d6-21d7)
- [Lenovo 早期 TrackPoint 4.73.1](https://support.lenovo.com/uu/en/downloads/ds003654-trackpoint-driver-for-windows-7-64-bit-vista-64-bit-thinkpad)
- [Lenovo T480 Synaptics UltraNav](https://pcsupport.lenovo.com/lc/en/products/laptops-and-netbooks/thinkpad-t-series-laptops/thinkpad-t480-type-20l5-20l6/downloads/ds502229-synaptics-ultranav-driver-for-windows-10-64-bit-thinkpad)
- [HP PointStick sp136026](https://ftp.hp.com/pub/softpaq/sp136001-136500/sp136026.html)
- [Dell PointStick 05V45](https://www.dell.com/support/home/en-us/drivers/driversdetails?driverid=05v45)
- Synaptics COM 类型与属性来自本机已安装 SynCOM.dll 的类型库，未加载下载包中的 DLL；只使用 DeviceType、ConnectionType、PnPID、DisableState。没有假定 PersistState 在所有驱动中含义相同。
- [Linux：TrackPoint 芯片变体](https://github.com/torvalds/linux/blob/master/drivers/input/mouse/trackpoint.h)、[Lenovo HID 定义](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-lenovo.c)、[ALPS 共享协议](https://www.kernel.org/doc/html/latest/input/devices/alps.html)。Linux 的协议资料仅用于识别与理解，不把 Linux 控制方法直接当成 Windows 接口。
- [Fujitsu S751 QuickPoint 规格](https://www.fujitsu.com/downloads/COMP/fpcap/notebooks/previous/factsheet_S751.pdf)、[HP/Compaq EasyPoint IV 手册](https://h10032.www1.hp.com/ctg/Manual/c01123946.pdf)、[TEX Shura 原厂资料](https://tex.com.tw/blogs/manufacture/tex-shura-diy-build-guide)、[HHKB Studio 原厂 FAQ](https://hhkeyboard.us/en/support/faq/hhkb-studio)
