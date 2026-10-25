# 发布流程

1. 更新 `src/Version.cs`、README 下载文件名、安装说明与 CHANGELOG。运行 `scripts/verify.ps1`，确认适用的人工检查及文档范围。
2. 提交并推送到 `main`，等待该提交的 `Windows checks` 全部通过。该流程在一次性 Windows 虚拟机安装、升级和卸载程序，不应在日常电脑上强行绕过 `test-installer-ci.ps1` 的环境检查。
3. 使用 GitHub CLI 下载该提交的 `windows-verification` artifact。将其 `release/PointingStickSwitch-Setup-<version>.exe` 与 `tests/installer-ci.txt` 放入本地 `artifacts/release/`。不要重新编译后把不同的安装包替换进去。
4. 在干净、已提交的对应源码工作区运行 `scripts/package-release.ps1`。脚本要求安装报告 PASS，并核对报告中的完整源码提交、安装包 SHA-256 和版本。源码 ZIP 使用 `git archive` 导出，不包含工作目录中的构建产物、诊断或下载包。
5. 创建对应版本标签和 GitHub Release，上传安装 EXE、源码 ZIP、`installer-ci.txt`、`build-info.json` 和 `SHA256SUMS.txt`。
6. 从 GitHub 重新下载资产并核对校验值，核对发布标签指向的提交。不要把资料支持、模拟通过、CI 通过写成其他品牌实机验证。

构建工具由 `scripts/install-build-tools.ps1` 下载官方 Inno Setup 6.7.3，并核对固定 SHA-256 与 Authenticode 签名。安装包自身暂未代码签名。Framework 编译器和 Inno Setup 会生成时间相关元数据，因此本项目提供可重复运行的构建流程，并不宣称在不同机器上得到逐字节相同的 EXE。
