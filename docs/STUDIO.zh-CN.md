# FaderDeck Studio — Windows GUI 预览

暂时不需要购买 ESP32-S3 或 FaderBuddy。Avalonia + .NET 10 图形程序可以验证四路软件推子、独立 Bank 和初版 OBS WebSocket 音量控制。

## 下载和启动

1. 在 [GitHub Actions](https://github.com/Accelerator6666/FaderDeck/actions) 中打开最新的**全部通过**的 CI。
2. 下载 `FaderDeck-Studio-Windows-x64`，将压缩包**全部解压**到同一个目录。
3. 执行 `FaderDeck.Studio.exe`。这是未签名的开发预览版，不是正式安装程序。

本地 .NET 10 SDK 也可运行：
```powershell
dotnet run --project software/FaderDeck.Studio -c Release
```

## 模拟模式

默认选择 `Simulation / 模拟`。拖动四个推子可改变各自位置；Bank 1–8 保存每路独立数值。所有操作只存在于 Windows 进程内部，不控制电脑音量、OBS 或实体电机。

## OBS 模式

1. 启动 OBS Studio，打开 **工具 → WebSocket 服务器设置**，启用服务并记录密码；OBS 5.x WebSocket 默认端口为 4455。
2. 切换到 `OBS Studio` 模式。
3. 输入 `ws://127.0.0.1:4455` 和正确的密码，单击“连接 OBS”。
4. 程序探测 OBS 输入列表中支持音量控制的输入，每个 Bank 显示四个；拖动滑杆通过 `SetInputVolume` 更新音量，并通过 `InputVolumeChanged` 刷新。

当前滑杆从 0% 到 100% 对应软件定义的 -60 至 0 dB，0% 使用零幅值（真正静音），这**不是 OBS 原生音量推子的精确复制**。首次读取成功后才显示 Verified；发出修改指令不等于获得反馈确认。

## 安全和未完成内容

- OBS 密码仅保存在内存中，不写入配置文件；禁止非本机的明文 ws:// 连接，远程使用 wss:// 并自行保证 TLS 和认证可靠。
- Studio 暂时不会连接任何 FaderBuddy 实体硬件；GUI 中的推子没有电机反馈。
- 只支持 OBS 音频输入音量，不含 OBS 整套混音功能，也不包含 DaVinci 调色。
- 当前通道按 OBS 输入列表顺序分配。短暂的本地回声抑制只是实验性质，长期双向同步需要后续验证。
- GitHub Actions 只能验证构建与纯逻辑测试，**不能证明 OBS 实机通信成功**。

如果连接失败，先确认 OBS 版本、WebSocket 服务是否启用、端口和密码。不要将包含真实密码的日志上传到公开 Issue。
