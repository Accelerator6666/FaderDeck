# FaderDeck / 模块化电动控制台

[English](README.md) · [架构](docs/ARCHITECTURE.md) · [通信协议](docs/PROTOCOL.md) · [开发路线](docs/ROADMAP.md) · [硬件验收清单](docs/HARDWARE_TEST.zh-CN.md) · [虚拟推子模拟器](docs/SIMULATOR.zh-CN.md)

FaderDeck 是一套可扩展的开源电动推子控制系统，规划支持 OBS Studio、REAPER、Ableton Live 和 DaVinci Resolve 等软件。

## 当前开发阶段：M0 验证原型

**本仓库目前不是可直接使用的完整调色台。** 第一个里程碑实现：

- ESP32-S3 ↔ 单个 FaderBuddy（I²C，默认地址 0x20）
- ESP32-S3 原生 USB Serial/JTAG CDC 命令交互（尚未实现 USB MIDI）
- Windows .NET 10 命令行测试工具
- 上位机参数状态安全门控（仅 Verified 状态允许自动定位）
- GitHub Actions 自动编译与核心逻辑测试

**已新增软件预览**：Avalonia 图形界面（四路推子、八个 Bank）和初版 OBS WebSocket v5 音量适配器，仍需真实 OBS 环境联调。**尚未实现**：REAPER/DaVinci 适配器、实体四路硬件同步、USB MIDI、真实电机自动跟随。

## 没有硬件也能测试：VirtualFader

现在可使用虚拟推子模拟器，不依赖 ESP32-S3，也不需要 COM 端口或真实电动推子：

```powershell
dotnet run --project software/FaderDeck.Cli -- sim --demo
dotnet run --project software/FaderDeck.Cli -- sim
```

也可以从 GitHub Actions 下载 Windows CLI 后执行 `FaderDeck.Cli.exe sim --demo`。模拟器支持推子位置、8 层功能、触摸、故障、断开与恢复测试，但**不能代表真实硬件精度和安全性**。详见[模拟器说明](docs/SIMULATOR.zh-CN.md)。

## FaderDeck Studio 图形预览（无需硬件）

从成功的 [GitHub Actions](https://github.com/Accelerator6666/FaderDeck/actions) 中下载 **FaderDeck-Studio-Windows-x64**，将 ZIP 所有文件解压到同一个目录并运行 `FaderDeck.Studio.exe`。

- `Simulation / 模拟`：四路虚拟推子、八个独立 Bank，无需外接设备。
- `OBS Studio`：在 OBS 中启用 WebSocket 服务，输入默认地址 `ws://127.0.0.1:4455` 和密码，连接后按 Bank 显示音频输入，可以调节音量并获取事件反馈。
- 密码不会写入本地配置；程序不允许用未经加密的远程 `ws://` 地址连接。
- GUI 尚不能连接物理 FaderBuddy，OBS 联调也还未完成。详见 [Studio 使用说明](docs/STUDIO.zh-CN.md)。

## M0 接线

| ESP32-S3 / 电源 | FaderBuddy |
| --- | --- |
| GPIO8（示例 SDA） | SDA |
| GPIO9（示例 SCL） | SCL |
| 3.3V 逻辑电源 | Vio |
| 外部稳压 5V | Vmot |
| 共地 | GND |

此映射仅作开发示例，必须对照实际 ESP32-S3 板卡引脚和 FaderBuddy 版本核对。禁止将 Vmot 5V 直接接到 ESP32 的 3.3V 引脚；测试时先检查电机电源、地址、线路及负载。原型不支持带电热插拔。

## 固件编译

需 ESP-IDF 5.5：

```bash
cd firmware/esp32-s3
idf.py set-target esp32s3
idf.py build
idf.py -p COM5 flash
```

M0 固件使用 ESP32-S3 内置 USB Serial/JTAG CDC，不是复合 USB 设备。串口连接应选对应的 USB Serial/JTAG 端口。

## Windows 测试

安装 .NET 10 SDK：

```powershell
dotnet run --project software/FaderDeck.Cli -- ports
dotnet run --project software/FaderDeck.Cli -- COM5 ping
dotnet run --project software/FaderDeck.Cli -- COM5 info
dotnet run --project software/FaderDeck.Cli -- COM5 diagnose
dotnet run --project software/FaderDeck.Cli -- COM5 watch 20
dotnet run --project software/FaderDeck.Cli -- COM5 state
dotnet run --project software/FaderDeck.Cli -- COM5 move 0 128 128
dotnet run --project software/FaderDeck.Cli -- COM5 layer 0
```

CI 成功后，在 [Actions](https://github.com/Accelerator6666/FaderDeck/actions) 中可以下载 Windows x64 命令行程序和 ESP32-S3 固件（二进制包中的 `flasher_args.json` 标明刷写偏移）。[硬件验收清单](docs/HARDWARE_TEST.zh-CN.md) 包含逐步测试流程。

`move` 会启动电机；`calibrate` 将触发全行程自校准。必须保证滑杆周围安全，先运行 `ping`、`state` 再进行电机测试。

## 设计原则

1. 保留 FaderBuddy 原有 ATtiny1616 电机闭环固件，不从零重做电机算法。
2. 主控不绑定具体软件，上位机 Adapter 负责映射。
3. **无法验证软件参数真实值时不得自动定位电机**。
4. 先完成可重复的单推子双向验证，再扩展四推子、USB MIDI 和桌面 UI。

许可证：Apache-2.0。FaderBuddy 原项目：[scottbez1/FaderBuddy](https://github.com/scottbez1/FaderBuddy)，本用户 Fork：[Accelerator6666/FaderBuddy](https://github.com/Accelerator6666/FaderBuddy)。
