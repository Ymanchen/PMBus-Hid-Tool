// �ļ���: MainWindow.xaml.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace PMBusHidTool
{
    public partial class MainWindow : Window
    {
        private readonly HidSmbusService _hidService;
        private readonly PmbusService _pmbusService;
        private List<byte> _foundAddresses = new List<byte>();
        private DispatcherTimer _autoRefreshTimer;

        public ObservableCollection<PmbusParameter> PmbusParameters { get; set; }
        public ObservableCollection<KeyValuePair<string, string>> DeviceInfos { get; set; }

        public MainWindow()
        {
            InitializeComponent();
            _hidService = new HidSmbusService();
            _pmbusService = new PmbusService(_hidService);
            
            PmbusParameters = new ObservableCollection<PmbusParameter>();
            PmbusDataGrid.ItemsSource = PmbusParameters;

            DeviceInfos = new ObservableCollection<KeyValuePair<string, string>>();
            DeviceInfoGrid.ItemsSource = DeviceInfos;

            _autoRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _autoRefreshTimer.Tick += async (s, e) => await RefreshMonitoringData();

            Log("��ӭʹ�� PMBus HID ��λ�� v3.0��������Ӳ����ɨ���豸��");
        }
        
        private void Log(string message)
        {
            Dispatcher.Invoke(() =>
            {
                LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
                LogTextBox.ScrollToEnd();
            });
        }

        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            Log("��ʼɨ���豸...");
            SetUiEnabled(false);
            AddressComboBox.Items.Clear();

            await Task.Run(() =>
            {
                if (!_hidService.IsConnected()) _hidService.Connect();
                if (_hidService.IsConnected()) _foundAddresses = _hidService.ScanAddresses();
            });

            if (!_hidService.IsConnected())
            {
                Log("����: δ�ҵ�ָ����HID�豸������VID/PID���豸���ӡ�");
                MessageBox.Show("δ�ҵ�ָ����HID�豸��\n����Ӳ�����ӣ���ȷ��HidSmbusService.cs�е�VID/PID�Ƿ���ȷ��", "���Ӵ���", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else if (_foundAddresses.Any())
            {
                Log($"ɨ����ɣ����� {_foundAddresses.Count} ���豸: {string.Join(", ", _foundAddresses.Select(a => $"0x{a:X2}"))}");
                foreach (var addr in _foundAddresses)
                {
                    AddressComboBox.Items.Add($"0x{addr:X2}");
                }
                AddressComboBox.SelectedIndex = 0;
            }
            else
            {
                Log("ɨ����ɣ���δ��I2C�����Ϸ����κ���Ӧ�豸��");
            }
            ScanButton.IsEnabled = true;
        }

        private async void AddressComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool isDeviceSelected = AddressComboBox.SelectedItem != null;
            SetUiEnabled(isDeviceSelected);

            if (isDeviceSelected)
            {
                _autoRefreshTimer.Start();
                await RefreshAllData();
            }
            else
            {
                _autoRefreshTimer.Stop();
            }
        }

        private async Task RefreshAllData()
        {
            await RefreshMonitoringData();
            await RefreshDeviceInfo();
            await RefreshConfigurationData();
        }

        private async Task RefreshMonitoringData()
        {
            if (AddressComboBox.SelectedIndex == -1) return;
            byte selectedAddress = _foundAddresses[AddressComboBox.SelectedIndex];
            Log($"���ڴӵ�ַ 0x{selectedAddress:X2} ˢ�¼������...");

            var results = await Task.Run(() => _pmbusService.ReadAllMonitoredParameters(selectedAddress));

            UpdateParameter("�����ѹ", "V", results.Vout);
            UpdateParameter("�������", "A", results.Iout);
            UpdateParameter("�����ѹ", "V", results.Vin);
            UpdateParameter("�¶�", "��C", results.Temperature);
            UpdateParameter("״̬��", "", results.StatusWord);

            if (results.StatusWord.HasValue)
            {
                StatusDecodeTextBox.Text = PmbusStatusDecoder.DecodeStatusWord(results.StatusWord.Value.RawValue);
            }
            else
            {
                StatusDecodeTextBox.Text = "��ȡ״̬��ʧ�ܡ�";
            }
            Log("��������Ѹ��¡�");
        }

        private async Task RefreshDeviceInfo()
        {
            if (AddressComboBox.SelectedIndex == -1) return;
            byte selectedAddress = _foundAddresses[AddressComboBox.SelectedIndex];
            Log($"���ڶ�ȡ�豸��Ϣ 0x{selectedAddress:X2}...");

            DeviceInfos.Clear();
            var info = await Task.Run(() => _pmbusService.ReadDeviceInformation(selectedAddress));
            
            DeviceInfos.Add(new KeyValuePair<string, string>("������ (MFR_ID)", info.MfrId ?? "N/A"));
            DeviceInfos.Add(new KeyValuePair<string, string>("�ͺ� (MFR_MODEL)", info.MfrModel ?? "N/A"));
            DeviceInfos.Add(new KeyValuePair<string, string>("Ӳ���汾 (MFR_REVISION)", info.MfrRevision ?? "N/A"));
            DeviceInfos.Add(new KeyValuePair<string, string>("���к� (MFR_SERIAL)", info.MfrSerial ?? "N/A"));
            DeviceInfos.Add(new KeyValuePair<string, string>("PMBus �汾 (PMBUS_REVISION)", info.PmbusRevision ?? "N/A"));
            Log("�豸��Ϣ�Ѹ��¡�");
        }
        
        private async Task RefreshConfigurationData()
        {
            if (AddressComboBox.SelectedIndex == -1) return;
            byte selectedAddress = _foundAddresses[AddressComboBox.SelectedIndex];
            Log($"���ڶ�ȡ�������� 0x{selectedAddress:X2}...");

            var voutOvLimit = await Task.Run(() => _pmbusService.ReadVoutOvFaultLimit(selectedAddress));
            VoutOvFaultLimitTextBox.Text = voutOvLimit?.ToString("F2") ?? "N/A";

            var ioutOcLimit = await Task.Run(() => _pmbusService.ReadIoutOcFaultLimit(selectedAddress));
            IoutOcFaultLimitTextBox.Text = ioutOcLimit?.ToString("F2") ?? "N/A";

            var otLimit = await Task.Run(() => _pmbusService.ReadOtFaultLimit(selectedAddress));
            OtFaultLimitTextBox.Text = otLimit?.ToString("F2") ?? "N/A";
            
            Log("���������Ѹ��¡�");
        }

        private void UpdateParameter(string name, string unit, PmbusReadResult? result)
        {
            var param = PmbusParameters.FirstOrDefault(p => p.Name == name);
            if (param == null)
            {
                param = new PmbusParameter { Name = name, Unit = unit };
                PmbusParameters.Add(param);
            }

            if (result.HasValue)
            {
                param.Value = result.Value.ConvertedValue.ToString("F3");
                param.RawValue = $"0x{result.Value.RawValue:X4}";
            }
            else
            {
                param.Value = "��ȡʧ��";
                param.RawValue = "N/A";
            }
        }

        private async void ClearFaultsButton_Click(object sender, RoutedEventArgs e)
        {
            if (AddressComboBox.SelectedIndex == -1) return;
            byte selectedAddress = _foundAddresses[AddressComboBox.SelectedIndex];
            Log($"���� CLEAR_FAULTS ����ַ 0x{selectedAddress:X2}...");
            bool success = await Task.Run(() => _pmbusService.ClearFaults(selectedAddress));
            Log(success ? "�����������ͳɹ���" : "������������ʧ�ܡ�");
            if(success) await RefreshMonitoringData();
        }

        private async void TurnOnButton_Click(object sender, RoutedEventArgs e)
        {
            if (AddressComboBox.SelectedIndex == -1) return;
            byte selectedAddress = _foundAddresses[AddressComboBox.SelectedIndex];
            Log($"���� ON �����ַ 0x{selectedAddress:X2}...");
            bool success = await Task.Run(() => _pmbusService.SetOperation(selectedAddress, PmbusOperation.On));
            Log(success ? "��������ͳɹ���" : "���������ʧ�ܡ�");
        }

        private async void TurnOffButton_Click(object sender, RoutedEventArgs e)
        {
            if (AddressComboBox.SelectedIndex == -1) return;
            byte selectedAddress = _foundAddresses[AddressComboBox.SelectedIndex];
            Log($"���� OFF �����ַ 0x{selectedAddress:X2}...");
            bool success = await Task.Run(() => _pmbusService.SetOperation(selectedAddress, PmbusOperation.Off));
            Log(success ? "�ض�����ͳɹ���" : "�ض������ʧ�ܡ�");
        }

        private async void SetLimitButton_Click(object sender, RoutedEventArgs e)
        {
            if (AddressComboBox.SelectedIndex == -1) return;
            byte selectedAddress = _foundAddresses[AddressComboBox.SelectedIndex];
            var button = sender as Button;
            if (button == null) return;

            string tag = button.Tag.ToString();
            bool success = false;

            switch (tag)
            {
                case "VOUT_OV":
                    if (double.TryParse(VoutOvFaultLimitTextBox.Text, out double voutLimit))
                    {
                        Log($"���� VOUT_OV_FAULT_LIMIT Ϊ {voutLimit}V...");
                        success = await Task.Run(() => _pmbusService.SetVoutOvFaultLimit(selectedAddress, voutLimit));
                    }
                    break;
                case "IOUT_OC":
                    if (double.TryParse(IoutOcFaultLimitTextBox.Text, out double ioutLimit))
                    {
                        Log($"���� IOUT_OC_FAULT_LIMIT Ϊ {ioutLimit}A...");
                        success = await Task.Run(() => _pmbusService.SetIoutOcFaultLimit(selectedAddress, ioutLimit));
                    }
                    break;
                case "OT":
                    if (double.TryParse(OtFaultLimitTextBox.Text, out double otLimit))
                    {
                        Log($"���� OT_FAULT_LIMIT Ϊ {otLimit}��C...");
                        success = await Task.Run(() => _pmbusService.SetOtFaultLimit(selectedAddress, otLimit));
                    }
                    break;
            }
            Log(success ? "���óɹ���" : "����ʧ�ܻ�������Ч��");
            if(success) await RefreshConfigurationData();
        }

        private async void ManualReadButton_Click(object sender, RoutedEventArgs e)
        {
            if (AddressComboBox.SelectedIndex == -1) return;
            byte selectedAddress = _foundAddresses[AddressComboBox.SelectedIndex];

            if (!byte.TryParse(ManualCommandCodeTextBox.Text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte commandCode))
            {
                MessageBox.Show("������������һ����Ч��16������ (����: 8B)��", "�������", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Log($"�ֶ���ȡ: ��ַ=0x{selectedAddress:X2}, ����=0x{commandCode:X2}");
            var result = await Task.Run(() => _pmbusService.ExecuteReadWord(selectedAddress, commandCode));
            if (result.HasValue)
            {
                ManualReadResultTextBox.Text = $"0x{result.Value:X4}";
                Log($"��ȡ�ɹ�: �յ� 0x{result.Value:X4}");
            }
            else
            {
                ManualReadResultTextBox.Text = "��ȡʧ��";
                Log("��ȡʧ�ܡ�");
            }
        }

        private async void ManualWriteButton_Click(object sender, RoutedEventArgs e)
        {
            if (AddressComboBox.SelectedIndex == -1) return;
            byte selectedAddress = _foundAddresses[AddressComboBox.SelectedIndex];

            if (!byte.TryParse(ManualCommandCodeTextBox.Text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte commandCode))
            {
                MessageBox.Show("������������һ����Ч��16������ (����: 8B)��", "�������", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!ushort.TryParse(ManualWriteValueTextBox.Text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort value))
            {
                MessageBox.Show("д�����ݱ�����һ����Ч��16������ (0000-FFFF)��", "�������", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Log($"�ֶ�д����: ��ַ=0x{selectedAddress:X2}, ����=0x{commandCode:X2}, ֵ=0x{value:X4}");
            bool success = await Task.Run(() => _pmbusService.ExecuteWriteWord(selectedAddress, commandCode, value));
            Log(success ? "д��ɹ���" : "д��ʧ�ܡ�");
        }

        private void SetUiEnabled(bool isEnabled)
        {
            ClearFaultsButton.IsEnabled = isEnabled;
            ManualReadButton.IsEnabled = isEnabled;
            ManualWriteButton.IsEnabled = isEnabled;
            TurnOnButton.IsEnabled = isEnabled;
            TurnOffButton.IsEnabled = isEnabled;
            
            // Enable all configuration "Set" buttons
            SetVoutOvFaultLimitButton.IsEnabled = isEnabled;
            SetIoutOcFaultLimitButton.IsEnabled = isEnabled;
            SetOtFaultLimitButton.IsEnabled = isEnabled;
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            _hidService.Disconnect();
            base.OnClosing(e);
        }
    }

    public class PmbusParameter : INotifyPropertyChanged
    {
        private string _value;
        private string _rawValue;
        public string Name { get; set; }
        public string Unit { get; set; }
        public string Value
        {
            get => _value;
            set { _value = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value))); }
        }
        public string RawValue
        {
            get => _rawValue;
            set { _rawValue = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RawValue))); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
    
    public static class PmbusStatusDecoder
    {
        public static string DecodeStatusWord(ushort status)
        {
            if (status == 0) return "״̬������No faults or warnings detected.";
            var sb = new StringBuilder();
            if ((status & 0x8000) != 0) sb.AppendLine("Bit 15: VOUT Fault");
            if ((status & 0x4000) != 0) sb.AppendLine("Bit 14: IOUT Fault or IOUT_OC_FAULT");
            if ((status & 0x2000) != 0) sb.AppendLine("Bit 13: VIN Fault or VIN_UV_FAULT");
            if ((status & 0x1000) != 0) sb.AppendLine("Bit 12: MFR_SPECIFIC Fault");
            if ((status & 0x0800) != 0) sb.AppendLine("Bit 11: POWER_GOOD# is negated");
            if ((status & 0x0400) != 0) sb.AppendLine("Bit 10: FANS Fault");
            if ((status & 0x0200) != 0) sb.AppendLine("Bit 9: OTHER Fault");
            if ((status & 0x0100) != 0) sb.AppendLine("Bit 8: Unknown Fault");
            if ((status & 0x0080) != 0) sb.AppendLine("Bit 7: VOUT Overvoltage Fault");
            if ((status & 0x0040) != 0) sb.AppendLine("Bit 6: IOUT Overcurrent Fault");
            if ((status & 0x0020) != 0) sb.AppendLine("Bit 5: VIN Undervoltage Fault");
            if ((status & 0x0010) != 0) sb.AppendLine("Bit 4: Temperature Fault or Warning");
            if ((status & 0x0008) != 0) sb.AppendLine("Bit 3: CML (Comm, Mem, Logic) Fault");
            if ((status & 0x0004) != 0) sb.AppendLine("Bit 2: Other Memory or Logic Fault");
            if ((status & 0x0002) != 0) sb.AppendLine("Bit 1: Busy");
            if ((status & 0x0001) != 0) sb.AppendLine("Bit 0: A specific OFF condition");
            return sb.ToString();
        }
    }
}
