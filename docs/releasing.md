# 发布流程

1. 更新 `src/Version.cs`、README 下载文件名、安装说明与 CHANGELOG，运行 `scripts/verify.ps1`。
2. 提交并推送到 `main`，等待该提交的 `Windows checks` 通过。安装、升级与卸载测试在一次性 GitHub Windows 虚拟机执行。
3. 下载对应提交的 `windows-verification` artifact，将安装 EXE 和 `tests/installer-ci.txt` 放入本地 `artifacts/release/`。
4. 在已提交的源码工作区运行 `scripts/package-release.ps1`。脚本核对 PASS 报告、源码提交、安装包 SHA-256 和版本，使用 `git archive` 导出源码 ZIP。
5. 创建版本标签和 GitHub Release，上传安装 EXE、源码 ZIP、`installer-ci.txt`、`build-info.json` 和 `SHA256SUMS.txt`。
6. 重新下载 GitHub 资产，核对校验值与发布标签所指向的提交。

`scripts/install-build-tools.ps1` 从官方渠道获取 Inno Setup 6.7.3，核对固定 SHA-256 和 Authenticode 签名。发行文件的签名状态记录在 `build-info.json`。Framework 编译器和 Inno Setup 在构建时生成时间相关元数据。
