using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using RZDTrainer.Models;

namespace RZDTrainer.Services
{
    /// <summary>
    /// Работа с лексикой сценариев.
    ///
    /// • GetVocabulary — набор слов из сценариев и команд. Используется для безопасной
    ///   «привязки» распознанного текста к лексике (в VoskCorrector) — это включено
    ///   по умолчанию и не мешает распознаванию.
    ///
    /// • Build — строит ЖЁСТКУЮ грамматику Vosk (распознаются только эти слова). Это
    ///   опция: на маленькой модели она может сильно резать распознавание, поэтому
    ///   по умолчанию выключена (см. AppSettings.UseVoskGrammar).
    /// </summary>
    public static class VoskGrammarBuilder
    {
        private static readonly string[] Connectors =
        {
            "с", "со", "на", "за", "и", "к", "ко", "по", "до", "из",
            "в", "во", "о", "об", "у", "не", "а", "от", "при", "под"
        };

        private static readonly string[] Numbers =
        {
            "ноль", "один", "одна", "одно",
            "два", "две", "три", "четыре", "пять", "шесть", "семь", "восемь", "девять",
            "десять", "одиннадцать", "двенадцать", "тринадцать", "четырнадцать",
            "пятнадцать", "шестнадцать", "семнадцать", "восемнадцать", "девятнадцать",
            "двадцать", "тридцать", "сорок", "пятьдесят", "шестьдесят", "семьдесят",
            "восемьдесят", "девяносто", "сто", "двести", "триста", "четыреста", "пятьсот",
            "первый", "первого", "первая", "первой", "первом",
            "второй", "второго", "третий", "третьего", "третьем",
            "четвёртый", "четвертый", "четвёртого", "четвертого",
            "пятый", "пятого", "шестой", "шестого", "седьмой", "седьмого",
            "восьмой", "восьмого", "девятый", "девятого", "десятый", "десятого"
        };

        private static readonly string[] Extra = { "эм", "тэ", "эс", "утс" };

        /// <summary>Слова из сценариев и команд (для привязки результата к лексике).</summary>
        public static HashSet<string> GetVocabulary(IEnumerable<Scenario> scenarios,
                                                    string voiceCommandsPath = "voice_commands.json")
        {
            return CollectScenarioWords(scenarios, voiceCommandsPath);
        }

        /// <summary>
        /// Жёсткая грамматика Vosk в виде JSON ["слово1","слово2",...,"[unk]"].
        /// null — если собрать нечего.
        /// </summary>
        public static string? Build(IEnumerable<Scenario> scenarios,
                                    string voiceCommandsPath = "voice_commands.json")
        {
            var words = CollectScenarioWords(scenarios, voiceCommandsPath);

            foreach (var w in Connectors) words.Add(w);
            foreach (var w in Numbers) words.Add(w);
            foreach (var w in Extra) words.Add(w);

            if (words.Count == 0)
                return null;

            // е/ё — разные модели пишут по‑разному
            foreach (var w in words.ToList())
                if (w.Contains('ё')) words.Add(w.Replace('ё', 'е'));

            var list = words.OrderBy(w => w, StringComparer.Ordinal).ToList();
            list.Add("[unk]");

            // ВАЖНО: пишем кириллицу как есть (UTF‑8), без \uXXXX — иначе Vosk
            // не сопоставит слова со своим словарём.
            var options = new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var json = JsonSerializer.Serialize(list, options);
            Debug.WriteLine($"=== Грамматика Vosk: {list.Count - 1} слов ===");
            return json;
        }

        private static HashSet<string> CollectScenarioWords(IEnumerable<Scenario> scenarios, string voiceCommandsPath)
        {
            var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (scenarios != null)
            {
                foreach (var s in scenarios)
                {
                    AddWords(words, s.Canonical);
                    AddWords(words, s.Keywords);
                    AddWords(words, s.AcceptableVariants);
                    AddWords(words, s.WrongVariants);
                }
            }

            TryAddVoiceCommandWords(words, voiceCommandsPath);
            return words;
        }

        private static void AddWords(HashSet<string> set, IEnumerable<string>? phrases)
        {
            if (phrases == null) return;
            foreach (var p in phrases) AddWords(set, p);
        }

        private static void AddWords(HashSet<string> set, string? phrase)
        {
            if (string.IsNullOrWhiteSpace(phrase)) return;

            var cleaned = Regex.Replace(phrase.ToLowerInvariant(), @"[^а-яё]+", " ");
            foreach (var token in cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (token.Length >= 2)
                    set.Add(token);
        }

        private static void TryAddVoiceCommandWords(HashSet<string> set, string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;

                void AddArray(JsonElement arr)
                {
                    foreach (var el in arr.EnumerateArray())
                        if (el.ValueKind == JsonValueKind.String)
                            AddWords(set, el.GetString());
                }

                void AddPhraseObjects(JsonElement arr)
                {
                    foreach (var item in arr.EnumerateArray())
                        if (item.TryGetProperty("phrases", out var ph) && ph.ValueKind == JsonValueKind.Array)
                            AddArray(ph);
                }

                if (root.TryGetProperty("critical", out var crit) && crit.ValueKind == JsonValueKind.Array)
                    AddArray(crit);
                if (root.TryGetProperty("confirm", out var conf) && conf.ValueKind == JsonValueKind.Array)
                    AddPhraseObjects(conf);
                if (root.TryGetProperty("info", out var info) && info.ValueKind == JsonValueKind.Array)
                    AddPhraseObjects(info);
                if (root.TryGetProperty("system", out var sys) && sys.ValueKind == JsonValueKind.Object)
                {
                    if (sys.TryGetProperty("confirm", out var sc) && sc.ValueKind == JsonValueKind.Array) AddArray(sc);
                    if (sys.TryGetProperty("cancel", out var sx) && sx.ValueKind == JsonValueKind.Array) AddArray(sx);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"VoskGrammarBuilder: не удалось прочитать {path}: {ex.Message}");
            }
        }
    }
}
