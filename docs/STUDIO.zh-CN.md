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
4. 程序探测 OBS 输入列表中支持音量控制的输入。映射编辑器会显示每个 Bank 的四路配置；默认“自动（按 OBS 列表顺序）”保持过去行为。
5. 在某个 Fader 的下拉菜单中选择具体 OBS 音频输入，即可固定绑定；选择“不绑定”则禁用该推子。四路推子可以绑定相同的 OBS 输入。
6. 更改立即写入本地 JSON 配置，切换 Bank、关闭并重启 Studio 后保留。点击“恢复本 Bank 自动映射”只重置当前 Bank。
7. 绑定名称不存在时显示“指定输入缺失”，对应推子禁用；**不会**偷偷换绑到其他输入。拖动正常绑定的推子通过 `SetInputVolume` 修改参数，并以 OBS 回读结果更新显示。

当前滑杆从 0% 到 100% 对应软件定义的 -60 至 0 dB，0% 使用零幅值（真正静音），这**不是 OBS 原生音量推子的精确复制**。首次读取成功后才显示 Verified；发出修改指令不等于获得反馈确认。

## 安全和未完成内容

- OBS 密码仅保存在内存中，不写入配置文件；禁止非本机的明文 ws:// 连接，远程使用 wss:// 并自行保证 TLS 和认证可靠。
- Studio 暂时不会连接任何 FaderBuddy 实体硬件；GUI 中的推子没有电机反馈。
- 只支持 OBS 音频输入音量，不含 OBS 整套混音功能，也不包含 DaVinci 调色。
- 映射选择使用 OBS 当前音频输入名；更改 OBS 输入名称后需要重新选择映射。短暂的本地回声抑制只是实验性质，长期双向同步需要后续验证。
- GitHub Actions 只能验证构建与纯逻辑测试，**不能证明 OBS 实机通信成功**。

如果连接失败，先确认 OBS 版本、WebSocket 服务是否启用、端口和密码。不要将包含真实密码的日志上传到公开 Issue。

## 映射配置文件

- 位置：`%LOCALAPPDATA%\FaderDeck\obs-mappings.json`
- 存储内容：8 个 Bank 的自定义输入名称、不绑定状态及版本号；不包含 OBS 密码。
- 默认不需要建立文件，所有推子使用自动映射。第一次修改后自动保存；保存采用同目录临时文件后原子替换。
- 如果文件格式损坏或版本不支持，Studio 会阻止继续保存并显示错误，避免覆盖已有数据。请先备份该 JSON 后再手动修正或移走，并重启程序。
- **自动分配**按 OBS 输入列表顺序运行；**指定输入**匹配精确名称，不随列表排序改变；**不绑定**禁止发送操作。

注意：这是 OBS 音频输入映射编辑器，并不是已经完成了 DaVinci、REAPER 的通用参数插件框架。
