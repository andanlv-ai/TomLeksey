using System;
using System.Collections.Generic;

namespace PowerLanguage.Strategy
{
    // Запись CSV без дублей (ТЗ §8): перенос Write/ReadWatermark из DDAutoTrader.Strategy.CS.
    public class LraCsv
    {
        // dir — папка файлов (создать, если нет); sym — символ, колонка 3 строк (индекс 2) для метки времени.
        public LraCsv(string dir, string sym) { throw new NotImplementedException(); }

        // key = DateTime.MaxValue — писать всегда (runs.csv). Иначе строка пишется, только если key новее
        // последней строки этого символа в файле на момент первого обращения к файлу. Колонка 0 — "yyyy-MM-dd HH:mm".
        public void Write(string file, string header, DateTime key, string line) { throw new NotImplementedException(); }
    }
}
