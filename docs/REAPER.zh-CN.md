# REAPER OSC 适配器 — 无硬件原型

本模块位于 `software/FaderDeck.Reaper`，提供 OSC 1.0 单个 Float32 参数编码/解码，以及本机 UDP 发送/接收服务。支持 `/track/@/volume` 音量参数。**首次提交仅提供通信适配器和自动化测试，尚未在 FaderDeck Studio GUI 中接线，也未在真正 REAPER 内验收。**

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
