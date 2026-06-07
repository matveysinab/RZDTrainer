using NAudio.Wave;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Vosk;

namespace RZDTrainer.Services
{
    public class SpeechRecognizer : IDisposable
    {
        private Model? _model;
        private VoskRecognizer? _recognizer;
        private WaveInEvent? _waveIn;
        private bool _isRecording = false;
        private string _lastPartial = "";
        private readonly object _lock = new object();

        public event Action<string>? OnPartialResult;
        public event Action<string>? OnCompletePhrase;

        public bool IsAvailable => _model != null && _waveIn != null;

        public void Initialize(string modelPath, int deviceNumber = 0, string? grammarJson = null)
        {
            try
            {
                lock (_lock)
                {
                    _model?.Dispose();
                    _recognizer?.Dispose();
                    _waveIn?.Dispose();

                    string fullModelPath = Path.GetFullPath(modelPath);
                    if (!Directory.Exists(fullModelPath))
                    {
                        throw new DirectoryNotFoundException($"Модель Vosk не найдена: {fullModelPath}");
                    }

                    _model = new Model(fullModelPath);
                    _recognizer = CreateRecognizer(_model, grammarJson);
                    _recognizer.SetMaxAlternatives(0);
                    _recognizer.SetWords(false);

                    _waveIn = new WaveInEvent();
                    _waveIn.DeviceNumber = deviceNumber;
                    _waveIn.WaveFormat = new WaveFormat(16000, 1);
                    _waveIn.DataAvailable += OnDataAvailable;
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка инициализации Vosk: {ex.Message}", ex);
            }
        }

        // Создаёт распознаватель. Если передана грамматика — ограничивает распознавание
        // лексикой сценариев. При любой ошибке грамматики тихо откатываемся к полной модели,
        // чтобы программа никогда не падала из-за словаря.
        private static VoskRecognizer CreateRecognizer(Model model, string? grammarJson)
        {
            if (!string.IsNullOrWhiteSpace(grammarJson))
            {
                try
                {
                    var rec = new VoskRecognizer(model, 16000.0f, grammarJson);
                    Debug.WriteLine("Vosk: грамматика применена (распознавание ограничено лексикой сценариев)");
                    return rec;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Vosk: не удалось применить грамматику ({ex.Message}); используется полная модель");
                }
            }
            return new VoskRecognizer(model, 16000.0f);
        }

        public void StartRecording()
        {
            lock (_lock)
            {
                if (_waveIn == null || _recognizer == null) return;

                _recognizer.Reset();
                _waveIn.StartRecording();
                _isRecording = true;
                _lastPartial = "";
                Debug.WriteLine("Recording started");
            }
        }

        public void StopRecordingAndFinalize()
        {
            lock (_lock)
            {
                if (_waveIn == null || !_isRecording) return;

                _waveIn.StopRecording();
                _isRecording = false;
                Debug.WriteLine("Recording stopped");

                // Получаем финальный результат
                if (_recognizer != null)
                {
                    string finalResult = _recognizer.FinalResult();
                    Debug.WriteLine($"Final result raw: {finalResult}");

                    var text = ExtractTextFromJson(finalResult);
                    Debug.WriteLine($"Extracted text: '{text}'");

                    // Если финальный результат пустой, используем последний partial
                    if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(_lastPartial))
                    {
                        text = _lastPartial;
                        Debug.WriteLine($"Using last partial: '{text}'");
                    }

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        OnCompletePhrase?.Invoke(text);
                    }
                    else
                    {
                        Debug.WriteLine("WARNING: No text recognized!");
                    }
                }
            }
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (!_isRecording) return;

            lock (_lock)
            {
                if (_recognizer == null) return;

                if (_recognizer.AcceptWaveform(e.Buffer, e.BytesRecorded))
                {
                    var result = _recognizer.Result();
                    var text = ExtractTextFromJson(result);
                    if (!string.IsNullOrEmpty(text))
                    {
                        Debug.WriteLine($"Auto-complete: {text}");
                        _lastPartial = text;
                        // Не вызываем OnCompletePhrase здесь
                    }
                }
                else
                {
                    var partial = _recognizer.PartialResult();
                    var partialText = ExtractPartialTextFromJson(partial);
                    if (!string.IsNullOrEmpty(partialText))
                    {
                        _lastPartial = partialText;
                        OnPartialResult?.Invoke(partialText);
                    }
                }
            }
        }

        private string ExtractTextFromJson(string json)
        {
            try
            {
                if (string.IsNullOrEmpty(json)) return "";
                var result = JsonConvert.DeserializeObject<VoskResult>(json);
                return result?.Text ?? "";
            }
            catch { return ""; }
        }

        private string ExtractPartialTextFromJson(string json)
        {
            try
            {
                if (string.IsNullOrEmpty(json)) return "";
                var result = JsonConvert.DeserializeObject<VoskPartialResult>(json);
                return result?.Partial ?? "";
            }
            catch { return ""; }
        }

        public static List<AudioDeviceInfo> GetAudioDevices()
        {
            var devices = new List<AudioDeviceInfo>();
            for (int i = 0; i < WaveInEvent.DeviceCount; i++)
            {
                var caps = WaveInEvent.GetCapabilities(i);
                devices.Add(new AudioDeviceInfo(i, caps.ProductName));
            }
            return devices;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _waveIn?.StopRecording();
                _waveIn?.Dispose();
                _recognizer?.Dispose();
                _model?.Dispose();
            }
        }

        private class VoskResult { public string? Text { get; set; } }
        private class VoskPartialResult { public string? Partial { get; set; } }
    }

    public class AudioDeviceInfo
    {
        public int Number { get; set; }
        public string Name { get; set; } = "";
        public AudioDeviceInfo(int number, string name) => (Number, Name) = (number, name);
    }
}