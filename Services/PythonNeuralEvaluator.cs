using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;
using System.Text;
using RZDTrainer.Models;

namespace RZDTrainer.Services
{
    public sealed class PythonNeuralEvaluator
    {
        private readonly string _pythonPath;
        private readonly string _scriptPath;

        public PythonNeuralEvaluator()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // 1. Сначала ищем встроенный Python (портативная версия)
            string embeddedPython = Path.Combine(baseDir, "python_runtime", "python.exe");

            // 2. Если нет, ищем системный (твой путь)
            //string systemPython = @"C:\Users\Matvey\AppData\Local\Python\pythoncore-3.14-64\python.exe";

            // 3. Если и его нет, пробуем просто "python"
            //string fallbackPython = "python";

            if (File.Exists(embeddedPython))
            {
                _pythonPath = embeddedPython;
                Debug.WriteLine($"✅ Использую ВСТРОЕННЫЙ Python: {_pythonPath}");
            }
            //else if (File.Exists(systemPython))
            //{
            //    _pythonPath = systemPython;
            //    Debug.WriteLine($"✅ Использую СИСТЕМНЫЙ Python: {_pythonPath}");
            //}
            //else
            //{
            //    _pythonPath = fallbackPython;
            //    Debug.WriteLine($"⚠️ Использую PYTHON ИЗ PATH: {_pythonPath}");
            //}

            _scriptPath = Path.Combine(baseDir, "python", "evaluator.py");

            Debug.WriteLine($"Script path: {_scriptPath}");
            Debug.WriteLine($"Script exists: {File.Exists(_scriptPath)}");
        }

        public async Task<EvaluationResult> Evaluate(string phrase, string scenarioId)
        {
            try
            {
                Debug.WriteLine($"=== PYTHON EVALUATE ===");
                Debug.WriteLine($"Phrase: {phrase}");

                if (!File.Exists(_scriptPath))
                {
                    return EvaluationResult.Error($"Скрипт не найден: {_scriptPath}");
                }

                var input = new { phrase = phrase, scenario_id = scenarioId };
                var inputJson = JsonSerializer.Serialize(input);

                var tempFile = Path.GetTempFileName();
                await File.WriteAllTextAsync(tempFile, inputJson, new UTF8Encoding(false));
                Debug.WriteLine($"Temp file: {tempFile}");

                var startInfo = new ProcessStartInfo
                {
                    FileName = _pythonPath,
                    Arguments = $"\"{_scriptPath}\" \"{tempFile}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using var process = new Process();
                process.StartInfo = startInfo;
                process.Start();

                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                Debug.WriteLine($"Python output: {output}");
                Debug.WriteLine($"Python error: {error}");

                try { File.Delete(tempFile); } catch { }

                if (string.IsNullOrWhiteSpace(output))
                {
                    return EvaluationResult.Error($"Нет вывода. stderr: {error}");
                }

                int startIdx = output.IndexOf('{');
                int endIdx = output.LastIndexOf('}');
                if (startIdx >= 0 && endIdx > startIdx)
                {
                    string json = output.Substring(startIdx, endIdx - startIdx + 1);
                    var result = JsonSerializer.Deserialize<EvaluationResult>(json);
                    if (result != null)
                    {
                        Debug.WriteLine($"Score: {result.Score}, NeuralUsed: {result.NeuralUsed}");
                        return result;
                    }
                }

                return EvaluationResult.Error($"Не найден JSON: {output}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception: {ex.Message}");
                return EvaluationResult.Error($"Ошибка: {ex.Message}");
            }
        }
    }
}