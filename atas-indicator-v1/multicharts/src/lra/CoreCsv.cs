using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace PowerLanguage.Strategy
{
    // Запись CSV без дублей (ТЗ §8): перенос Write/ReadWatermark из DDAutoTrader.Strategy.CS.
    public class LraCsv
    {
        private static readonly CultureInfo CI = CultureInfo.InvariantCulture;
        private readonly string _dir;
        private readonly string _sym;
        private readonly Dictionary<string, DateTime> _watermark = new Dictionary<string, DateTime>();
        private readonly Dictionary<string, List<string>> _backlog = new Dictionary<string, List<string>>();

        // dir — папка файлов (создать, если нет); sym — символ, колонка 3 строк (индекс 2) для метки времени.
        public LraCsv(string dir, string sym)
        {
            _dir = dir;
            _sym = sym;
            Directory.CreateDirectory(dir);
        }

        // key = DateTime.MaxValue — писать всегда (runs.csv). Иначе строка пишется, только если key новее
        // последней строки этого символа в файле на момент первого обращения к файлу. Колонка 0 — "yyyy-MM-dd HH:mm".
        // Файл занят (открыт в Excel) — строка ждёт в очереди до следующей записи.
        public void Write(string file, string header, DateTime key, string line)
        {
            string path = Path.Combine(_dir, file);
            if (key != DateTime.MaxValue)
            {
                DateTime wm;
                if (!_watermark.TryGetValue(file, out wm)) wm = _watermark[file] = ReadWatermark(path);
                if (key <= wm) return;   // метка читается один раз за расчёт: строки с одним временем не теряются
            }
            List<string> queue;
            if (!_backlog.TryGetValue(file, out queue)) queue = _backlog[file] = new List<string>();
            if (header != null && !File.Exists(path) && queue.Count == 0) queue.Add(header);
            queue.Add(line);
            try
            {
                File.AppendAllText(path, string.Join("\r\n", queue) + "\r\n");
                queue.Clear();
            }
            catch (IOException ex)
            {
                Console.WriteLine("[LraTrader] {0} занят, в очереди {1} строк: {2}", file, queue.Count, ex.Message);
            }
        }

        private DateTime ReadWatermark(string path)
        {
            DateTime max = DateTime.MinValue;
            if (!File.Exists(path)) return max;
            foreach (string line in File.ReadAllLines(path))
            {
                string[] p = line.Split(',');
                DateTime t;
                if (p.Length > 2 && p[2] == _sym &&
                    DateTime.TryParseExact(p[0], "yyyy-MM-dd HH:mm", CI, DateTimeStyles.None, out t) && t > max)
                    max = t;
            }
            return max;
        }
    }
}
