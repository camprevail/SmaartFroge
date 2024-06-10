using Newtonsoft.Json.Linq;
using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace SPL
{
    public partial class MainWindow : Window
    {
        private ClientWebSocket _webSocket;
        private BitmapImage _greenImage;
        private BitmapImage _yellowImage;
        private BitmapImage _redImage;
        private string _serverIpAddress;
        private int _serverPort;

        private readonly (string Name, uint Signature, int Port)[] products = new[]
        {
            ("Smaart Suite", 0x4F4C4548u, 25952),
            ("Smaart SPL", 0x4C505353u, 25951),
            ("Smaart RT", 0x21545253u, 25950),
            ("Smaart LE", 0x21454C53u, 25949)
        };

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            InitializeProductComboBox();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadImages();
        }

        private void LoadImages()
        {
            _greenImage = LoadImage("pack://application:,,,/Images/green.png");
            _yellowImage = LoadImage("pack://application:,,,/Images/yellow.png");
            _redImage = LoadImage("pack://application:,,,/Images/red.png");
        }

        private BitmapImage LoadImage(string imagePath)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            return bitmap;
        }

        private void InitializeProductComboBox()
        {
            foreach (var product in products)
            {
                ComboBoxItem item = new ComboBoxItem();
                item.Content = product.Name;
                item.Tag = product; // Store the entire product tuple as the Tag
                ProductComboBox.Items.Add(item);
            }
            ProductComboBox.SelectedIndex = 0; // Select the first item by default
        }

        private async void FindServerButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProductComboBox.SelectedItem != null)
            {
                try
                {
                    ComboBoxItem selectedProductItem = (ComboBoxItem)ProductComboBox.SelectedItem;
                    var product = ((string Name, uint Signature, int Port))selectedProductItem.Tag;

                    var serverInfo = await DiscoverSmaartServerAsync(product.Signature, product.Port);
                    if (serverInfo.Item1 != null)
                    {
                        _serverIpAddress = serverInfo.Item1;
                        _serverPort = serverInfo.Item2;
                        ShowStatusMessage($"Server found at {_serverIpAddress}:{_serverPort}");

                        await GetDevicesAsync(_serverIpAddress, _serverPort);
                    }
                    else
                    {
                        ShowStatusMessage("Server not found.");
                    }
                }
                catch (Exception ex)
                {
                    ShowStatusMessage($"Error finding server: {ex.Message}");
                }
            }
            else
            {
                ShowStatusMessage("Please select a product.");
            }
        }

        private async Task<(string, int)> DiscoverSmaartServerAsync(uint serverSignature, int serverPort)
        {
            try
            {
                var serverInfo = await DiscoverSmaartServer.DiscoverSmaartServerAsync(serverSignature, serverPort, ShowStatusMessage);
                return serverInfo;
            }
            catch (Exception ex)
            {
                ShowStatusMessage($"Failed to discover Smaart server: {ex.Message}");
                return (null, 0);
            }
        }

        private async Task GetDevicesAsync(string ipAddress, int port)
        {
            try
            {
                var endpoint = "/api/v4/";
                Uri serverUri = new Uri($"ws://{ipAddress}:{port}{endpoint}");

                using (var webSocket = new ClientWebSocket())
                {
                    await webSocket.ConnectAsync(serverUri, CancellationToken.None);

                    var requestPayload = new
                    {
                        action = "get",
                        target = "activeCalibratedInputs"
                    };
                    string jsonPayload = Newtonsoft.Json.JsonConvert.SerializeObject(requestPayload);
                    var bytesToSend = new ArraySegment<byte>(Encoding.UTF8.GetBytes(jsonPayload));
                    await webSocket.SendAsync(bytesToSend, WebSocketMessageType.Text, true, CancellationToken.None);

                    var buffer = new byte[1024 * 4];
                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    var jsonString = Encoding.UTF8.GetString(buffer, 0, result.Count);

                    ParseAndPopulateDevices(jsonString);
                    ParseAndPopulateMetrics(jsonString);
                }
            }
            catch (Exception ex)
            {
                ShowDeviceStatusMessage($"Failed to get devices: {ex.Message}");
            }
        }

        private void ParseAndPopulateDevices(string jsonString)
        {
            try
            {
                var jsonData = JObject.Parse(jsonString);
                var devices = jsonData["response"]?["devices"];

                DevicesComboBox.Items.Clear();

                if (devices != null)
                {
                    foreach (var device in devices)
                    {
                        var channels = device["activeCalibratedChannels"];
                        foreach (var channel in channels)
                        {
                            string channelName = channel["channelName"].ToString();
                            string streamEndpoint = channel["streamEndpoint"].ToString();

                            ComboBoxItem item = new ComboBoxItem();
                            item.Content = channelName;
                            item.Tag = streamEndpoint;
                            DevicesComboBox.Items.Add(item);
                        }
                    }

                    if (DevicesComboBox.Items.Count > 0)
                    {
                        DevicesComboBox.SelectedIndex = 0; // Select the first item by default
                        ShowDeviceStatusMessage("Devices populated.");
                    }
                    else
                    {
                        ShowDeviceStatusMessage("No devices found. Is logging started?");
                    }
                }
            }
            catch (Exception ex)
            {
                ShowDeviceStatusMessage($"Error parsing device list: {ex.Message}");
            }
        }

        private void ParseAndPopulateMetrics(string jsonString)
        {
            try
            {
                var jsonData = JObject.Parse(jsonString);
                var metrics = jsonData["response"]?["metrics"];

                MetricsComboBox.Items.Clear();

                if (metrics != null)
                {
                    var i = 0;
                    foreach (var metric in metrics)
                    {
                        string metricName = metric.ToString();
                        ComboBoxItem item = new ComboBoxItem();
                        item.Content = metricName;
                        item.Tag = i;
                        MetricsComboBox.Items.Add(item);
                        i += 1;
                    }

                    if (MetricsComboBox.Items.Count > 0)
                    {
                        MetricsComboBox.SelectedIndex = 0; // Select the first item by default
                    }
                }
            }
            catch (Exception ex)
            {
                ShowDeviceStatusMessage($"Error parsing metrics list: {ex.Message}");
            }
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (DevicesComboBox.SelectedItem != null && MetricsComboBox.SelectedItem != null)
            {
                try
                {
                    ComboBoxItem selectedDeviceItem = (ComboBoxItem)DevicesComboBox.SelectedItem;
                    string streamEndpoint = selectedDeviceItem.Tag.ToString();

                    await ConnectToStreamAsync(streamEndpoint);
                }
                catch (Exception ex)
                {
                    ShowDeviceStatusMessage($"Error connecting to stream: {ex.Message}");
                }
            }
            else
            {
                ShowDeviceStatusMessage("Please select a device and metrics.");
            }
        }

        private async Task ConnectToStreamAsync(string streamEndpoint)
        {
            try
            {
                Uri streamUri = new Uri($"ws://{_serverIpAddress}:{_serverPort}{streamEndpoint}");

                _webSocket = new ClientWebSocket();
                await _webSocket.ConnectAsync(streamUri, CancellationToken.None);
                SPLTextBlock.Text = "No data";
                await ReceiveMessagesAsync();
            }
            catch (Exception ex)
            {
                ShowDeviceStatusMessage($"Failed to connect to WebSocket: {ex.Message}");
            }
        }

        private async Task ReceiveMessagesAsync()
        {
            var buffer = new byte[1024 * 4];

            while (_webSocket.State == WebSocketState.Open)
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                var jsonString = Encoding.UTF8.GetString(buffer, 0, result.Count);
                UpdateSPLValue(jsonString);
            }
        }

        private void UpdateSPLValue(string jsonString)
        {
            try
            {
                var jsonData = JObject.Parse(jsonString);

                if (MetricsComboBox.SelectedItem != null)
                {
                    ComboBoxItem selectedMetricItem = (ComboBoxItem)MetricsComboBox.SelectedItem;
                    string selectedMetric = selectedMetricItem.Content.ToString();
                    var selectedMetricIndex = selectedMetricItem.Tag;
                    var SPLToken = jsonData["metrics"][selectedMetricIndex][selectedMetric];

                    if (SPLToken != null)
                    {
                        float SPLValue = SPLToken.ToObject<float>();
                        var SPLInt = (int)SPLValue;
                        string formattedSPLValue = SPLValue.ToString("F2");
                        Dispatcher.Invoke(() => {
                            SPLTextBlock.Text = $"{selectedMetric}\n{formattedSPLValue}";
                            UpdateStatusImage(SPLInt);
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                ShowDeviceStatusMessage($"Error updating SPL value: {ex.Message}");
            }
        }

        private void UpdateStatusImage(int SPLValue)
        {
            if (SPLValue > 102)
            {
                StatusImage.Source = _redImage;
            }
            else if (SPLValue > 99)
            {
                StatusImage.Source = _yellowImage;
            }
            else
            {
                StatusImage.Source = _greenImage;
            }
        }

        private void ShowStatusMessage(string message)
        {
            StatusLabel.Content = message;
        }

        private void ShowDeviceStatusMessage(string message)
        {
            DeviceStatusLabel.Content = message;
        }
    }
}
