using System;
using System.Threading.Tasks;
using System.Windows;
using NAudio.Wave;
using RZDTrainer.Services;

namespace RZDTrainer.Views
{
    public partial class DeviceSelectorWindow : Window
    {
        private int _selectedDeviceNumber = -1;
        private string _selectedDeviceName = "";
        private AudioDeviceInfo? _selectedDevice; // Добавил поле для хранения выбранного устройства

        public int SelectedDeviceNumber => _selectedDeviceNumber;
        public string SelectedDeviceName => _selectedDeviceName;

        public DeviceSelectorWindow()
        {
            InitializeComponent();
            LoadDevices();
        }

        private void LoadDevices()
        {
            var devices = SpeechRecognizer.GetAudioDevices();
            DevicesListBox.ItemsSource = devices;

            if (devices.Count > 0)
            {
                DevicesListBox.SelectedIndex = 0;
            }
        }

        private async void TestButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = DevicesListBox.SelectedItem as AudioDeviceInfo;
            if (selected == null)
            {
                MessageBox.Show("Выберите устройство", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _selectedDevice = selected; // Сохраняем
            await TestMicrophone(selected.Number, selected.Name);
        }

        private async Task TestMicrophone(int deviceNumber, string deviceName)
        {
            TestButton.IsEnabled = false;
            TestButton.Content = "🎤 Слушаю...";

            using var recorder = new WaveInEvent();
            recorder.DeviceNumber = deviceNumber;
            recorder.WaveFormat = new WaveFormat(16000, 1);

            var maxLevel = 0f;
            var tcs = new TaskCompletionSource<bool>();

            recorder.DataAvailable += (s, args) =>
            {
                float level = 0;
                for (int i = 0; i < args.BytesRecorded; i += 2)
                {
                    short sample = BitConverter.ToInt16(args.Buffer, i);
                    level += Math.Abs(sample);
                }
                level = (level / (args.BytesRecorded / 2)) / 32768f;
                maxLevel = Math.Max(maxLevel, level);
            };

            recorder.RecordingStopped += (s, args) => tcs.TrySetResult(true);
            recorder.StartRecording();

            await Task.Delay(2000);
            recorder.StopRecording();
            await tcs.Task;

            TestButton.IsEnabled = true;
            TestButton.Content = "🔊 Тест";

            if (maxLevel < 0.01f)
            {
                MessageBox.Show($"Устройство \"{deviceName}\"\n\n❌ Сигнал не обнаружен. Проверьте микрофон.",
                    "Результат теста", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show($"Устройство \"{deviceName}\"\n\n✅ Микрофон работает!\nУровень сигнала: {maxLevel * 100:F0}%",
                    "Результат теста", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = DevicesListBox.SelectedItem as AudioDeviceInfo;
            if (selected == null)
            {
                MessageBox.Show("Выберите устройство", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _selectedDeviceNumber = selected.Number;
            _selectedDeviceName = selected.Name;

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}