# PointingStickSwitch · 指点杆开关

[![Windows checks](https://github.com/Yifan0718/PointingStickSwitch/actions/workflows/windows.yml/badge.svg)](https://github.com/Yifan0718/PointingStickSwitch/actions/workflows/windows.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Windows 指点杆一键开关。安装后通过桌面图标启用或关闭指点杆，日常操作免 UAC，自动保存和恢复状态。按硬件接口识别设备，同一接口的不同品牌、地区和商品型号共用识别规则。

**[下载单文件安装程序](https://github.com/Yifan0718/PointingStickSwitch/releases/latest)** · [兼容性与资料](docs/compatibility.md) · [测试](docs/testing.md) · [更新记录](CHANGELOG.md)

## 安装和使用

1. 下载 `PointingStickSwitch-Setup-2.0.2.exe`。运行环境：Windows 10 1703+ / Windows 11、x86/x64、.NET Framework 4.8。
2. 运行安装程序并完成管理员授权。安装包包含主程序、32 位厂商接口辅助程序、后台服务和说明。
3. 打开桌面的「指点杆控制面板」。单设备自动选择，多设备手动选择。
4. 点击桌面的「一键切换指点杆」，或使用控制面板的启用 / 关闭按钮。

旧版 AccuPointSwitch 支持原地升级。需要查看设备信息时，点击「导出诊断」。

## 接口支持

| 接口 | 识别与验证 |
| --- | --- |
| Dynabook / Toshiba AccuPoint 独立 HID Col05 | 官方驱动映射与模拟测试；X30L-J / TOSB1008 实机验证 |
| ThinkPad 独立 TrackPoint、早期 IBM3780 | 官方驱动映射与正反例测试 |
| HP ELAN Col05、Dell ALPS Col03 PointStick | 官方驱动映射与正反例测试 |
| Lenovo 外接 TrackPoint 键盘的 5 个产品 ID | 鼠标集合识别与规则测试 |
| Synaptics 类型 4/5 逻辑指点杆 | 逻辑接口策略测试；本机 64 位 SynCOM 实机验证 |

设备目录包含 167 条官方 INF 模型行，归一化后为 98 个独立接口标识；外接键盘另计 5 个产品 ID。共享接口和目录外设备提供诊断模式。硬件 ID、驱动来源和验证方式见[兼容性文档](docs/compatibility.md)。

## 状态保存

- PnP 接口由后台服务控制，按设备实例保存状态，在开机、唤醒和运行中定期恢复。
- Synaptics 逻辑接口通过普通用户进程调用现有厂商驱动，按用户保存状态，登录后约 30 秒内恢复。
- 多设备使用固定选择。设备断开后保留选择和状态，重新配对或更换端口后可在控制面板重新选择。
- 卸载前接回已关闭设备，并在各用户账户启用 Synaptics 逻辑指点杆。卸载程序恢复服务管理的 PnP 设备。

设备识别和状态保存均在本地完成。诊断文件由用户导出，包含设备型号、硬件 ID、驱动资料及经过哈希处理的实例标识。

## 构建与测试

C# / WinForms / .NET Framework 4.8，使用系统 Framework 编译器。构建脚本支持 Windows PowerShell 5.1 和 PowerShell 7。

```powershell
.\src\build.ps1
.\scripts\verify.ps1
.\src\build-installer.ps1 -IsccPath 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
```

构建产物位于 `artifacts/bin/`，安装包位于 `artifacts/release/`，测试结果位于 `artifacts/tests/`。版本号维护在 `src/Version.cs`，安装包使用 Inno Setup 6.7.3 构建。

x64/x86 测试各包含 13,670 项断言，布局测试覆盖 24 组 100%–300% 字体与控件缩放场景。GitHub Actions 在干净 Windows 虚拟机完成安装、升级、卸载和桌面快捷方式检查。各版本记录保存在 `evidence/`。

Release 提供单文件安装包、源码 ZIP、安装测试报告、构建信息和 SHA-256 校验清单。安装包直接取自通过测试的 CI 产物，发布流程见 [docs/releasing.md](docs/releasing.md)。

```powershell
Get-FileHash .\PointingStickSwitch-Setup-2.0.2.exe -Algorithm SHA256
```

```text
src/         应用、服务、接口策略、安装脚本
tests/       硬件资料夹具与回归测试
scripts/     验证、构建工具和发布打包
docs/        兼容性、资料出处和测试说明
evidence/    按版本保存的验证结果
licenses/    第三方许可证
```

![长设备名的布局测试示例](docs/layout-example.png)

## English

A Windows pointing-stick switch with desktop shortcuts, persistent device state and routine operation without UAC after installation. Devices are identified by their hardware interfaces. The application uses independent PnP interfaces and vendor-provided Synaptics logical-stick APIs.

The compatibility table records driver sources, automated tests and hardware validation. Shared interfaces and devices outside the catalog offer diagnostic export. The application UI is Simplified Chinese; the installer also offers English and Japanese.

## 许可与贡献

项目使用 [MIT](LICENSE) 许可。第三方材料见 [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES.md)，新增硬件与贡献流程见 [CONTRIBUTING](CONTRIBUTING.md)。
