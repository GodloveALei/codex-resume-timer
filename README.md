# Codex 定时继续 · Codex Resume Timer

一个便携的 Windows 小工具，在指定时间向已打开的 Codex 桌面对话提交一次「继续」。可以同时为多个对话设置独立的倒计时。

A portable Windows timer that sends a one-time “继续” (continue) message to a selected Codex desktop chat.

## 下载与使用

在 [GitHub Releases](https://github.com/GodloveALei/codex-resume-timer/releases/latest) 下载并双击 `CodexResumeTimer.exe`。图标已经内嵌，运行只需这一个文件。电脑需要安装并打开 Codex 桌面端。

1. 在目标 Codex 对话中按 `Ctrl+Alt+L` 复制链接。
2. 粘贴到工具，选填备注，设置延迟，点击「添加定时」。
3. 重复添加其他对话。点某一行的「删除」会取消该项等待中的发送。

保留工具、Codex 桌面端和目标对话，可最小化窗口或锁屏。默认在有定时时阻止系统自动睡眠。手动睡眠期间到点的定时会在唤醒后处理。关闭工具、注销或重启会清除未发送的定时。

## 发送状态

- **已送达**：本机连接确认了原对话收到这次的「继续」。
- **已提交**：桌面端确认接收请求，运行结果可在原对话查看。
- **待核对**：请先在原对话检查是否收到「继续」，再决定是否重新定时。
- **未提交**：发出前无法连接该对话，或提交请求没有找到目标对话。

倒计时、绑定与提交请求通过本机运行，Codex 执行任务仍需要联网和可用额度。工具使用 Codex 桌面端的本机通信接口，兼容性随桌面端版本而变化。

## 从源码构建

在项目目录打开 Windows PowerShell，然后运行：

```powershell
.\build.ps1
```

脚本使用 Windows .NET Framework 的 C# 编译器。图标在构建过程中生成并嵌入 `CodexResumeTimer.exe`，临时文件会自动清理。

## 许可证

源码采用 [MIT](LICENSE) 许可证。此项目为独立工具。Codex 名称和云朵标志用于指明兼容的产品，相关商标权利归各自权利人。
