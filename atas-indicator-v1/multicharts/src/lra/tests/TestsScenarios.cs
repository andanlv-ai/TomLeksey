using System;
using System.Collections.Generic;

namespace PowerLanguage.Strategy
{
    public static class TestsScenarios
    {
        static readonly DateTime Day = new DateTime(2024, 3, 5, 10, 0, 0);

        // Обычный бар без сделок: на итоги дня не влияет.
        static LraBar Q(int i, double hi, double lo, double close)
        {
            return new LraBar { Paris = Day.AddHours(i), High = hi, Low = lo, Close = close };
        }

        // Бар окна: толпа покупает. Ask 90 / Bid 10, сделок вверх 80 / вниз 20, три покупательских уровня.
        static LraBar W(int i, double hi, double lo, double close)
        {
            var b = Q(i, hi, lo, close);
            b.Ask = 90; b.Bid = 10; b.Volume = 100; b.UpTicks = 80; b.DownTicks = 20;
            b.LvPrice = new[] { 99.0, 100.0, 101.0 };
            b.LvAsk = new[] { 30.0, 30.0, 30.0 };
            b.LvBid = new[] { 5.0, 5.0, 5.0 };
            return b;
        }

        // Историю (idx 0..5) делаем со свингами: верхний 105 (idx1), нижний 93 (idx3). Окно — два последних бара.
        static List<LraBar> Swings(double close6, double close7, double hi7, double lo7)
        {
            return new List<LraBar> {
                Q(0, 101, 99, 100), Q(1, 105, 99, 100), Q(2, 101, 99, 100), Q(3, 101, 93, 100), Q(4, 101, 99, 100),
                Q(5, 100, 99, 100), W(6, 100.5, 99.5, close6), W(7, hi7, lo7, close7) };
        }

        // Те же бары, но без свингов: SL/TP найти нельзя.
        static List<LraBar> Flat()
        {
            var h = new List<LraBar>();
            for (int i = 0; i < 6; i++) h.Add(Q(i, 100.5, 99.5, 100));
            h.Add(W(6, 100.5, 99.5, 100)); h.Add(W(7, 100.5, 99.5, 100));
            return h;
        }

        static LraSettings S()
        {
            return new LraSettings { Tick = 0.5, PipSize = 1, OffsetTicks = 1, ImbBars = 2 };
        }

        static LraSignal Find(List<LraSignal> r, string book)
        {
            foreach (var x in r) if (x.Book == book) return x;
            return null;
        }

        public static void Run()
        {
            var s = S();
            var none = new List<LraRange>();
            const double atr = 2;

            // A: толпа покупает, цена стоит (move = 0 <= 0.5*2) -> один сигнал вниз
            var r = LraScen.Evaluate(Swings(100, 100, 100.5, 99.5), atr, double.NaN, none, s);
            TestMain.Check(r.Count == 1 && r[0].Book == "AGAINST", "A: один сигнал AGAINST");
            var a = r[0];
            TestMain.Check(a.Dir == -1 && TestMain.Near(a.Entry, 100), "A: Dir = -1, вход по Close");
            TestMain.Check(TestMain.Near(a.SL, 105.5), "A: SL над входом (свинг 105 + тик)");
            TestMain.Check(TestMain.Near(a.TP, 92.5), "A: TP под входом (свинг 93 - тик)");
            TestMain.Check(a.Skip == "" && a.WinFrom == 6 && a.Crowd.Dir == 1, "A: Skip пуст, WinFrom, Crowd");

            // TP дальше MaxTakePips -> обрезается до 5 пипсов
            s.MaxTakePips = 5;
            r = LraScen.Evaluate(Swings(100, 100, 100.5, 99.5), atr, double.NaN, none, s);
            TestMain.Check(r.Count == 1 && TestMain.Near(r[0].TP, 95) && r[0].Skip == "", "A: TP обрезан до MaxTakePips");
            s = S();

            // D: цена упала на 2 ATR (100 -> 96), толпа покупает
            r = LraScen.Evaluate(Swings(98, 96, 98, 95.5), atr, double.NaN, none, s);
            TestMain.Check(r.Count == 1 && r[0].Book == "MOMENTUM", "падение на 2 ATR при покупающей толпе -> ровно один сигнал MOMENTUM");
            var d = Find(r, "MOMENTUM");
            TestMain.Check(d != null && d.Dir == -1 && TestMain.Near(d.Entry, 96), "D: MOMENTUM, Dir = -1, вход по Close");
            TestMain.Check(d != null && d.Skip == "" && TestMain.Near(d.SL, 101) && TestMain.Near(d.TP, 92.5),
                "D: SL - свежий свинг окна (High 100.5 бара 6 + тик), TP - свинг 93 - тик");

            // цена выросла на 2 ATR вместе с толпой: A не проходит (4 > 1), D не проходит (знак совпал с Dir)
            r = LraScen.Evaluate(Swings(102, 104, 104, 101.5), atr, double.NaN, none, s);
            TestMain.Check(r.Count == 0, "цена идёт за толпой -> сигналов нет");

            // нет свингов -> NO_LEVEL
            r = LraScen.Evaluate(Flat(), atr, double.NaN, none, s);
            TestMain.Check(r.Count == 1 && r[0].Skip == "NO_LEVEL", "нет свинга -> NO_LEVEL");

            // SL дальше MaxStopPips -> NO_LEVEL
            s.MaxStopPips = 3;
            r = LraScen.Evaluate(Swings(100, 100, 100.5, 99.5), atr, double.NaN, none, s);
            TestMain.Check(r.Count == 1 && r[0].Skip == "NO_LEVEL", "SL дальше MaxStopPips -> NO_LEVEL");
            s = S();

            // толпы нет / мало баров -> пусто
            var quiet = Flat(); quiet[6] = Q(6, 100.5, 99.5, 100); quiet[7] = Q(7, 100.5, 99.5, 100);
            TestMain.Check(LraScen.Evaluate(quiet, atr, double.NaN, none, s).Count == 0, "Dir = 0 -> пустой список");
            var two = new List<LraBar> { W(0, 100.5, 99.5, 100), W(1, 100.5, 99.5, 100) };
            TestMain.Check(LraScen.Evaluate(two, atr, double.NaN, none, s).Count == 0, "нет Close[ImbBars] -> пустой список");

            // фильтр ЛРА: Poc выше цены, сделка вниз (от Poc) -> AGAINST_LRA
            var above = new List<LraRange> { new LraRange { Poc = 110 } };
            r = LraScen.Evaluate(Swings(100, 100, 100.5, 99.5), atr, 100, above, s);
            TestMain.Check(r.Count == 1 && r[0].Skip == "AGAINST_LRA" && r[0].Range == above[0]
                && TestMain.Near(r[0].RangeDistPips, 10) && double.IsNaN(r[0].TP2), "ЛРА выше, сделка вниз -> AGAINST_LRA");

            // завершённый ЛРА игнорируется
            var done = new List<LraRange> { new LraRange { Poc = 110, Done = true } };
            r = LraScen.Evaluate(Swings(100, 100, 100.5, 99.5), atr, 100, done, s);
            TestMain.Check(r.Count == 1 && r[0].Skip == "" && r[0].Range == null, "неактивный ЛРА не фильтрует");

            // NO_LEVEL важнее AGAINST_LRA
            r = LraScen.Evaluate(Flat(), atr, 100, above, s);
            TestMain.Check(r.Count == 1 && r[0].Skip == "NO_LEVEL" && r[0].Range == above[0], "NO_LEVEL важнее AGAINST_LRA");

            // ЛРА ниже на 40 пипсов, день 100 пипсов (0.5*100 = 50 >= 40 >= 30) -> TP2 = Poc
            var below = new List<LraRange> { new LraRange { Poc = 60 } };
            r = LraScen.Evaluate(Swings(100, 100, 100.5, 99.5), atr, 100, below, s);
            TestMain.Check(r.Count == 1 && r[0].Skip == "" && TestMain.Near(r[0].TP2, 60)
                && TestMain.Near(r[0].RangeDistPips, 40), "ЛРА ниже на 40 пипсов -> TP2 = Poc");

            // dayAtr NaN -> без TP2; слишком далеко (день 70 -> 35 < 40) -> без TP2; слишком близко (20 < 30) -> без TP2
            r = LraScen.Evaluate(Swings(100, 100, 100.5, 99.5), atr, double.NaN, below, s);
            TestMain.Check(r.Count == 1 && r[0].Skip == "" && double.IsNaN(r[0].TP2), "dayAtr NaN -> без TP2");
            r = LraScen.Evaluate(Swings(100, 100, 100.5, 99.5), atr, 70, below, s);
            TestMain.Check(r[0].Skip == "" && double.IsNaN(r[0].TP2), "ЛРА дальше DayTargetShare*ATR дня -> без TP2");
            var near = new List<LraRange> { new LraRange { Poc = 80 } };
            r = LraScen.Evaluate(Swings(100, 100, 100.5, 99.5), atr, 100, near, s);
            TestMain.Check(r[0].Skip == "" && double.IsNaN(r[0].TP2), "ЛРА ближе LraMinDistPips -> без TP2");
        }
    }
}
