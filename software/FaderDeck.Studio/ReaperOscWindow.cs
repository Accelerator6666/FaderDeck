using System.Net.Sockets;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FaderDeck.Core;
using FaderDeck.Reaper;

namespace FaderDeck.Studio;

/// <summary>Independent REAPER OSC preview; no hardware and no implied UDP handshake.</summary>
public sealed class ReaperOscWindow : Window
{
    private readonly ReaperOscClient reaper = new();
    private readonly ControlProfiles profiles;
    private readonly bool allowSave;
    private readonly ComboBox bank;
    private readonly ComboBox[] targets = new ComboBox[4];
    private readonly Slider[] sliders = new Slider[4];
    private readonly TextBlock[] faderStatus = new TextBlock[4];
    private readonly TextBlock[] faderValue = new TextBlock[4];
    private readonly TextBlock summary;
    private readonly TextBlock saveStatus;
    private readonly Button startButton;
    private readonly Button stopButton;
    private readonly TextBox destinationPort;
    private readonly TextBox feedbackPort;
    private readonly int?[] mappedTracks = new int?[4];
    private readonly Dictionary<int, double> knownVolumes = new();
    private readonly CancellationTokenSource?[] pending = new CancellationTokenSource?[4];
    private int generation;
    private bool updatingControls;

    private sealed record TrackOption(string Caption, ControlBinding Binding)
    {
        public override string ToString() => Caption;
    }

    public ReaperOscWindow()
    {
        Title = "FaderDeck · REAPER OSC Preview";
        Width = 1040;
        Height = 820;
        MinWidth = 790;
        MinHeight = 570;
        Background = new SolidColorBrush(Color.Parse("#101827"));

        string? loadError = null;
        try
        {
            profiles = ControlProfiles.LoadOrImport(ControlProfiles.DefaultPath, ObsMappingProfile.DefaultFilePath);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            profiles = new ControlProfiles();
            loadError = e.Message;
        }
        allowSave = loadError is null;

        var heading = new StackPanel { Spacing = 5 };
        heading.Children.Add(new TextBlock
        {
            Text = "REAPER OSC · 四路虚拟推子",
            FontSize = 25,
            FontWeight = FontWeight.SemiBold
        });
        heading.Children.Add(new TextBlock
        {
            Text = "本机 UDP 控制 · 不需要 FaderBuddy 或 ESP32-S3 · 仅收到 OSC 回传才是 Verified",
            Foreground = new SolidColorBrush(Color.Parse("#95AAC6"))
        });

        bank = new ComboBox
        {
            Width = 145,
            ItemsSource = Enumerable.Range(1, 8).Select(n => $"Bank {n}").ToArray(),
            SelectedIndex = 0
        };
        destinationPort = new TextBox { Text = "8000", Width = 105, Watermark = "REAPER RX" };
        feedbackPort = new TextBox { Text = "9000", Width = 105, Watermark = "Local RX" };
        startButton = new Button { Content = "启动 OSC" };
        stopButton = new Button { Content = "停止", IsEnabled = false };
        var row = new StackPanel { Spacing = 10, Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = "Bank", VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(bank);
        row.Children.Add(new TextBlock { Text = "REAPER 接收端口", VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(destinationPort);
        row.Children.Add(new TextBlock { Text = "本机回传端口", VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(feedbackPort);
        row.Children.Add(startButton);
        row.Children.Add(stopButton);

        summary = new TextBlock
        {
            Text = "未启动 UDP · 尚未收到 REAPER 反馈",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#E9BE83"))
        };
        saveStatus = new TextBlock
        {
            Text = allowSave
                ? $"跨软件 Profile 保存位置：{ControlProfiles.DefaultPath}"
                : $"配置读取失败；为防止覆盖文件已禁用编辑：{loadError}",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse(allowSave ? "#95AAC6" : "#E9BE83"))
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
            ColumnSpacing = 14
        };
        for (int i = 0; i < 4; i++)
        {
            int slot = i;
            var card = new StackPanel { Spacing = 11 };
            card.Children.Add(new TextBlock
            {
                Text = $"FADER {i + 1}",
                TextAlignment = TextAlignment.Center,
                FontWeight = FontWeight.SemiBold,
                FontSize = 16
            });
            targets[i] = new ComboBox
            {
                MinWidth = 145,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsEnabled = allowSave
            };
            targets[i].SelectionChanged += (_, _) => BindingChanged(slot);
            faderValue[i] = new TextBlock
            {
                Text = "—",
                TextAlignment = TextAlignment.Center,
                FontSize = 25,
                Foreground = new SolidColorBrush(Color.Parse("#E9BE83"))
            };
            sliders[i] = new Slider
            {
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                IsEnabled = false,
                Width = 65,
                Height = 270,
                HorizontalAlignment = HorizontalAlignment.Center,
                Orientation = Orientation.Vertical
            };
            sliders[i].ValueChanged += (_, _) => SliderChanged(slot);
            faderStatus[i] = new TextBlock
            {
                Text = "Unknown / 未同步",
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#95AAC6"))
            };
            card.Children.Add(targets[i]);
            card.Children.Add(faderValue[i]);
            card.Children.Add(sliders[i]);
            card.Children.Add(faderStatus[i]);
            var background = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#1B293E")),
                BorderBrush = new SolidColorBrush(Color.Parse("#30465F")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(16),
                Child = card
            };
            Grid.SetColumn(background, i);
            grid.Children.Add(background);
        }

        var reset = new Button { Content = "恢复当前 Bank 默认分配", IsEnabled = allowSave };
        reset.Click += (_, _) =>
        {
            profiles.ResetBank(ControlAdapters.Reaper, bank.SelectedIndex);
            Save();
            RefreshChoices();
            ApplyBank();
        };
        var footer = new TextBlock
        {
            Text = "提示：REAPER 的 OSC 控制面需自行设置监听和反馈端口；UDP 启动不等于 REAPER 在线。未收到音轨反馈时，推子允许显式发送但不会显示 Verified。纯软件预览不驱动实体电机。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#95AAC6"))
        };
        var root = new StackPanel { Margin = new Thickness(24), Spacing = 20 };
        root.Children.Add(heading);
        root.Children.Add(row);
        root.Children.Add(summary);
        root.Children.Add(new TextBlock { Text = "每一路推子可选自动音轨、指定 Track 1–32 或不绑定", FontWeight = FontWeight.SemiBold });
        root.Children.Add(grid);
        root.Children.Add(reset);
        root.Children.Add(saveStatus);
        root.Children.Add(footer);
        Content = new ScrollViewer { Content = root };

        bank.SelectionChanged += (_, _) => { RefreshChoices(); ApplyBank(); };
        startButton.Click += (_, _) => Start();
        stopButton.Click += async (_, _) => await StopAsync();
        reaper.VolumeFeedback += (track, normalized) => Dispatcher.UIThread.Post(() => OnFeedback(track, normalized));
        reaper.Stopped += message => Dispatcher.UIThread.Post(() =>
        {
            summary.Text = "OSC 回传监听异常：" + message;
            ClearValues();
        });
        Closed += async (_, _) =>
        {
            CancelAll();
            await reaper.DisposeAsync();
        };
        RefreshChoices();
        ApplyBank();
    }

    private void CancelAll()
    {
        for (int i = 0; i < 4; i++)
        {
            pending[i]?.Cancel();
            pending[i]?.Dispose();
            pending[i] = null;
        }
    }

    private void UpdateSlider(int slot, double percent, string status, bool verified)
    {
        updatingControls = true;
        try
        {
            sliders[slot].Value = percent;
            faderValue[slot].Text = $"{percent:0}%";
            faderValue[slot].Foreground = new SolidColorBrush(Color.Parse(verified ? "#66D3C3" : "#E9BE83"));
            faderStatus[slot].Text = status;
        }
        finally { updatingControls = false; }
    }

    private void RefreshChoices()
    {
        updatingControls = true;
        try
        {
            int currentBank = Math.Max(0, bank.SelectedIndex);
            for (int i = 0; i < 4; i++)
            {
                var choices = new List<TrackOption>
                {
                    new("自动音轨", ControlBinding.Auto),
                    new("不绑定", ControlBinding.Unassigned)
                };
                choices.AddRange(Enumerable.Range(1, 32).Select(track =>
                    new TrackOption($"Track {track}", ControlBinding.Bind("track.volume", track.ToString()))));
                var selected = profiles.Get(ControlAdapters.Reaper, currentBank, i);
                targets[i].ItemsSource = choices;
                targets[i].SelectedItem = choices.First(x => x.Binding == selected);
            }
        }
        finally { updatingControls = false; }
    }

    private void Save()
    {
        try
        {
            profiles.Save(ControlProfiles.DefaultPath);
            saveStatus.Text = $"映射已保存 · {ControlProfiles.DefaultPath}";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            saveStatus.Text = "保存失败，设置仅驻留在内存中：" + e.Message;
        }
    }

    private void BindingChanged(int slot)
    {
        if (updatingControls || !allowSave) return;
        if (targets[slot].SelectedItem is not TrackOption option) return;
        profiles.Set(ControlAdapters.Reaper, bank.SelectedIndex, slot, option.Binding);
        Save();
        ApplyBank();
    }

    private void ApplyBank()
    {
        generation++;
        CancelAll();
        int currentBank = Math.Max(0, bank.SelectedIndex);
        for (int i = 0; i < 4; i++)
        {
            ResolvedControl? binding = profiles.Resolve(ControlAdapters.Reaper, currentBank, i);
            mappedTracks[i] = binding is { } res ? int.Parse(res.Target) : null;
            sliders[i].IsEnabled = reaper.Listening && mappedTracks[i] is not null;
            if (mappedTracks[i] is int track && knownVolumes.TryGetValue(track, out double amount) && reaper.Listening)
                UpdateSlider(i, amount * 100, $"Verified · Track {track} OSC feedback", true);
            else
                UpdateSlider(i, 0, mappedTracks[i] is null
                    ? "未绑定 · 已禁用"
                    : $"Track {mappedTracks[i]} · Unknown（尚无反馈）", false);
        }
    }

    private void ClearValues()
    {
        knownVolumes.Clear();
        ApplyBank();
    }

    private void Start()
    {
        if (!int.TryParse(destinationPort.Text, out int reaperPort) ||
            !int.TryParse(feedbackPort.Text, out int hostPort))
        {
            summary.Text = "端口必须是 1–65535 的整数。";
            return;
        }
        try
        {
            reaper.Start(reaperPort, hostPort);
            destinationPort.IsEnabled = false;
            feedbackPort.IsEnabled = false;
            startButton.IsEnabled = false;
            stopButton.IsEnabled = true;
            knownVolumes.Clear();
            summary.Text = $"UDP 已启动 · 发送至 127.0.0.1:{reaperPort} · 等待回传到 127.0.0.1:{hostPort}（不代表 REAPER 在线）";
            ApplyBank();
        }
        catch (Exception e) when (e is SocketException or ArgumentException or InvalidOperationException)
        {
            summary.Text = "UDP 启动失败：" + e.Message;
        }
    }

    private async Task StopAsync()
    {
        generation++;
        CancelAll();
        await reaper.DisposeAsync();
        destinationPort.IsEnabled = true;
        feedbackPort.IsEnabled = true;
        startButton.IsEnabled = true;
        stopButton.IsEnabled = false;
        summary.Text = "UDP 已停止 · 所有音轨反馈已失效";
        ClearValues();
    }

    private void OnFeedback(int track, double value)
    {
        if (!reaper.Listening) return;
        knownVolumes[track] = value;
        summary.Text = $"已收到 REAPER 格式 OSC 回传 · Track {track} · {value * 100:0}%（本机 UDP）";
        for (int slot = 0; slot < 4; slot++)
        {
            if (mappedTracks[slot] == track)
                UpdateSlider(slot, value * 100, $"Verified · Track {track} OSC feedback", true);
        }
    }

    private void SliderChanged(int slot)
    {
        if (updatingControls || !reaper.Listening || mappedTracks[slot] is not int track) return;
        double value = sliders[slot].Value / 100;
        faderValue[slot].Text = $"{value * 100:0}%";
        faderStatus[slot].Text = "发送中 · UDP 未确认";
        pending[slot]?.Cancel();
        pending[slot]?.Dispose();
        var cts = new CancellationTokenSource();
        pending[slot] = cts;
        _ = SendAsync(slot, track, value, generation, cts.Token);
    }

    private async Task SendAsync(int slot, int track, double value, int version, CancellationToken token)
    {
        try
        {
            await Task.Delay(80, token);
            if (version != generation || mappedTracks[slot] != track || !reaper.Listening) return;
            await reaper.WriteTrackVolumeAsync(track, value, token);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (version == generation && mappedTracks[slot] == track && !token.IsCancellationRequested)
                    faderStatus[slot].Text = $"Track {track} · 已发送（未确认）";
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (version == generation && mappedTracks[slot] == track)
                    faderStatus[slot].Text = "发送失败：" + e.Message;
            });
        }
    }
}
