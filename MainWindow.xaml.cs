using RZDTrainer.Models;
using RZDTrainer.Services;
using RZDTrainer.Views;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace RZDTrainer
{
    public partial class MainWindow : Window
    {
        private SpeechRecognizer _recognizer;
        private VoiceCommandEngine _voiceCommandEngine;
        private TrainerEngine _trainerEngine;
        private bool _isRecording = false;
        private VoskCorrector _voskCorrector;

        private readonly string _settingsPath = "app_settings.json";

        public MainWindow()
        {
            InitializeComponent();
            _voskCorrector = new VoskCorrector();

            InitializeServices();
            InitializeMicrophone();
            LoadFirstScenario();
        }

        private void InitializeServices()
        {
            try
            {
                Debug.WriteLine("=== InitializeServices START ===");

                var commandCatalog = VoiceCommandCatalog.LoadFromJson("voice_commands.json");
                _voiceCommandEngine = new VoiceCommandEngine(commandCatalog);
                _voiceCommandEngine.OnCommandExecuted += OnCommandExecuted;
                _voiceCommandEngine.OnNeedsConfirmation += OnNeedsConfirmation;
                _voiceCommandEngine.OnCommandCancelled += OnCommandCancelled;

                var scenarioLoader = new ScenarioLoader();
                scenarioLoader.Load("scenarios.json");
                var neuralEvaluator = new PythonNeuralEvaluator();
                _trainerEngine = new TrainerEngine(neuralEvaluator, scenarioLoader);
                _trainerEngine.OnScenarioLoaded += OnScenarioLoaded;
                _trainerEngine.OnEvaluationComplete += OnEvaluationComplete;

                Debug.WriteLine("=== InitializeServices SUCCESS ===");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"InitializeServices ERROR: {ex.Message}");
                MessageBox.Show($"Ошибка инициализации: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InitializeMicrophone()
        {
            try
            {
                string modelPath = "vosk-model-small-ru-0.22";
                string grammarPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "grammar.json");

                if (!Directory.Exists(modelPath))
                {
                    MicStatusText.Text = "⚠️ Модель Vosk не найдена";
                    MicrophoneButton.IsEnabled = false;
                    return;
                }

                var devices = SpeechRecognizer.GetAudioDevices();
                if (devices.Count == 0)
                {
                    MicStatusText.Text = "⚠️ Микрофон не найден";
                    MicrophoneButton.IsEnabled = false;
                    return;
                }

                _recognizer = new SpeechRecognizer();
                string grammarFile = File.Exists(grammarPath) ? grammarPath : null;
                _recognizer.Initialize(modelPath, 0, grammarFile);

                _recognizer.OnPartialResult += OnPartialResult;
                _recognizer.OnCompletePhrase += OnCompletePhrase;

                MicStatusText.Text = $"🎤 {devices[0].Name} (с грамматикой)";
                MicrophoneButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                MicStatusText.Text = "⚠️ Ошибка микрофона";
                MicrophoneButton.IsEnabled = false;
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private AppSettings LoadSettings()
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            return new AppSettings();
        }

        private void SaveSettings(AppSettings settings)
        {
            var json = JsonSerializer.Serialize(settings);
            File.WriteAllText(_settingsPath, json);
        }

        private void ShowDeviceSelector()
        {
            var selector = new DeviceSelectorWindow();
            selector.Owner = this;

            if (selector.ShowDialog() == true)
            {
                var deviceNumber = selector.SelectedDeviceNumber;
                var deviceName = selector.SelectedDeviceName;

                var settings = LoadSettings();
                settings.MicrophoneDevice = deviceNumber;
                settings.MicrophoneName = deviceName;
                SaveSettings(settings);

                _recognizer?.Dispose();
                _recognizer = new SpeechRecognizer();
                _recognizer.Initialize("vosk-model-small-ru-0.22", deviceNumber);
                _recognizer.OnPartialResult += OnPartialResult;
                _recognizer.OnCompletePhrase += OnCompletePhrase;

                MicStatusText.Text = $"🎤 {deviceName}";
                MicrophoneButton.IsEnabled = true;
            }
        }

        private void LoadFirstScenario()
        {
            _trainerEngine?.LoadScenario(0);
        }

        private void OnScenarioLoaded(Scenario scenario)
        {
            Dispatcher.Invoke(() =>
            {
                TitleText.Text = $"[{scenario.Id}] {scenario.Title}";
                ScenarioText.Text = scenario.Context;
                ResultPopup.IsOpen = false;
                LiveText.Text = "Нажмите и удерживайте кнопку микрофона...";
                StatusText.Text = "Готов к работе";
            });
        }

        private void OnPartialResult(string partial)
        {
            var corrected = partial.ToLower()
                .Replace("иванав", "иванов")
                .Replace("ивановы", "иванов")
                .Replace("вагоновов", "вагонов")
                .Replace("вагонавов", "вагонов")
                .Replace("вагонав", "вагонов")
                .Replace("вагонава", "вагонов")
                .Replace("вагонавы", "вагонов")
                .Replace("вагон", "вагонов")
                .Replace("вагона", "вагонов");

            Dispatcher.Invoke(() =>
            {
                LiveText.Text = corrected;
                LiveText.Foreground = Brushes.Black;
            });
        }
        private async void OnCompletePhrase(string phrase)
        {
            Debug.WriteLine($"=== ОРИГИНАЛ: '{phrase}'");

            var corrected = phrase.ToLower();

            // Сначала заменяем длинные (чтобы не испортить короткие)
            var replacements = new Dictionary<string, string>
    {
        // Длинные фразы
        { "вагонова", "вагонов" },
        { "вагоновов", "вагонов" },
        { "вагонавов", "вагонов" },
        { "вагонава", "вагонов" },
        { "вагонавы", "вагонов" },
        { "ваганов", "вагонов" },
        { "вагонав", "вагонов" },
        { "вагонв", "вагонов" },
        { "составителей", "составитель" },
        { "составители", "составитель" },
        { "составителья", "составитель" },
        { "дежурные", "дежурный" },
        { "выбранные", "дежурный" },
        { "иванав", "иванов" },
        { "иванова", "иванов" },
        { "воров", "иванов" },
        { "передвинуть", "переставьте" },
        
        // Короткие (только целые слова)
        { "вагона", "вагонов" },
        { "вагоны", "вагонов" },
        { "шесть", "6" },
        { "шестой", "6" },
        { "шестого", "6" },
        { "десять", "10" },
        { "десятого", "10" },
        { "путина", "путь" },
        { "пути", "путь" },
        { "но", "на" },
        { "же", "на" },
        { "по", "на" },
        { "первая", "первый" },
        { "готова", "готов" },
        { "готовы", "готов" },
    };

            // Применяем замены
            foreach (var rep in replacements)
            {
                corrected = corrected.Replace(rep.Key, rep.Value);
            }

            // Дополнительно: исправляем "вагонав вагонав" в "вагонов вагонов"
            corrected = corrected.Replace("вагонав", "вагонов");

            // Чистим пробелы
            while (corrected.Contains("  "))
                corrected = corrected.Replace("  ", " ");

            Debug.WriteLine($"=== ИСПРАВЛЕНО: '{corrected}'");

            Dispatcher.Invoke(() => LiveText.Text = corrected);

            if (string.IsNullOrWhiteSpace(corrected)) return;

            var decision = _voiceCommandEngine.Process(corrected);

            if (decision.IsNone)
            {
                await _trainerEngine.EvaluateUserPhrase(corrected);
            }
        }

        private void OnCommandExecuted(string commandName)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = GetCommandMessage(commandName);
                ResultPopup.IsOpen = false;
            });
        }

        private void OnNeedsConfirmation(string commandName, string phrase)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = $"⚠️ Подтвердите: {commandName}? (скажите 'да' или 'подтверждаю')";
                StatusText.Foreground = Brushes.Orange;
                MicrophoneButton.Background = Brushes.Orange;
            });
        }

        private void OnCommandCancelled(string commandName)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = $"❌ Отменено: {commandName}";
                StatusText.Foreground = Brushes.Gray;
                MicrophoneButton.Background = Brushes.Red;
            });
        }

        private void OnEvaluationComplete(EvaluationResult result, string userPhrase)
        {
            Debug.WriteLine($"=== OnEvaluationComplete: Score={result?.Score}, HasError={result?.HasError} ===");

            Dispatcher.Invoke(() =>
            {
                ShowResultPopup(result, userPhrase);
            });
        }

        private void ShowResultPopup(EvaluationResult result, string userPhrase)
        {
            Debug.WriteLine($"ShowResultPopup: Score={result.Score}, IsCorrect={result.IsCorrect}");
            if (result.Score < 30 || string.IsNullOrEmpty(userPhrase) || userPhrase.Length < 10)
            {
                result.Score = 0;
            }
            if (result.HasError)
            {
                VerdictText.Text = "❌ ОШИБКА ОЦЕНКИ";
                VerdictText.Foreground = Brushes.Red;
                ExtraInfoText.Text = result.ErrorMessage;
            }
            else if (result.IsCorrect)
            {
                VerdictText.Text = $"✅ ПРАВИЛЬНО! {result.Score}%";
                VerdictText.Foreground = Brushes.Green;
            }
            else
            {
                VerdictText.Text = $"❌ ОШИБКА {result.Score}%";
                VerdictText.Foreground = Brushes.Red;
            }
            
            

            ScoreProgressBar.Value = result.Score;
            ScoreText.Text = $"{result.Score}%";
            UserPhraseText.Text = userPhrase;
            CanonicalText.Text = result.Canonical;
            DirectMatchText.Text = $"{result.DirectMatch}%";
            KeywordMatchText.Text = $"{result.KeywordMatch}%";
            NeuralMatchText.Text = result.NeuralUsed ? $"{result.NeuralMatch}%" : "не использована";

            if (result.FoundAcceptable)
                ExtraInfoText.Text = "✓ Фраза найдена в списке допустимых альтернатив";
            else if (result.IsWrong)
                ExtraInfoText.Text = "⚠️ Обнаружена ошибочная фраза из черного списка";
            else
                ExtraInfoText.Text = "";

            ResultPopup.IsOpen = true;
        }

        private string GetCommandMessage(string commandName)
        {
            return commandName switch
            {
                "EMERGENCY_STOP" => "🚨 АВАРИЙНАЯ ОСТАНОВКА! Маневры прекращены.",
                "Начать маневры" => "🚂 Маневры начаты. Следуйте регламенту.",
                "Завершить маневры" => "🏁 Маневры завершены. Доложите ДСП.",
                "Сцепка" => "🔗 Выполняется сцепка вагонов.",
                "Расцепка" => "🔓 Выполняется расцепка вагонов.",
                "Команда ДСП: закрепить состав" => "📢 Команда ДСП на закрепление принята.",
                "Доклад: закрепление выполнено" => "📢 Докладываю ДСП: закрепление выполнено.",
                "Доложить готовность" => "📢 Докладываю ДСП: к маневрам готов.",
                "ДСП: Верно, выполняйте!" => "✅ ДСП подтвердил: 'Верно, выполняйте!'",
                "Открыть журнал" => "📓 Открываю журнал маневровых операций.",
                _ => $"✅ {commandName}"
            };
        }

        // ============ КНОПКА МИКРОФОНА ============

        private void MicrophoneButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_recognizer == null || !_recognizer.IsAvailable) return;

            _isRecording = true;
            MicrophoneButton.Background = Brushes.DarkRed;
            MicIcon.Text = "🎙️";
            MicButtonText.Text = "ГОВОРИТЕ";
            RecordingIndicator.Visibility = Visibility.Visible;
            LiveText.Text = "";
            _recognizer.StartRecording();
        }

        private void MicrophoneButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isRecording) return;

            _isRecording = false;
            MicrophoneButton.Background = Brushes.Red;
            MicIcon.Text = "🎤";
            MicButtonText.Text = "ЗАЖМИТЕ";
            RecordingIndicator.Visibility = Visibility.Collapsed;
            _recognizer.StopRecordingAndFinalize();
        }

        // ============ КНОПКИ НАВИГАЦИИ ============

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            _trainerEngine?.LoadNextScenario();
            ResultPopup.IsOpen = false;
        }

        private void RepeatButton_Click(object sender, RoutedEventArgs e)
        {
            _trainerEngine?.ResetCurrentScenario();
            ResultPopup.IsOpen = false;
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            ShowDeviceSelector();
        }

        private void SelectScenarioButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var scenarioLoader = new ScenarioLoader();
                scenarioLoader.Load("scenarios.json");
                var allScenarios = scenarioLoader.GetAllScenarios();
                var currentIndex = scenarioLoader.CurrentIndex;

                var selector = new ScenarioSelectorWindow(allScenarios, currentIndex);
                selector.Owner = this;

                if (selector.ShowDialog() == true)
                {
                    var selectedIndex = selector.SelectedScenarioIndex;
                    if (selectedIndex >= 0 && selectedIndex < allScenarios.Count)
                    {
                        _trainerEngine.LoadScenario(selectedIndex);
                        ResultPopup.IsOpen = false;
                        ShowStatusMessage($"✅ Выбран сценарий: {allScenarios[selectedIndex].Title}");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка открытия списка сценариев: {ex.Message}", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowStatusMessage(string message)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = message;
                StatusText.Foreground = Brushes.Black;

                var timer = new System.Timers.Timer(3000);
                timer.Elapsed += (s, args) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (StatusText.Text == message)
                            StatusText.Text = "Готов к работе";
                    });
                    timer.Stop();
                    timer.Dispose();
                };
                timer.Start();
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            _recognizer?.Dispose();
            base.OnClosed(e);
        }
    }

    // Класс для настроек
    public class AppSettings
    {
        public int MicrophoneDevice { get; set; } = -1;
        public string MicrophoneName { get; set; } = "";
    }
}