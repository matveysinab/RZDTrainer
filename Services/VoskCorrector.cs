using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace RZDTrainer.Services
{
    public class VoskCorrector
    {
        private readonly Dictionary<string, string> _replacements = new Dictionary<string, string>
        {
            // ============ ИМЕНА (самые важные, должны быть первыми) ============
            { "иванов", "иванов" },  // оставляем как есть
            { "иванав", "иванов" },
            { "иванова", "иванов" },
            { "воров", "иванов" },
            
            // ============ ВАГОНЫ ============
            { "вагонавов", "вагонов" },
            { "вагонава", "вагонов" },
            { "вагонавы", "вагонов" },
            { "ваганов", "вагонов" },
            { "вагоновов", "вагонов" },
            { "вагона", "вагонов" },
            { "вагон", "вагонов" },
            { "вагоны", "вагонов" },
            
            // ============ ДОЛЖНОСТИ ============
            { "составителей", "составитель" },
            { "составители", "составитель" },
            { "составителья", "составитель" },
            { "составителя", "составитель" },
            { "дежурные", "дежурный" },
            { "выбранные", "дежурный" },
            
            // ============ ЧИСЛА ============
            { "шесть", "6" },
            { "шестой", "6" },
            { "шестого", "6" },
            { "первая", "1" },
            { "первый", "1" },
            { "первого", "1" },
            { "первой", "1" },
            { "четвертый", "4" },
            { "четвертого", "4" },
            { "четвёртый", "4" },
            { "четвёртого", "4" },
            { "пятый", "5" },
            { "пятого", "5" },
            { "десятого", "10" },
            { "десять", "10" },
            
            // ============ ПУТЬ ============
            { "путина", "путь" },
            { "пути", "путь" },
            
            // ============ ПРЕДЛОГИ ============
            { " с ", " с " },  // оставляем
            { " на ", " на " },  // оставляем
        };

        // Отдельные замены для коротких слов (чтобы не портить другие)
        private readonly Dictionary<string, string> _wordReplacements = new Dictionary<string, string>
        {
            { "но", "на" },
            { "же", "на" },
            { "по", "на" },
            { "готова", "готов" },
            { "готовы", "готов" },
            { "готово", "готов" },
        };

        public string Correct(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            Debug.WriteLine($"=== VoskCorrector ВХОД: '{text}'");

            var result = text.ToLower();

            // 1. Сначала заменяем длинные фразы и имена
            foreach (var kvp in _replacements)
            {
                result = result.Replace(kvp.Key, kvp.Value);
            }

            // 2. Затем заменяем отдельные слова (с границами)
            foreach (var kvp in _wordReplacements)
            {
                result = Regex.Replace(result, $@"\b{Regex.Escape(kvp.Key)}\b", kvp.Value);
            }

            // Убираем лишние пробелы
            result = Regex.Replace(result, @"\s+", " ");

            Debug.WriteLine($"=== VoskCorrector ВЫХОД: '{result}'");

            return result.Trim();
        }
    }
}