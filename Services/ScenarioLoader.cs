using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using RZDTrainer.Models;

namespace RZDTrainer.Services
{
    public sealed class ScenarioLoader
    {
        private List<Scenario> _scenarios = new();
        private int _currentIndex = 0;

        public int CurrentIndex => _currentIndex;
        public int TotalScenarios => _scenarios.Count;
        public void Load(string filePath)
        {
            try
            {
                // Полный путь к файлу
                string fullPath = Path.GetFullPath(filePath);

                System.Diagnostics.Debug.WriteLine($"=== Загрузка сценариев ===");
                System.Diagnostics.Debug.WriteLine($"Путь: {fullPath}");
                System.Diagnostics.Debug.WriteLine($"Файл существует: {File.Exists(fullPath)}");

                if (!File.Exists(fullPath))
                {
                    throw new FileNotFoundException($"Файл сценариев не найден: {fullPath}\nТекущая папка: {Directory.GetCurrentDirectory()}");
                }

                string json = File.ReadAllText(fullPath, System.Text.Encoding.UTF8);
                System.Diagnostics.Debug.WriteLine($"Длина JSON: {json.Length} символов");
                System.Diagnostics.Debug.WriteLine($"Первые 100 символов: {json.Substring(0, Math.Min(100, json.Length))}");

                // Настройки десериализации
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };

                var wrapper = JsonSerializer.Deserialize<ScenarioWrapper>(json, options);

                if (wrapper?.Scenarios != null && wrapper.Scenarios.Count > 0)
                {
                    _scenarios = wrapper.Scenarios;
                    System.Diagnostics.Debug.WriteLine($"✅ Загружено {_scenarios.Count} сценариев из {fullPath}");

                    // Выводим ID сценариев
                    foreach (var s in _scenarios)
                    {
                        System.Diagnostics.Debug.WriteLine($"  - {s.Id}: {s.Title}");
                    }
                }
                else
                {
                    throw new Exception("Не удалось загрузить сценарии из JSON");
                }
            }
            catch (JsonException ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ JSON ошибка: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Path: {ex.Path}");
                System.Diagnostics.Debug.WriteLine($"Line: {ex.LineNumber}, Position: {ex.BytePositionInLine}");
                throw new Exception($"Ошибка парсинга JSON: {ex.Message}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Ошибка загрузки сценариев: {ex.Message}");
                throw;
            }
        }

        public Scenario GetScenario(int index)
        {
            if (index < 0 || index >= _scenarios.Count)
                throw new IndexOutOfRangeException($"Сценарий {index} не найден. Всего сценариев: {_scenarios.Count}");

            _currentIndex = index;
            return _scenarios[index];
        }

        public List<Scenario> GetAllScenarios()
        {
            return _scenarios;
        }

        public Scenario GetCurrentScenario()
        {
            return _scenarios[_currentIndex];
        }

        private class ScenarioWrapper
        {
            public string? Role { get; set; }
            public List<Scenario>? Scenarios { get; set; }
        }
    }
}