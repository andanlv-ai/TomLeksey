using System;
using System.Collections.Generic;

namespace PowerLanguage.Strategy
{
    public static class TestsLevels
    {
        static LraBar B(double hi, double lo) { return new LraBar { High = hi, Low = lo }; }

        public static void Run()
        {
            var s = new LraSettings { SwingLookback = 6, OffsetTicks = 1, Tick = 0.5, PipSize = 1 };

            // верхние свинги: idx2 (High 12), idx5 (High 15, выше всех), idx7 (High 11, ниже цены 13); idx8 - последний бар
            var up = new List<LraBar> {
                B(5, 1), B(8, 1), B(12, 1), B(8, 1), B(9, 1), B(15, 1), B(9, 1), B(11, 1), B(10, 1) };
            // idx5 (15) свинг, idx2 (12) свинг, idx7 (11) свинг. price 10: ближайший по времени выше цены — idx7 (11)
            TestMain.Check(TestMain.Near(LraLevels.Nearest(up, 1, 10, s), 11.5), "Nearest +1: ближайший по времени выше цены, с отступом");
            // price 13: idx7 (11) ниже цены не берётся, следующий — idx5 (15)
            TestMain.Check(TestMain.Near(LraLevels.Nearest(up, 1, 13, s), 15.5), "Nearest +1: свинг ниже цены пропускается");
            s.SwingLookback = 2; // окно idx 6..7, свингов выше 13 нет
            TestMain.Check(double.IsNaN(LraLevels.Nearest(up, 1, 13, s)), "Nearest +1: свинг дальше SwingLookback -> NaN");
            s.SwingLookback = 6;

            // последний бар свингом не считается
            var lastBar = new List<LraBar> { B(5, 1), B(6, 1), B(20, 1) };
            TestMain.Check(double.IsNaN(LraLevels.Nearest(lastBar, 1, 10, s)), "Nearest: последний бар не свинг");

            // нижние свинги: зеркально
            var dn = new List<LraBar> { B(9, 8), B(9, 5), B(9, 8), B(9, 7), B(9, 8) };
            TestMain.Check(TestMain.Near(LraLevels.Nearest(dn, -1, 9, s), 6.5), "Nearest -1: нижний свинг с отступом вниз");
            TestMain.Check(double.IsNaN(LraLevels.Nearest(dn, -1, 4, s)), "Nearest -1: свинг выше цены не берётся");

            // DayAtr: 4 полных дня 10/20/30/40 + неполный текущий
            var h = new List<LraBar>();
            double[] spans = { 10, 20, 30, 40 };
            for (int d = 0; d < 4; d++)
            {
                var day = new DateTime(2024, 1, 1 + d);
                h.Add(new LraBar { Paris = day.AddHours(9), High = 100 + spans[d], Low = 100 });
                h.Add(new LraBar { Paris = day.AddHours(10), High = 100 + spans[d] / 2, Low = 100 + 1 });
            }
            h.Add(new LraBar { Paris = new DateTime(2024, 1, 5, 9, 0, 0), High = 500, Low = 0 }); // текущий день не считается
            var s2 = new LraSettings { AtrPeriod = 14, PipSize = 1 };
            TestMain.Check(TestMain.Near(LraLevels.DayAtr(h, s2) / s2.PipSize, 25), "DayAtr: средний размах 4 дней = 25");
            s2.AtrPeriod = 2; // последние 2 полных дня: 30 и 40
            TestMain.Check(TestMain.Near(LraLevels.DayAtr(h, s2), 35), "DayAtr: берутся последние AtrPeriod дней");

            // 2 полных дня -> NaN
            var h2 = new List<LraBar> { h[0], h[1], h[2], h[3], h[8] };
            // h[8] — 5 января, дни 1 и 2 полные... + текущий
            TestMain.Check(double.IsNaN(LraLevels.DayAtr(h2, new LraSettings())), "DayAtr: меньше 3 полных дней -> NaN");
        }
    }
}
