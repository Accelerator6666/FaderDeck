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
    private readonly ComboBox[] mappingSelectors = new ComboBox[4];
    private readonly ObsMappingProfile mappingProfile;
    private readonly bool canSaveMappings;
    private IReadOnlyList<string> availableInputs = Array.Empty<string>();
    private bool loadingMappingEditor;
    private readonly StackPanel editorPanel;
    private readonly TextBlock mappingSaveStatus;
    private sealed record MappingOption(string Title, FaderBinding Binding)
    {
        public override string ToString() => Title;
    }

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

        string? loadError = null;
        try { mappingProfile = ObsMappingProfile.Load(ObsMappingProfile.DefaultFilePath); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            mappingProfile = new ObsMappingProfile();
            loadError = e.Message;
        }
        canSaveMappings = loadError is null;

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

        editorPanel = new StackPanel { Spacing = 10, IsVisible = false };
        var editorTitle = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12
        };
        editorTitle.Children.Add(new TextBlock
        {
            Text = "OBS 参数映射 · 本 Bank",
            FontSize = 17,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        var resetBankButton = new Button
        {
            Content = "恢复本 Bank 自动映射",
            IsEnabled = canSaveMappings
        };
        resetBankButton.Click += async (_, _) => await ResetMappingBankAsync();
        editorTitle.Children.Add(resetBankButton);
        editorPanel.Children.Add(editorTitle);
        var editorGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
            ColumnSpacing = 10
        };
        for (int i = 0; i < 4; i++)
        {
            int slot = i;
            var group = new StackPanel { Spacing = 4 };
            group.Children.Add(new TextBlock { Text = $"Fader {i + 1}", FontSize = 12 });
            mappingSelectors[i] = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinWidth = 140,
                IsEnabled = canSaveMappings
            };
            mappingSelectors[i].SelectionChanged += async (_, _) => await MappingChangedAsync(slot);
            group.Children.Add(mappingSelectors[i]);
            Grid.SetColumn(group, i);
            editorGrid.Children.Add(group);
        }
        editorPanel.Children.Add(editorGrid);
        mappingSaveStatus = new TextBlock
        {
            Text = canSaveMappings
                ? $"映射配置自动保存：{ObsMappingProfile.DefaultFilePath}"
                : $"配置文件无法读取（停止自动保存，避免覆盖）：{loadError}",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = canSaveMappings ? ColorBrush("#9BAEC7") : ColorBrush("#E8B97F")
        };
        editorPanel.Children.Add(mappingSaveStatus);

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
        var reaperButton = new Button
        {
            Content = "打开 REAPER OSC 控制台（无硬件预览）",
            HorizontalAlignment = HorizontalAlignment.Left
        };
        reaperButton.Click += (_, _) => new ReaperOscWindow().Show(this);
        root.Children.Add(reaperButton);
        root.Children.Add(connectionRow);
        root.Children.Add(headlineStatus);
        root.Children.Add(editorPanel);
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
            availableInputs = Array.Empty<string>();
            UpdateMappingEditor();
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
        editorPanel.IsVisible = inObsMode;
        UpdateMappingEditor();
        CancelPendingSends();
        if (inObsMode)
        {
            connectButton.IsEnabled = !obs.Connected;
            disconnectButton.IsEnabled = obs.Connected;
            refreshButton.IsEnabled = obs.Connected;
            endpointInput.IsEnabled = !obs.Connected;
            passwordInput.IsEnabled = !obs.Connected;
            headlineStatus.Text = obs.Connected ? "OBS 已连接" : "OBS 未连接 · 请启动 OBS 并配置 WebSocket";
            helpText.Text = "使用上方下拉框，为四路推子选择自动分配、指定音频输入或不绑定。只有成功读取 OBS 实际音量后，推子才能操作。";
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
            // Re-read authoritative OBS state: InputVolumeChanged may have been
            // suppressed while the local fader was being dragged.
            double confirmedDb = await obs.GetInputVolumeDbAsync(input, cancel);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (version == revision && mappedInputs[slot] == input && !cancel.IsCancellationRequested)
                {
                    suppressFeedbackUntil[slot] = DateTime.MinValue;
                    SetVisual(slot, ObsProtocol.DbToSlider(confirmedDb), "Verified · OBS 回读", true);
                }
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
        UpdateMappingEditor();
        if (!inObsMode) RestoreSimulationBank();
        else if (obs.Connected) await ApplyMappingsAsync(revision);
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

    private void UpdateMappingEditor()
    {
        loadingMappingEditor = true;
        try
        {
            int bank = Math.Max(0, bankSelect.SelectedIndex);
            for (int slot = 0; slot < 4; slot++)
            {
                FaderBinding selected = mappingProfile.Get(bank, slot);
                var options = new List<MappingOption>
                {
                    new("自动（按 OBS 列表顺序）", FaderBinding.Auto),
                    new("不绑定（禁用此推子）", FaderBinding.Unassigned)
                };
                foreach (string input in availableInputs.Distinct(StringComparer.Ordinal))
                    options.Add(new MappingOption(input, FaderBinding.ForInput(input)));
                if (selected.Mode == MappingMode.Explicit
                    && !availableInputs.Contains(selected.InputName, StringComparer.Ordinal))
                    options.Add(new MappingOption($"未发现输入 · {selected.InputName}", selected));
                mappingSelectors[slot].ItemsSource = options;
                mappingSelectors[slot].SelectedItem =
                    options.First(x => x.Binding == selected);
                mappingSelectors[slot].IsEnabled = inObsMode && canSaveMappings;
            }
        }
        finally
        {
            loadingMappingEditor = false;
        }
    }

    private bool SaveMappings()
    {
        try
        {
            mappingProfile.Save(ObsMappingProfile.DefaultFilePath);
            mappingSaveStatus.Text = $"已保存 · {ObsMappingProfile.DefaultFilePath}";
            mappingSaveStatus.Foreground = ColorBrush("#8BDDCA");
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            mappingSaveStatus.Text = "保存失败（当前更改仅在内存中）：" + e.Message;
            mappingSaveStatus.Foreground = ColorBrush("#E8B97F");
            Log("映射文件保存失败：" + e.Message);
            return false;
        }
    }

    private async Task MappingChangedAsync(int slot)
    {
        if (loadingMappingEditor || !inObsMode || !canSaveMappings) return;
        if (mappingSelectors[slot].SelectedItem is not MappingOption option) return;
        int bank = Math.Max(0, bankSelect.SelectedIndex);
        mappingProfile.Set(bank, slot, option.Binding);
        SaveMappings();
        revision++;
        CancelPendingSends();
        if (obs.Connected) await ApplyMappingsAsync(revision);
        else DisconnectSlots("请先连接 OBS");
    }

    private async Task ResetMappingBankAsync()
    {
        if (!canSaveMappings || !inObsMode) return;
        int bank = Math.Max(0, bankSelect.SelectedIndex);
        mappingProfile.ResetBank(bank);
        SaveMappings();
        revision++;
        CancelPendingSends();
        UpdateMappingEditor();
        if (obs.Connected) await ApplyMappingsAsync(revision);
        else DisconnectSlots("请先连接 OBS");
        Log($"Bank {bank + 1} 已恢复自动映射");
    }

    private async Task RefreshObsBankAsync(int version)
    {
        if (!obs.Connected || !inObsMode) return;
        refreshButton.IsEnabled = false;
        CancelPendingSends();
        DisconnectSlots("刷新输入列表");
        try
        {
            IReadOnlyList<string> inputs = await obs.GetAudioInputsAsync();
            if (version != revision || !inObsMode || !obs.Connected) return;
            availableInputs = inputs;
            UpdateMappingEditor();
            await ApplyMappingsAsync(version);
        }
        catch (Exception e)
        {
            if (version != revision) return;
            headlineStatus.Text = "OBS 刷新失败：" + e.Message;
            Log("OBS 刷新失败：" + e.Message);
        }
        finally
        {
            if (version == revision) refreshButton.IsEnabled = obs.Connected;
        }
    }

    private async Task ApplyMappingsAsync(int version)
    {
        if (!inObsMode || !obs.Connected) return;
        CancelPendingSends();
        DisconnectSlots("读取 OBS 实际值");
        int bank = Math.Max(0, bankSelect.SelectedIndex);
        for (int slot = 0; slot < 4; slot++)
        {
            FaderBinding binding = mappingProfile.Get(bank, slot);
            string? name = mappingProfile.Resolve(bank, slot, availableInputs);
            if (name is null)
            {
                string reason = binding.Mode switch
                {
                    MappingMode.Unassigned => "未绑定 · 已禁用",
                    MappingMode.Explicit => "指定输入缺失 · 已禁用",
                    _ => "自动映射无输入 · 已禁用"
                };
                SetVisual(slot, 0, reason, false);
                continue;
            }

            try
            {
                double db = await obs.GetInputVolumeDbAsync(name);
                if (version != revision || !inObsMode || !obs.Connected) return;
                mappedInputs[slot] = name;
                channelNames[slot].Text = name;
                sliders[slot].IsEnabled = true;
                SetVisual(slot, ObsProtocol.DbToSlider(db), "Verified · OBS 已读取", true);
            }
            catch (Exception e)
            {
                if (version != revision) return;
                mappedInputs[slot] = null;
                channelNames[slot].Text = name;
                channelStatuses[slot].Text = "读取失败 · 禁止发送";
                sliders[slot].IsEnabled = false;
                Log($"OBS {name} 读取失败：{e.Message}");
            }
        }
        if (version == revision)
        {
            headlineStatus.Text = $"OBS 已连接 · {availableInputs.Count} 个可用输入 · Bank {bank + 1}";
            Log($"OBS Bank {bank + 1} 映射已应用");
        }
    }
}
