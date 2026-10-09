# REAPER OSC 适配器 — 无硬件原型

本模块位于 `software/FaderDeck.Reaper`，提供 OSC 1.0 单个 Float32 参数编码/解码，以及本机 UDP 发送/接收服务。支持 `/track/@/volume` 音量参数。**已提供独立的 REAPER OSC 图形窗口；尚未在真实 REAPER 上验收。** 在 FaderDeck Studio 主窗口点击“打开 REAPER OSC 控制台”，不影响现有 OBS 模式。

## REAPER OSC 参考设置

1. 在 REAPER 菜单 `Options → Preferences → Control/OSC/web` 添加 OSC（Open Sound Control）控制面；具体菜单名称依版本而异。
2. REAPER 接收端口：**8000**，向控制台发送状态反馈的目标 IP：**127.0.0.1**，端口：**9000**。
3. 在 REAPER 的 OSC pattern config 目录复制默认 `Default.ReaperOSC`，制作自定义配置副本。确认：
   - `DEVICE_TRACK_COUNT 32`，因为 FaderDeck 规划 8 Bank × 4 路，即 32 个轨道槽位。
   - `DEVICE_TRACK_BANK_FOLLOWS DEVICE`，避免轨道 Bank 随混音器自动改变。
   - `TRACK_VOLUME n/track/@/volume`，匹配 `/track/1/volume` 等浮点消息。
4. REAPER 的轨道数和显示 Bank 关系取决于控制面配置。请先在空白测试工程中验证音轨编号与反馈对应关系，**不要直接用于重要录音现场**。

参考官方文档：https://www.reaper.fm/sdk/osc/osc.php

## 适配器行为

- 默认本地 UDP 端口：REAPER `127.0.0.1:8000`；本程序侦听 `127.0.0.1:9000`。
- `WriteTrackVolumeAsync(track, normalized)` 发送 OSC float，轨道 1–32，normalized 范围 0–1。
- `VolumeFeedback` 事件接收 REAPER 回传的 `/track/{n}/volume`，只接受本机地址，范围 0–1。
- **UDP 发送成功不能作为参数设置成功的证据**；启动监听不代表 REAPER 已连接。只有收到对应轨道的反馈，才可提升状态为 Verified。
- 当前没有 REAPER 的同步读值调用，因此初始位置处于 Unknown，需要 REAPER 实际发出对应音轨反馈。
- 暂不支持接收 OSC Bundle、OSC 64-bit Double、其他轨道效果参数和控制面的自动发现。

## 参数映射

新通用 Profile 存储于 `%LOCALAPPDATA%\FaderDeck\profiles.json`（版本 1），定义 8 × 4 推子。
- `obs`：默认逐项选取 OBS 可用音频输入，Explicit 按精确输入名指定。
- `reaper`：默认 Bank 1 对应 1–4 号轨、Bank 2 对应 5–8 号轨，依此类推。可以设置 Explicit 指定 1–32 号轨，也可以 Unassigned。
- `davinci`：仅注册不可读/不可写的公开脚本 API 能力占位，**不会发送假的调色轮控制指令**。

首次生成通用 profiles.json 时可读取并转换旧的 `obs-mappings.json`，**保留旧文件、不自动覆盖**。目前 OBS GUI 仍使用旧文件，真正共享新 Profile 的图形界面整合在下一步完成。

## 使用 REAPER 图形控制窗口

1. 在 GitHub Actions 最新成功的工作流中下载 `FaderDeck-Studio-Windows-x64`，完整解压并启动程序。
2. 单击主界面的“打开 REAPER OSC 控制台（无硬件预览）”。
3. 设置 REAPER 接收端口（默认 8000）、Studio 回传端口（默认 9000），点击“启动 OSC”。这一步仅代表 UDP 端口绑定，并**不表示 REAPER 已在线**。
4. Bank 1 默认绑定 Track 1–4；Bank 2 对应 Track 5–8，最多 Bank 8 的 Track 29–32。
5. 每路可以选“自动音轨”“Track 1–32”“不绑定”。设置保存到 `profiles.json`，不会覆盖旧的 OBS 映射文件。
6. 拖动虚拟推子发送 OSC；在收到 REAPER 真正发来的轨道音量回传前，状态显示“已发送（未确认）”，不会误标 Verified。
7. 收到本机的 `/track/n/volume` float 反馈才会绿色显示 Verified。建议先从空白 REAPER 工程的 1–4 号轨验证。

当前 GUI 不假设 OSC 数据包有 ACK。即使点了“启动 OSC”，也不能保证 REAPER 已收到控制信号。请检查 REAPER 控制面的 OSC pattern 与端口映射。
