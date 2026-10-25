# 接口兼容性与验证

PointingStickSwitch 根据设备接口选择控制方式。日版、海外版和更名机型使用相同接口时，共用识别规则。

## 控制接口

| 设备族 | 识别与控制方式 | 资料及验证 |
| --- | --- | --- |
| Dynabook / Toshiba AccuPoint | TOSB1002/1004/1005/1006/1007/1008/1009/100A、DNBK1003/1004 的 HID Col05；PnP 控制 | Synaptics 19.5.32.154 PSTICK 映射与模拟测试；X30L-J / TOSB1008 实机验证 |
| ThinkPad 独立 TrackPoint | EPD.inf 列出的 59 个 LEN03xx ACPI ID，含 ELAN、ALPS、Synaptics、JYT 变体 | Lenovo n3fel03w 31.21.60.1；逐条正反例测试 |
| IBM / Lenovo 早期 TrackPoint | TP4TRACK.INF 中的 IBM3780；PnP 控制 | 4.73.1 官方驱动与规则测试，相关系列包括 X60/X61/X200/X201 |
| HP PointStick | ELAN0720/0721/0744/0745/0747/0748/074F/0750 的 Col05 | HP sp136026 32.2.6.1；部分 EliteBook / ZBook 驱动映射与逐条模拟测试 |
| Dell PointStick | 20 个 DELLxxxx 的 Col03，HIDClass 类型 | Dell 05V45 / ALPS 10.3201.101.312；Latitude 5400/5410/5411、5500/5510/5511、部分 Precision 驱动映射与模拟测试 |
| Lenovo 外接 TrackPoint 键盘 | VID 17EF，PID 6009/6047/6048/60EE/60E1；HID 鼠标类及鼠标用途集合 | Linux hid-lenovo 产品定义与 Windows 规则模拟测试 |
| Synaptics 逻辑指点杆 | SynCOM 类型 DeviceIBMCompatibleStick=4 或 DeviceStyk=5；逻辑设备 DisableState | 本机 HIDPD 实际开关与读回；PS/2、SMBus、I2C、RMIHID 等连接类型的策略模拟 |

`tests/official-models.csv` 保存 167 条官方 INF 模型行，包含架构项和硬件 ID 别名，归一化后为 98 个独立接口标识。外接键盘另计 5 个产品 ID。[sources.json](sources.json) 记录 INF 哈希、版本和来源。

## 共享接口与诊断模式

旧 ALPS DualPoint 和部分 Synaptics UltraNav 将触摸板与指点杆复用到一个 PS/2 设备。程序通过厂商 SynCOM 提供的独立逻辑指点杆进行控制；其余共享接口使用诊断模式。

Fujitsu LifeBook QuickPoint、HP/Compaq EasyPoint IV、Sony VAIO 历史型号、TEX Shinobi/Shura/Yoda、PFU HHKB Studio，以及目录外设备可使用诊断导出。新增适配按驱动映射、接口属性和设备拓扑建立识别规则。

控制功能需要 BIOS 中启用设备、厂商驱动可用且接口支持状态切换。界面显示当前接口的控制范围；按键和滚轮归属由驱动的接口划分决定。

## 权限与状态

- 安装、修复和卸载使用 Windows 管理员授权。日常切换使用本地服务或普通用户 SynCOM 进程。
- PnP 后台服务使用 LocalSystem，接收固定协议与设备 SHA-256 标识。命名管道使用本地访问控制，客户端校验服务进程 PID。
- PnP 状态按设备实例保存，在开机、唤醒及运行中每约 30 秒核对。断开后保留记录；换端口或重新配对产生新实例时重新选择。
- SynCOM 在普通用户的隔离 STA 进程中运行，调用超时为 8 秒，x86 辅助进程负责 32 位接口。
- SynCOM 偏好按用户保存，登录后约 30 秒内恢复。多个用户操作同一设备时，最后一次硬件写入生效。
- 多设备先选定目标。已选设备断开后保留选择，连接设备或重新选择后继续操作。切换、保存和恢复串行执行。
- 升级沿用 AccuPointSwitch 服务名及安装目录。旧版 DesiredEnabled 迁移规则对应唯一的 TOSB1008 设备。
- 卸载前接回已关闭的设备，并在各用户账户启用 Synaptics 逻辑指点杆。卸载程序恢复已记录的 PnP 设备并移除服务。

## 测试方式

测试直接编译生产代码，通过模拟后端重放硬件记录和注入故障。

1. 逐条验证官方 INF 映射、别名与大小写，并检查键盘、触摸板、集合、实例根、离线状态和 ID 后缀反例。
2. 覆盖共享 PS/2、虚拟鼠标、USB/蓝牙父节点及键盘集合的识别规则。
3. 运行 10,000 个畸形协议输入、2,000 次随机状态操作，以及 100 次并发切换和恢复。
4. 注入驱动拒绝、状态超时、存储失败、热拔出、身份变化和关联输入设备变化，检查回滚与恢复。
5. SynCOM 策略模拟覆盖类型 4/5、六类连接、共享触摸板、重复身份、属性读取、写入故障与状态恢复。
6. 真实 Windows 命名管道测试覆盖 ACL、服务 PID、UTF-8、协议、超时及后续请求。
7. x64/x86 测试进程调用 SetupAPI / ConfigManager 进行只读枚举。
8. 布局测试模拟 100%、125%、150%、200%、250%、300% 缩放，在 620/360 逻辑宽度下测量文字和控件；同时运行保持窗口尺寸的字体缩放测试，共 24 组。

实机记录对应 Dynabook X30L-J / TOSB1008：SynCOM 开关与读回、服务 PnP 往返、其余 15 个输入设备状态核对，以及服务重启恢复。测试结束后恢复初始状态。

各版本的自动化、实机和安装记录保存在 `evidence/`，运行方法见 [testing.md](testing.md)。

## 复现

运行环境为 .NET Framework 4.8，厂商接口由系统已安装的驱动提供。

```powershell
.\src\build.ps1
.\scripts\verify.ps1
.\artifacts\bin\TrackPointSwitch.exe --diagnose --output .\diagnostic.json --quiet
.\src\build-installer.ps1 -IsccPath 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
```

诊断文件记录设备型号、硬件 ID、驱动版本和经过哈希处理的实例标识，保存到用户选择的位置。新增型号所需资料见 [CONTRIBUTING](../CONTRIBUTING.md)。

## 主要资料

- [Microsoft：CM_Disable_DevNode 与 Windows 10 持久化标记](https://learn.microsoft.com/en-us/windows/win32/api/cfgmgr32/nf-cfgmgr32-cm_disable_devnode)
- [Microsoft：HID 顶层集合](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/top-level-collections)；[键盘与鼠标集合](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/keyboard-and-mouse-hid-client-drivers)
- [Lenovo EPD 31.21.60.1](https://support.lenovo.com/us/en/downloads/ds558259-elan-trackpoint-driver-for-windows-11-version-21h2-or-later-10-version-20h2-or-later-thinkpad-p16-gen-1-type-21d6-21d7)
- [Lenovo 早期 TrackPoint 4.73.1](https://support.lenovo.com/uu/en/downloads/ds003654-trackpoint-driver-for-windows-7-64-bit-vista-64-bit-thinkpad)
- [Lenovo T480 Synaptics UltraNav](https://pcsupport.lenovo.com/lc/en/products/laptops-and-netbooks/thinkpad-t-series-laptops/thinkpad-t480-type-20l5-20l6/downloads/ds502229-synaptics-ultranav-driver-for-windows-10-64-bit-thinkpad)
- [HP PointStick sp136026](https://ftp.hp.com/pub/softpaq/sp136001-136500/sp136026.html)
- [Dell PointStick 05V45](https://www.dell.com/support/home/en-us/drivers/driversdetails?driverid=05v45)
- Synaptics COM 类型与属性来自本机已安装 SynCOM.dll 的类型库，使用 DeviceType、ConnectionType、PnPID、DisableState。
- [Linux：TrackPoint 芯片变体](https://github.com/torvalds/linux/blob/master/drivers/input/mouse/trackpoint.h)、[Lenovo HID 定义](https://github.com/torvalds/linux/blob/master/drivers/hid/hid-lenovo.c)、[ALPS 共享协议](https://www.kernel.org/doc/html/latest/input/devices/alps.html)。Linux 协议资料用于设备识别与接口研究。
- [Fujitsu S751 QuickPoint 规格](https://www.fujitsu.com/downloads/COMP/fpcap/notebooks/previous/factsheet_S751.pdf)、[HP/Compaq EasyPoint IV 手册](https://h10032.www1.hp.com/ctg/Manual/c01123946.pdf)、[TEX Shura 原厂资料](https://tex.com.tw/blogs/manufacture/tex-shura-diy-build-guide)、[HHKB Studio 原厂 FAQ](https://hhkeyboard.us/en/support/faq/hhkb-studio)
