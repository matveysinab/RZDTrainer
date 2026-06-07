using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace RZDTrainer.Services
{
    /// <summary>
    /// Исправляет типовые ошибки распознавания Vosk и приводит фразу к лексике сценариев.
    ///
    /// 1) Замены по ЦЕЛЫМ словам (через \b). Раньше использовался string.Replace по
    ///    подстроке, из‑за чего "иванов" превращался в "иванав" ("но"→"на"),
    ///    "поехали" → "наехали" ("по"→"на"), "вагонов" → "вагоновов". Теперь исключено.
    /// 2) Привязка к словарю сценариев: незнакомое слово заменяется ближайшим словом
    ///    из сценариев, если оно очень близко (1–2 буквы разницы). Это «подстройка
    ///    под лексику» на уровне результата — она не мешает распознаванию.
    /// </summary>
    public class VoskCorrector
    {
        // Пары (что найти -> на что заменить). Применяются по ЦЕЛЫМ словам.
        private static readonly List<KeyValuePair<string, string>> Map = new()
        {
            // ===== Фамилия (позывной составителя) =====
            new("иванав",  "иванов"),
            new("иванова", "иванов"),
            new("иваново", "иванов"),
            new("иваноф",  "иванов"),
            new("воров",   "иванов"),

            // ===== Вагоны =====
            new("вагонавов", "вагонов"),
            new("вагоновов", "вагонов"),
            new("вагонова",  "вагонов"),
            new("вагонава",  "вагонов"),
            new("вагонавы",  "вагонов"),
            new("вагонав",   "вагонов"),
            new("ваганов",   "вагонов"),
            new("вагоны",    "вагонов"),
            new("вагона",    "вагонов"),
            new("вагон",     "вагонов"),

            // ===== Должности / роли =====
            new("составителей", "составитель"),
            new("составители",  "составитель"),
            new("составителья", "составитель"),
            new("составителя",  "составитель"),
            new("состовитель",  "составитель"),
            new("дежурные",     "дежурный"),
            new("дежурной",     "дежурный"),
            new("машиниста",    "машинист"),
            new("машинисту",    "машинист"),

            // ===== Готовность =====
            new("готова", "готов"),
            new("готовы", "готов"),
            new("готово", "готов"),

            // ===== Предлог (безопасно, только как отдельное слово) =====
            new("но", "на"),

            // ===== Числа: слова -> цифры (в сценариях номера путей и количество
            //       вагонов записаны цифрами). Порядковые "первый/первого" НЕ трогаем —
            //       это позывной ("Иванов первого"). =====
            new("четырнадцать", "14"),
            new("тринадцать",   "13"),
            new("одиннадцать",  "11"),
            new("двенадцать",   "12"),
            new("пятнадцать",   "15"),
            new("четвёртого", "4"), new("четвертого", "4"),
            new("четвёртый",  "4"), new("четвертый",  "4"),
            new("четыре", "4"),
            new("десятого", "10"), new("десятый", "10"), new("десять", "10"),
            new("шестого", "6"), new("шестой", "6"), new("шесть", "6"),
            new("восьмого", "8"), new("восьмой", "8"), new("восемь", "8"),
            new("пятого", "5"), new("пятый", "5"), new("пять", "5"),
            new("девять", "9"),
            new("семь", "7"),
            new("три", "3"),
            new("два", "2"),
        };

        private static readonly List<(Regex rx, string to)> Rules =
            Map.OrderByDescending(p => p.Key.Length)
               .Select(p => (new Regex($@"\b{Regex.Escape(p.Key)}\b",
                                        RegexOptions.IgnoreCase | RegexOptions.Compiled), p.Value))
               .ToList();

        // Словарь сценариев для привязки незнакомых слов (необязательный)
        private HashSet<string> _vocab = new(StringComparer.Ordinal);

        /// <summary>Задаёт словарь сценариев для привязки результата к лексике.</summary>
        public void SetVocabulary(IEnumerable<string>? words)
        {
            _vocab = new HashSet<string>(StringComparer.Ordinal);
            if (words == null) return;
            foreach (var w in words)
                if (!string.IsNullOrWhiteSpace(w))
                    _vocab.Add(w.ToLowerInvariant());
        }

        public string Correct(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text ?? "";

            Debug.WriteLine($"=== VoskCorrector ВХОД: '{text}'");

            var result = text.ToLowerInvariant();

            // Убираем служебный токен распознавателя
            result = result.Replace("[unk]", " ");

            // 1) Замены по целым словам
            foreach (var (rx, to) in Rules)
                result = rx.Replace(result, to);

            // 2) Привязка незнакомых слов к словарю сценариев
            if (_vocab.Count > 0)
                result = SnapToVocabulary(result);

            // Чистим пробелы
            result = Regex.Replace(result, @"\s+", " ").Trim();

            Debug.WriteLine($"=== VoskCorrector ВЫХОД: '{result}'");
            return result;
        }

        // Заменяет каждое незнакомое кириллическое слово ближайшим словом из словаря,
        // но только если оно действительно близко и кандидат единственный.
        private string SnapToVocabulary(string text)
        {
            var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < tokens.Length; i++)
            {
                var tok = tokens[i];

                if (tok.Length < 4) continue;          // слишком короткое — не трогаем
                if (!IsCyrillic(tok)) continue;        // цифры, символы и т.п.
                if (_vocab.Contains(tok)) continue;    // уже корректное слово

                int maxDist = tok.Length <= 5 ? 1 : 2;
                string? best = null;
                int bestDist = int.MaxValue;
                bool tie = false;

                foreach (var cand in _vocab)
                {
                    if (Math.Abs(cand.Length - tok.Length) > maxDist) continue;
                    if (cand.Length == 0 || cand[0] != tok[0]) continue; // та же первая буква
                    int d = Levenshtein(tok, cand, maxDist);
                    if (d < 0) continue;
                    if (d < bestDist) { bestDist = d; best = cand; tie = false; }
                    else if (d == bestDist) tie = true;
                }

                if (best != null && bestDist <= maxDist && !tie)
                    tokens[i] = best;
            }

            return string.Join(' ', tokens);
        }

        private static bool IsCyrillic(string s)
        {
            foreach (var c in s)
                if (!((c >= 'а' && c <= 'я') || c == 'ё'))
                    return false;
            return s.Length > 0;
        }

        // Расстояние Левенштейна с ранним выходом (если превышает max — возвращает -1)
        private static int Levenshtein(string a, string b, int max)
        {
            int la = a.Length, lb = b.Length;
            if (Math.Abs(la - lb) > max) return -1;

            var prev = new int[lb + 1];
            var curr = new int[lb + 1];
            for (int j = 0; j <= lb; j++) prev[j] = j;

            for (int i = 1; i <= la; i++)
            {
                curr[0] = i;
                int rowMin = curr[0];
                for (int j = 1; j <= lb; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    curr[j] = Math.Min(Math.Min(prev[j] + 1, curr[j - 1] + 1), prev[j - 1] + cost);
                    if (curr[j] < rowMin) rowMin = curr[j];
                }
                if (rowMin > max) return -1;

                var tmp = prev; prev = curr; curr = tmp;
            }
            return prev[lb] <= max ? prev[lb] : -1;
        }
    }
}
