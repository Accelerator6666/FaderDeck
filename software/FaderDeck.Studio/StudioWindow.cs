using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FaderDeck.Core;
using FaderDeck.Obs;

namespace FaderDeck.Studio;

/// <summary>
/// No device needed: 4 fader UI in Simulation profile. In OBS profile each
/// slot maps to an audio-capable OBS input in the selected bank.
/// </summary>
public sealed class StudioWindow : Window
{
    private readonly VirtualFader[] virtualFaders = Enumerable.Range(0, 4).Select(_ => new VirtualFader()).ToArray();
    private readonly ObsWebSocketClient obs = new();
    private readonly Slider[] sliders = new Slider[4];
    private readonly TextBlock[] channelNames = new TextBlock[4];
    private readonly TextBlock[] channelValues = new TextBlock[4];
    private readonly TextBlock[] channelStatuses = new TextBlock[4];
    private readonly string?[] mappedInputs = new string?[4];
    private readonly CancellationTokenSource?[] pendingSends = new CancellationTokenSource?[4];
    private readonly DateTime[] suppressFeedbackUntil = new DateTime[4];

    private readonly ComboBox profileSelect;
    private readonly ComboBox bankSelect;
    private readonly TextBox endpointInput;
    private readonly TextBox passwordInput;
    private readonly Button connectButton;
    private readonly Button disconnectButton;
    private readonly Button refreshButton;
    private readonly TextBlock headlineStatus;
    private readonly TextBlock activityLog;
    private readonly TextBlock helpText;

    private bool internalValueUpdate;
    private int revision;
    private bool inObsMode => profileSelect.SelectedIndex == 1;

    public StudioWindow()
    {
        Title = "FaderDeck Studio · Preview";
        Width = 1050;
        Height = 810;
        MinWidth = 780;
        MinHeight = 620;
        Background = ColorBrush("#101827");

        var title = new TextBlock
        {
            Text = "FaderDeck Studio",
            FontSize = 28,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White
        };
        var tagline = new TextBlock
        {
            Text = "四路虚拟推子  ·  软件模式  ·  OBS WebSocket v5",
            FontSize = 13,
            Foreground = ColorBrush("#9BAEC7")
        };
        var titleGroup = new StackPanel { Spacing = 4 };
        titleGroup.Children.Add(title);
        titleGroup.Children.Add(tagline);

        profileSelect = new ComboBox
        {
            Width = 180,
            ItemsSource = new[] { "Simulation / 模拟", "OBS Studio" },
            SelectedIndex = 0
        };
        bankSelect = new ComboBox
        {
            Width = 150,
            ItemsSource = Enumerable.Range(1, 8).Select(i => $"Bank {i}").ToArray(),
            SelectedIndex = 0
        };
        var selectors = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        selectors.Children.Add(new TextBlock { Text = "模式", VerticalAlignment = VerticalAlignment.Center });
        selectors.Children.Add(profileSelect);
        selectors.Children.Add(new TextBlock { Text = "功能页", VerticalAlignment = VerticalAlignment.Center });
        selectors.Children.Add(bankSelect);
        var heading = new StackPanel { Spacing = 12 };
        heading.Children.Add(titleGroup);
        heading.Children.Add(selectors);

        endpointInput = new TextBox
        {
            Text = "ws://127.0.0.1:4455",
            Width = 235,
            Watermark = "OBS WebSocket URL"
        };
        passwordInput = new TextBox
        {
            Width = 175,
            PasswordChar = '●',
            Watermark = "OBS Password"
        };
        connectButton = new Button { Content = "连接 OBS", IsEnabled = false };
        disconnectButton = new Button { Content = "断开", IsEnabled = false };
        refreshButton = new Button { Content = "重新读取", IsEnabled = false };
        var connectionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 9,
            VerticalAlignment = VerticalAlignment.Center
        };
        connectionRow.Children.Add(new TextBlock { Text = "服务器", VerticalAlignment = VerticalAlignment.Center });
        connectionRow.Children.Add(endpointInput);
        connectionRow.Children.Add(passwordInput);
        connectionRow.Children.Add(connectButton);
        connectionRow.Children.Add(disconnectButton);
        connectionRow.Children.Add(refreshButton);

        headlineStatus = new TextBlock
        {
            Text = "SIMULATION · 本地虚拟设备 · 不需要硬件",
            Foreground = ColorBrush("#8BDDCA"),
            FontSize = 13
        };
        helpText = new TextBlock
        {
            Text = "拖动滑杆试用模拟推子。切换 Bank 会恢复每层的位置。不会发送任何外部控制命令。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = ColorBrush("#9BAEC7")
        };

        var faderGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
            ColumnSpacing = 14
        };
        for (int i = 0; i < 4; i++)
        {
            int slot = i;
            var panel = new StackPanel
            {
                Spacing = 12,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            channelNames[i] = new TextBlock
            {
                Text = $"FADER {i + 1}",
                FontSize = 16,
                FontWeight = FontWeight.SemiBold,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            channelValues[i] = new TextBlock
            {
                Text = "50%",
                FontSize = 24,
                FontWeight = FontWeight.SemiBold,
                TextAlignment = TextAlignment.Center,
                Foreground = ColorBrush("#66D3C3")
            };
            sliders[i] = new Slider
            {
                Minimum = 0,
                Maximum = 100,
                Value = 50,
                Orientation = Orientation.Vertical,
                Height = 285,
                Width = 66,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            channelStatuses[i] = new TextBlock
            {
                Text = "Virtual / Ready",
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Foreground = ColorBrush("#9BAEC7")
            };

            panel.Children.Add(channelNames[i]);
            panel.Children.Add(channelValues[i]);
            panel.Children.Add(sliders[i]);
            panel.Children.Add(channelStatuses[i]);
            var card = new Border
            {
                Background = ColorBrush("#1B293E"),
                BorderBrush = ColorBrush("#30465F"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(18),
                Child = panel
            };
            Grid.SetColumn(card, i);
            faderGrid.Children.Add(card);
            sliders[i].ValueChanged += (_, _) => HandleSliderInput(slot);
        }

        activityLog = new TextBlock
        {
            Text = "日志：等待操作",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = ColorBrush("#A9BCD1")
        };

        var root = new StackPanel
        {
            Spacing = 20,
            Margin = new Thickness(25)
        };
        root.Children.Add(heading);
        root.Children.Add(connectionRow);
        root.Children.Add(headlineStatus);
        root.Children.Add(faderGrid);
        root.Children.Add(helpText);
        root.Children.Add(new Border
        {
            Background = ColorBrush("#182338"),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14),
            Child = activityLog
        });
        Content = new ScrollViewer { Content = root };

        profileSelect.SelectionChanged += (_, _) => ApplyProfile();
        bankSelect.SelectionChanged += async (_, _) => await ChangeBankAsync();
        connectButton.Click += async (_, _) => await ConnectObsAsync();
        disconnectButton.Click += async (_, _) => await DisconnectObsAsync();
        refreshButton.Click += async (_, _) => await RefreshObsBankAsync(revision);
        obs.VolumeChanged += HandleObsVolume;
        obs.Disconnected += message => Dispatcher.UIThread.Post(() =>
        {
            CancelPendingSends();
            if (!inObsMode) return;
            headlineStatus.Text = "OBS 已断开 · " + message;
            DisconnectSlots("OBS 离线");
            connectButton.IsEnabled = true;
            disconnectButton.IsEnabled = false;
            refreshButton.IsEnabled = false;
            Log("OBS 连接中断：" + message);
        });
        Closed += async (_, _) => { CancelPendingSends(); await obs.DisposeAsync(); };

        ApplyProfile();
    }

    private static IBrush ColorBrush(string hex) => new SolidColorBrush(Color.Parse(hex));

    private void Log(string message)
    {
        string now = DateTime.Now.ToString("HH:mm:ss");
        string previous = activityLog.Text ?? "";
        string current = $"[{now}] {message}";
        activityLog.Text = (current + Environment.NewLine + previous)[..Math.Min(current.Length + Environment.NewLine.Length + previous.Length, 1500)];
    }

    private void SetVisual(int slot, double percent, string state, bool authoritative)
    {
        internalValueUpdate = true;
        try
        {
            sliders[slot].Value = Math.Clamp(percent, 0, 100);
            channelValues[slot].Text = inObsMode
                ? $"{Math.Round(percent):0}%  ·  {(percent <= 0 ? "OFF" : $"{ObsProtocol.SliderToDb(percent):0.0} dB")}"
                : $"{Math.Round(percent):0}%";
            channelValues[slot].Foreground = authoritative ? ColorBrush("#66D3C3") : ColorBrush("#E8B97F");
            channelStatuses[slot].Text = state;
        }
        finally
        {
            internalValueUpdate = false;
        }
    }

    private void CancelPendingSends()
    {
        for (int i = 0; i < pendingSends.Length; i++)
        {
            pendingSends[i]?.Cancel();
            pendingSends[i]?.Dispose();
            pendingSends[i] = null;
        }
    }

    private void DisconnectSlots(string reason)
    {
        for (int i = 0; i < 4; i++)
        {
            mappedInputs[i] = null;
            channelNames[i].Text = $"FADER {i + 1}";
            sliders[i].IsEnabled = false;
            SetVisual(i, 0, reason + " / Unknown", false);
        }
    }

    private void ApplyProfile()
    {
        revision++;
        CancelPendingSends();
        if (inObsMode)
        {
            connectButton.IsEnabled = !obs.Connected;
            disconnectButton.IsEnabled = obs.Connected;
            refreshButton.IsEnabled = obs.Connected;
            endpointInput.IsEnabled = !obs.Connected;
            passwordInput.IsEnabled = !obs.Connected;
            headlineStatus.Text = obs.Connected ? "OBS 已连接" : "OBS 未连接 · 请启动 OBS 并配置 WebSocket";
            helpText.Text = "OBS 会读取当前 Bank 的四个音频输入。反馈以 OBS 实际值为准；尚未读取的参数不可视为已同步。";
            DisconnectSlots("未同步");
            if (obs.Connected) _ = RefreshObsBankAsync(revision);
        }
        else
        {
            connectButton.IsEnabled = false;
            disconnectButton.IsEnabled = false;
            refreshButton.IsEnabled = false;
            endpointInput.IsEnabled = false;
            passwordInput.IsEnabled = false;
            headlineStatus.Text = "SIMULATION · 四路虚拟设备 · 无需硬件";
            helpText.Text = "拖动推子改变虚拟值；切换 Bank 恢复独立 Layer 的位置。本模式不控制 OBS，也不控制实体电机。";
            RestoreSimulationBank();
        }
    }

    private void RestoreSimulationBank()
    {
        int layer = Math.Max(0, bankSelect.SelectedIndex);
        for (int i = 0; i < 4; i++)
        {
            VirtualFader sim = virtualFaders[i];
            sim.SetTouch(false);
            string reply = sim.Execute($"LAYER {layer}");
            channelNames[i].Text = $"FADER {i + 1}";
            sliders[i].IsEnabled = reply == "OK LAYER";
            SetVisual(i, sim.State.Position * 100.0 / 255, $"Layer {layer + 1} · {reply}", true);
        }
        Log($"SIM Bank {layer + 1} 已切换");
    }

    private void HandleSliderInput(int slot)
    {
        if (internalValueUpdate) return;
        double percent = sliders[slot].Value;
        if (!inObsMode)
        {
            VirtualFader sim = virtualFaders[slot];
            sim.SetTouch(true);
            sim.ManualMove((byte)Math.Clamp(Math.Round(percent * 255 / 100), 0, 255));
            sim.SetTouch(false);
            SetVisual(slot, sim.State.Position * 100.0 / 255, "Virtual · 已更新", true);
            return;
        }

        string? input = mappedInputs[slot];
        if (input is null || !obs.Connected) return;
        channelValues[slot].Text = $"{Math.Round(percent):0}% · {(percent <= 0 ? "OFF" : $"{ObsProtocol.SliderToDb(percent):0.0} dB")}";
        channelStatuses[slot].Text = "发送中 · 等待 OBS 确认";
        suppressFeedbackUntil[slot] = DateTime.UtcNow.AddMilliseconds(250);
        pendingSends[slot]?.Cancel();
        pendingSends[slot]?.Dispose();
        var debounce = new CancellationTokenSource();
        pendingSends[slot] = debounce;
        _ = SendSliderAsync(slot, input, percent, revision, debounce.Token);
    }

    private async Task SendSliderAsync(int slot, string input, double percent, int version, CancellationToken cancel)
    {
        try
        {
            await Task.Delay(85, cancel);
            if (version != revision || !inObsMode || mappedInputs[slot] != input) return;
            await obs.SetInputVolumeAsync(input, percent, cancel);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (version == revision && mappedInputs[slot] == input)
                    channelStatuses[slot].Text = "OBS 接受设置 · 等待反馈";
            });
        }
        catch (OperationCanceledException) { /* next move wins */ }
        catch (Exception e)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (version != revision || mappedInputs[slot] != input) return;
                channelStatuses[slot].Text = "错误 · 未确认";
                Log($"OBS {input} 设置失败：{e.Message}");
            });
        }
    }

    private void HandleObsVolume(string input, double db)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!inObsMode || !obs.Connected) return;
            for (int i = 0; i < 4; i++)
            {
                if (mappedInputs[i] != input) continue;
                if (DateTime.UtcNow < suppressFeedbackUntil[i]) continue;
                SetVisual(i, ObsProtocol.DbToSlider(db), "Verified · OBS 反馈", true);
            }
        });
    }

    private async Task ChangeBankAsync()
    {
        revision++;
        CancelPendingSends();
        if (!inObsMode) RestoreSimulationBank();
        else if (obs.Connected) await RefreshObsBankAsync(revision);
        else DisconnectSlots("未连接");
    }

    private async Task ConnectObsAsync()
    {
        if (!inObsMode) return;
        connectButton.IsEnabled = false;
        headlineStatus.Text = "正在连接 OBS...";
        try
        {
            if (!Uri.TryCreate(endpointInput.Text?.Trim(), UriKind.Absolute, out var url))
                throw new ArgumentException("OBS WebSocket URL 无效。");
            await obs.ConnectAsync(url, passwordInput.Text ?? "");
            passwordInput.Text = ""; // password is never saved on disk
            disconnectButton.IsEnabled = true;
            refreshButton.IsEnabled = true;
            endpointInput.IsEnabled = false;
            passwordInput.IsEnabled = false;
            headlineStatus.Text = "OBS 已连接 · 读取音频输入...";
            Log("OBS WebSocket v5 连接成功");
            await RefreshObsBankAsync(revision);
        }
        catch (Exception e)
        {
            headlineStatus.Text = "连接失败 · " + e.Message;
            Log("OBS 连接失败：" + e.Message);
            connectButton.IsEnabled = true;
        }
    }

    private async Task DisconnectObsAsync()
    {
        revision++;
        CancelPendingSends();
        await obs.DisconnectAsync();
        ApplyProfile();
        Log("主动断开 OBS");
    }

    private async Task RefreshObsBankAsync(int version)
    {
        if (!obs.Connected || !inObsMode) return;
        refreshButton.IsEnabled = false;
        CancelPendingSends();
        DisconnectSlots("读取中");
        try
        {
            IReadOnlyList<string> all = await obs.GetAudioInputsAsync();
            if (version != revision || !inObsMode) return;
            int start = Math.Max(0, bankSelect.SelectedIndex) * 4;
            for (int slot = 0; slot < 4; slot++)
            {
                int index = start + slot;
                if (index >= all.Count) continue;
                string name = all[index];
                double db = await obs.GetInputVolumeDbAsync(name);
                if (version != revision || !inObsMode) return;
                mappedInputs[slot] = name;
                channelNames[slot].Text = name;
                sliders[slot].IsEnabled = true;
                SetVisual(slot, ObsProtocol.DbToSlider(db), "Verified · OBS 读取", true);
            }
            headlineStatus.Text = $"OBS 已连接 · {all.Count} 个可调音量输入 · Bank {bankSelect.SelectedIndex + 1}";
            Log($"OBS 已读取 {all.Count} 个音频输入");
        }
        catch (Exception e)
        {
            if (version != revision) return;
            headlineStatus.Text = "OBS 读取失败 · " + e.Message;
            Log("OBS 读取失败：" + e.Message);
        }
        finally
        {
            if (version == revision) refreshButton.IsEnabled = obs.Connected;
        }
    }
}
