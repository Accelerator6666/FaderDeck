# FaderDeck 虚拟推子模拟器（无需硬件）

模拟器随 Windows CLI 一起提供。它直接响应与 M0 串口固件相同的 `PING`、`INFO`、`STATE`、`LAYER`、`MOVE`、`CALIBRATE` 命令，**不会连接串口，也不会驱动真实电机**。

## 运行

从 Actions 的 `FaderDeck-Windows-CLI` 下载并解压后，打开 PowerShell：

```powershell
.\FaderDeck.Cli.exe sim --demo
.\FaderDeck.Cli.exe sim
```

如已安装 .NET 10 SDK，在仓库根目录执行：

```powershell
dotnet run --project software/FaderDeck.Cli -- sim --demo
dotnet run --project software/FaderDeck.Cli -- sim
```

交互式示例（每次启动都从初始状态开始）：

```text
PING
INFO
STATE
MOVE 0 200 128
STATE
:touch on
MOVE 0 50 128
:manual 160
STATE
:touch off
LAYER 1
STATE
:fault on
MOVE 1 80 128
:fault off
:connect off
INFO
:connect on
:quit
```

模拟器专有命令以冒号开头：`:touch on/off`、`:manual 0..255`、`:busy on/off`、`:fault on/off`、`:connect on/off`、`:protocol 0..255`、`:caldone`、`:help`、`:quit`。这些命令**仅存在于模拟器**，不能发送给 ESP32-S3 固件。

## 能验证什么

- 主机协议解析、命令校验、推子状态和 8 个功能层的切换
- 被触摸、忙碌、校准、故障和断连状态下拒绝危险命令
- 对于非活动 Layer，更新记忆位置不会立即改变当前显示位置
- Windows CLI 的交互流程与核心参数安全门控规则

## 不能验证什么

- 真实电机速度、行程精度、堵转、电流及触摸电容
- USB Serial/JTAG 枚举、I²C 时序和无线/USB 断线行为
- ESP32-S3 的硬件供电与电路安全
- OBS/REAPER/DaVinci 的真正控制与状态反馈

模拟器故意把电机移动简化为**立即到达目标**；校准操作会停留在模式 4，直到输入 `:caldone`。它不是机械系统的物理仿真，不应用于推断真实硬件可靠性。

当实体硬件到位后，改用 [M0 硬件验收清单](HARDWARE_TEST.zh-CN.md)，记录实际测试结果。
