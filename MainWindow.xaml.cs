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
using System.Windows.Media.Imaging;

namespace RZDTrainer
{
    public partial class MainWindow : Window
    {
        private SpeechRecognizer _recognizer;
        private VoiceCommandEngine _voiceCommandEngine;
        private TrainerEngine _trainerEngine;
        private bool _isRecording = false;
        private VoskCorrector _voskCorrector;
        private string? _grammarJson;
        private bool _useVoskGrammar = false; // жёсткая грамматика по умолчанию выключена

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

                // Лексика сценариев.
                try
                {
                    var all = scenarioLoader.GetAllScenarios();

                    // (1) Безопасная привязка результата к словам сценариев — включена всегда,
                    //     не мешает распознаванию.
                    _voskCorrector.SetVocabulary(VoskGrammarBuilder.GetVocabulary(all));

                    // (2) Жёсткая грамматика Vosk — строим заранее, но применяем ТОЛЬКО если
                    //     включено в настройках (на маленькой модели она режет распознавание).
                    _grammarJson = VoskGrammarBuilder.Build(all);
                    _useVoskGrammar = LoadSettings().UseVoskGrammar;
                }
                catch (Exception gex)
                {
                    Debug.WriteLine($"Лексика/грамматика: {gex.Message}");
                    _grammarJson = null;
                    _useVoskGrammar = false;
                }

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
                _recognizer.Initialize(modelPath, 0, _useVoskGrammar ? _grammarJson : null);

                _recognizer.OnPartialResult += OnPartialResult;
                _recognizer.OnCompletePhrase += OnCompletePhrase;

                MicStatusText.Text = $"🎤 {devices[0].Name}" + (_useVoskGrammar ? "  ·  грамматика" : "");
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
                _recognizer.Initialize("vosk-model-small-ru-0.22", deviceNumber, _useVoskGrammar ? _grammarJson : null);
                _recognizer.OnPartialResult += OnPartialResult;
                _recognizer.OnCompletePhrase += OnCompletePhrase;

                MicStatusText.Text = $"🎤 {deviceName}" + (_useVoskGrammar ? "  ·  грамматика" : "");
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

                // Сложность сценария
                DifficultyText.Text = scenario.DifficultyLabel;
                DifficultyBadge.Background = GetDifficultyBrush(scenario.DifficultyLabel);

                // Картинка сценария (если задана и файл существует)
                ApplyScenarioImage(scenario);

                ResultPopup.IsOpen = false;
                LiveText.Text = "Нажмите и удерживайте кнопку микрофона…";
                LiveText.Foreground = (Brush)new BrushConverter().ConvertFromString("#90A0B0");
                StatusText.Text = "Готов к работе";
            });
        }

        // Показывает картинку сценария или прячет блок, если картинки нет
        private void ApplyScenarioImage(Scenario scenario)
        {
            var path = scenario.ImageFullPath;
            if (string.IsNullOrEmpty(path))
            {
                ScenarioImage.Source = null;
                ImageCard.Visibility = Visibility.Collapsed;
                return;
            }

            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;          // не блокируем файл
                bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                ScenarioImage.Source = bmp;
                ImageCard.Visibility = Visibility.Visible;
            }
            catch
            {
                ScenarioImage.Source = null;
                ImageCard.Visibility = Visibility.Collapsed;
            }
        }

        // Цвет бейджа сложности
        private static Brush GetDifficultyBrush(string difficulty)
        {
            var d = (difficulty ?? "").Trim().ToLowerInvariant();
            string hex = d switch
            {
                "лёгкий" or "легкий" => "#2E7D32",
                "сложный" => "#C62828",
                "средний" => "#EF6C00",
                _ => "#607D8B"
            };
            return (Brush)new BrushConverter().ConvertFromString(hex);
        }

        private void OnPartialResult(string partial)
        {
            // Единый безопасный корректор (по целым словам) вместо подстрочных замен
            var corrected = _voskCorrector.Correct(partial);

            Dispatcher.Invoke(() =>
            {
                LiveText.Text = corrected;
                LiveText.Foreground = Brushes.Black;
            });
        }

        private async void OnCompletePhrase(string phrase)
        {
            Debug.WriteLine($"=== ОРИГИНАЛ: '{phrase}'");

            // Все исправления — в одном месте (VoskCorrector), строго по целым словам.
            var corrected = _voskCorrector.Correct(phrase);

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
                VerdictBanner.Background = (Brush)new BrushConverter().ConvertFromString("#FDECEA");
                ExtraInfoText.Text = result.ErrorMessage;
            }
            else if (result.IsCorrect)
            {
                VerdictText.Text = $"✅ ПРАВИЛЬНО! {result.Score}%";
                VerdictText.Foreground = (Brush)new BrushConverter().ConvertFromString("#1B5E20");
                VerdictBanner.Background = (Brush)new BrushConverter().ConvertFromString("#E8F5E9");
            }
            else
            {
                VerdictText.Text = $"❌ ОШИБКА {result.Score}%";
                VerdictText.Foreground = (Brush)new BrushConverter().ConvertFromString("#B71C1C");
                VerdictBanner.Background = (Brush)new BrushConverter().ConvertFromString("#FDECEA");
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
            MicButtonText.Text = "ЗАЖМИТЕ И ГОВОРИТЕ";
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

        // Жёсткая грамматика Vosk. По умолчанию ВЫКЛ — на маленькой модели она
        // сильно режет распознавание. Включается вручную: "UseVoskGrammar": true
        // в файле app_settings.json (рядом с программой).
        public bool UseVoskGrammar { get; set; } = false;
    }
}