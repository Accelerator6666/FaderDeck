# M0 单推子硬件验收清单（请逐项记录）

> 当前是开发用实验固件，不是经过认证的安全控制器。不要连接高价值设备、运动障碍物或无人看管运行。**测试期间不要将手指放在滑杆行程内。**

## 1. 必备器件

- [ ] ESP32-S3 原生 USB Serial/JTAG 端口可用（具体开发板型号和引脚需确认）
- [ ] FaderBuddy PCB + 60mm 电动推子，I2C 地址 0x20，协议 v5
- [ ] 稳压 5V 电机电源、正确规格的导线和保护措施
- [ ] 3.3V 逻辑电源与 SDA/SCL 上拉电平匹配；所有设备共地
- [ ] SDA GPIO8 / SCL GPIO9 **仅适用于当前示例固件**，与实际板卡引脚核对
- [ ] 触摸检测需使用导电滑帽；塑料滑帽可能无法提供预期触摸状态

禁止带电接拔 I2C 模块；严禁将 FaderBuddy Vmot 5V 接到 ESP32-S3 3.3V 供电口。测试自校准前确保整个行程没有障碍。

## 2. 下载测试固件与命令行程序

在 [GitHub Actions](https://github.com/Accelerator6666/FaderDeck/actions) 中打开最新成功的 `FaderDeck CI`，下载以下工件：

- `FaderDeck-ESP32S3-M0`：bootloader、partition table、app binary 与 flash 参数
- `FaderDeck-Windows-CLI`：Windows x64 单文件测试程序

使用 ESP-IDF 5.5 可以直接从源码执行 `idf.py flash`。如果只用二进制工件烧录，**根据压缩包中的 flasher_args.json 核实偏移地址**，不应凭经验使用固定地址。确认自己使用的 USB 端口支持 ESP32-S3 下载模式。

## 3. Windows 只读检查

将以下命令中的 `COM5` 换成实际端口。先断开电机供电，验证控制端接线，确认无短路后按接线方案恢复供电。

```powershell
.\FaderDeck.Cli.exe ports
.\FaderDeck.Cli.exe COM5 ping
.\FaderDeck.Cli.exe COM5 info
.\FaderDeck.Cli.exe COM5 diagnose
.\FaderDeck.Cli.exe COM5 watch 20
```

预期 `ping` 返回 `PONG 1`；`info` 返回协议 v5、固件版本和序列号；`state` 可读取位置、模式和触摸。若 `info` 报错，请先检查 SDA、SCL、GND、逻辑电压及 I2C 地址，不要运行 MOVE。

### 结果记录

| 项目 | 实际结果 |
| --- | --- |
| ESP32-S3 型号 | |
| FaderBuddy 固件版本 | |
| COM 端口 | |
| INFO / STATE 输出 | |
| 人手缓慢移动时 position 是否变化 | |
| 使用导电滑帽时 touch 是否变为 1 | |
| I2C 丢包/ERR 次数 | |

## 4. 电机动作测试（仅在上面通过后）

先运行 `state`，确认 `Mode=2` 且 `Touch=False`、无机械障碍；确认 5V 电源限流措施正常。**首次使用需按 FaderBuddy 原项目说明在安全环境下执行 self-calibration，它会全行程运动。**

```powershell
.\FaderDeck.Cli.exe COM5 calibrate
.\FaderDeck.Cli.exe COM5 state
.\FaderDeck.Cli.exe COM5 move 0 128 0
.\FaderDeck.Cli.exe COM5 state
```

运动/校准后等待结束并反复读取状态；位置 128 只是 M0 测试点，不代表实际物理零点。**在固件仍处于 mode 0 或 mode 4 时不要继续发送 MOVE**。如果异常发热、噪声、卡阻或电源保护动作，立即断电并排查机械结构。

## 5. 验收条件（实测后填写）

- [ ] 断电/重启后无需自动电机移动，能够重新正常读取
- [ ] INFO 协议、版本、序列号与 FaderBuddy 固件一致
- [ ] 位置读数随手动移动变化，方向和范围符合预期
- [ ] 使用导电滑帽时 TOUCH 工作，触摸期间 MOVE 被拒绝
- [ ] 校准成功，电动定位后状态值与目标值一致（允许机械死区误差）
- [ ] 串口断开、拔插 USB 后设备不发生意外电机移动

**没有实际硬件实验数据前，不应勾选这些验收项。**
