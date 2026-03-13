using System.Net.Sockets;
using System.Net;
using System.Text;
using ZXing.Net.Maui;

namespace MauiApp1;

public partial class MainPage : ContentPage
{
    private Socket? _socket;
    private bool _isConnected = false;
    private readonly object _lock = new();

    // 功能码对应列表 (请确保这里与 SettingsPage.xaml 中的顺序一致)
    private readonly string[] _funcList = new[]
    {
        "原点偏移", // 对应 01
        "待定1",    // 对应 02
        "待定2",    // 对应 03
        "待定3",    // 对应 04
        "待定4"     // 对应 05
    };

    public MainPage()
    {
        InitializeComponent();

        // 初始化时加载上次的补光灯偏好
        bool isTorchOn = Preferences.Get("UserTorchPreference", false);
        TorchSwitch.IsToggled = isTorchOn;
        TorchStatusLabel.Text = isTorchOn ? "补光灯: 开" : "补光灯: 关";

        if (BarcodeReader != null)
        {
            BarcodeReader.Options = new BarcodeReaderOptions
            {
                Formats = BarcodeFormat.QrCode | BarcodeFormat.Code128 | BarcodeFormat.Ean13,
                AutoRotate = true,   // 保持开启，允许任意角度扫描
                Multiple = false,    // 保持关闭，一次只扫一个
                TryHarder = true     // 保持开启，会进行深度分析，虽然慢微毫秒但识别率高
            };
        }
        StartAutoConnectLoop();
    }

    private async void OnStartScanClicked(object sender, EventArgs e)
    {
        ScannerOverlay.IsVisible = true;
        await Task.Delay(300);
        BarcodeReader.IsDetecting = true;

        // 根据记录的偏好设置补光灯状态
        try
        {
            BarcodeReader.IsTorchOn = TorchSwitch.IsToggled;
        }
        catch { /* 设备不支持 */ }
    }

    // 补光灯切换逻辑
    private void OnTorchToggled(object sender, ToggledEventArgs e)
    {
        bool isOn = e.Value;

        // 1. 立即保存用户偏好
        Preferences.Set("UserTorchPreference", isOn);

        // 2. 更新UI文字
        TorchStatusLabel.Text = isOn ? "补光灯: 开" : "补光灯: 关";

        // 3. 如果当前正在扫描，立即切换物理灯光
        if (BarcodeReader != null && ScannerOverlay.IsVisible)
        {
            try
            {
                BarcodeReader.IsTorchOn = isOn;
            }
            catch { }
        }
    }

    //private async void OnStartScanClicked(object sender, EventArgs e)
    //{
    //    ScannerOverlay.IsVisible = true;

    //    // 给 UI 一点渲染时间
    //    await Task.Delay(300);

    //    BarcodeReader.IsDetecting = true;

    //    // 💡 这是一个“黑科技”补丁：
    //    // 如果画面模糊，在启动瞬间闪一下灯，可以强迫摄像头重新寻找焦距
    //    try
    //    {
    //        BarcodeReader.IsTorchOn = true;
    //        await Task.Delay(150);
    //        BarcodeReader.IsTorchOn = false;
    //    }
    //    catch { /* 部分设备不支持闪光灯则跳过 */ }
    //}

    // 页面销毁或隐藏时，务必关闭摄像头，防止资源占用导致下次黑屏
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (BarcodeReader != null)
        {
            BarcodeReader.IsDetecting = false;
            ScannerOverlay.IsVisible = false;
            try { BarcodeReader.IsTorchOn = false; } catch { }
        }
    }

    // --- 网络状态监听 ---
    private void OnConnectivityChanged(object sender, ConnectivityChangedEventArgs e)
    {
        // 如果当前没有网络访问权限，强制断开
        if (e.NetworkAccess != NetworkAccess.Internet && e.NetworkAccess != NetworkAccess.ConstrainedInternet && e.NetworkAccess != NetworkAccess.Local)
        {
            DisconnectSocket("网络连接已断开");
        }
    }

    // --- 数值控制逻辑 ---
    private void OnPlusClicked(object sender, EventArgs e)
    {
        if (int.TryParse(CounterEntry.Text, out int val))
            CounterEntry.Text = (val + 1).ToString();
    }

    private void OnMinusClicked(object sender, EventArgs e)
    {
        if (int.TryParse(CounterEntry.Text, out int val) && val > 0)
            CounterEntry.Text = (val - 1).ToString();
    }

    private void OnCounterUnfocused(object sender, FocusEventArgs e)
    {
        if (!int.TryParse(CounterEntry.Text, out int val) || val < 0)
            CounterEntry.Text = "0";
    }

    // --- 自动重连逻辑 ---
    private void StartAutoConnectLoop()
    {
        Task.Run(async () =>
        {
            while (true)
            {
                if (_isConnected)
                {
                    // 轮询检查 Socket 物理状态 (解决拔网线/关WiFi后状态不更新的问题)
                    if (_socket == null || !IsSocketConnected(_socket))
                    {
                        DisconnectSocket("服务器连接中断");
                    }
                }

                if (!_isConnected)
                {
                    // 检查手机是否有网络，没网就不尝试连服务器了
                    if (Connectivity.Current.NetworkAccess != NetworkAccess.None &&
                        Connectivity.Current.NetworkAccess != NetworkAccess.Unknown)
                    {
                        await TryConnect();
                    }
                }

                await Task.Delay(3000);
            }
        });
    }

    // 深度检查 Socket 连接状态
    private bool IsSocketConnected(Socket s)
    {
        try
        {
            return !((s.Poll(1000, SelectMode.SelectRead) && (s.Available == 0)) || !s.Connected);
        }
        catch
        {
            return false;
        }
    }

    private async Task TryConnect()
    {
        string ip = Preferences.Get("IP", "192.168.6.6");
        if (!int.TryParse(Preferences.Get("Port", "5050"), out int port)) port = 8080;

        try
        {
            Socket tempSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            // 异步连接
            var connectTask = tempSocket.ConnectAsync(IPAddress.Parse(ip), port);

            // 设置 2 秒超时
            var completedTask = await Task.WhenAny(connectTask, Task.Delay(2000));

            if (completedTask == connectTask && tempSocket.Connected)
            {
                lock (_lock) { _socket = tempSocket; _isConnected = true; }
                UpdateUIStatus(true);
            }
            else
            {
                // 超时或失败
                tempSocket.Close();
                UpdateUIStatus(false);
            }
        }
        catch
        {
            UpdateUIStatus(false);
        }
    }

    private void DisconnectSocket(string reason)
    {
        lock (_lock)
        {
            if (_socket != null)
            {
                try { _socket.Close(); } catch { }
                _socket = null;
            }
            _isConnected = false;
        }
        UpdateUIStatus(false);
        // 可选：记录日志 AddLog($"[系统] {reason}", Colors.Red);
    }

    private void UpdateUIStatus(bool connected)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            StatusLabel.Text = connected ? "✅ 已连接服务器" : "❌ 未连接 (自动重试中...)";
            StatusFrame.BackgroundColor = connected ? Colors.Green : Colors.Red;
        });
    }

    // --- 扫码逻辑 ---
    //private async void OnStartScanClicked(object sender, EventArgs e)
    //{
    //    // 1. 先显示遮罩层
    //    ScannerOverlay.IsVisible = true;

    //    // 2. 【关键修复】增加微小延时，等待UI渲染完毕后再开启摄像头检测
    //    // 这通常能解决部分设备上点击后黑屏的问题
    //    await Task.Delay(100);

    //    BarcodeReader.IsDetecting = true;
    //}

    private void OnStopScanClicked(object sender, EventArgs e)
    {
        BarcodeReader.IsDetecting = false;
        try { BarcodeReader.IsTorchOn = false; } catch { }
        ScannerOverlay.IsVisible = false;
    }

    private void OnBarcodesDetected(object sender, BarcodeDetectionEventArgs e)
    {
        var result = e.Results?.FirstOrDefault();
        if (result == null) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            BarcodeReader.IsDetecting = false;
            ScannerOverlay.IsVisible = false;
            try { BarcodeReader.IsTorchOn = false; } catch { } // 添加这行来关闭闪光灯
            try { Vibration.Default.Vibrate(); } catch { }
            ProcessAndSend(result.Value);
        });
    }

    // --- 数据封装与发送 (新格式 + 功能码映射) ---
    private void ProcessAndSend(string scanCode)
    {
        // 1. 读取参数
        string prefix = Preferences.Get("Prefix", "");
        string funcName = Preferences.Get("Func", "待定1"); // 获取功能名称
        string lenStr = Preferences.Get("Len", "0");
        string sep = Preferences.Get("Sep", ",");
        string endChar = Preferences.Get("EndChar", "");

        // 修改为：
        string inputData;
        if (int.TryParse(CounterEntry.Text, out int inputNum))
        {
            // 格式化为至少两位的十六进制，大写字母
            inputData = inputNum.ToString("X2");
        }
        else
        {
            // 如果解析失败，保持原样
            inputData = CounterEntry.Text;
        }

        // 2. 【关键修改】功能码名称转数字 (01, 02...)
        int index = Array.IndexOf(_funcList, funcName);
        string funcCode;
        if (index >= 0)
        {
            // 找到索引，转换为两位数 (例如 index 0 -> "01")
            funcCode = (index + 1).ToString("D2");
        }
        else
        {
            // 如果没找到（或者是"无"），视情况处理，这里默认给 "00" 或者不发送
            funcCode = (funcName == "无") ? "" : "00";
        }

        // 3. 长度校验
        if (int.TryParse(lenStr, out int targetLen) && targetLen > 0)
        {
            if (scanCode.Length != targetLen)
            {
                AddLog($"⚠️ 校验失败: 长度应为{targetLen}, 实测{scanCode.Length}", Colors.Orange);
                return;
            }
        }

        // 4. 构建格式：起始符 + 功能码(数字) + 校验长度 + 输入框数据 + 扫码数据 + 分隔符 + 结束码
        StringBuilder sb = new();
        sb.Append(prefix);
        sb.Append(funcCode); // 这里现在是 01, 02 等
        sb.Append(lenStr);
        sb.Append(inputData);
        sb.Append(scanCode);
        sb.Append(sep);
        sb.Append(endChar);

        string finalData = sb.ToString();
        SendData(finalData);
    }

    private void SendData(string data)
    {
        // 再次检查连接状态
        if (!_isConnected || _socket == null)
        {
            AddLog($"❌ 未连接，丢弃: {data}", Colors.Red);
            // 尝试触发一次断线检测
            DisconnectSocket("发送失败，判定为断开");
            return;
        }

        try
        {
            _socket.Send(Encoding.UTF8.GetBytes(data));
            AddLog($"📤 已发送: {data}", Colors.Black);

            // 发送成功后自动 +1
            MainThread.BeginInvokeOnMainThread(() => {
                if (int.TryParse(CounterEntry.Text, out int val))
                    CounterEntry.Text = (val + 1).ToString();
            });
        }
        catch
        {
            // 发送异常，说明连接已断
            DisconnectSocket("发送异常");
            AddLog("❌ 连接已断开", Colors.Red);
        }
    }

    private void AddLog(string msg, Color color)
    {
        string time = DateTime.Now.ToString("HH:mm:ss");
        LogEditor.Text = $"[{time}] {msg}\n" + LogEditor.Text;
    }

    private void OnClearLogClicked(object sender, EventArgs e) => LogEditor.Text = "";
}