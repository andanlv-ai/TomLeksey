// Прогон ядра LraTrader по CSV записывателя (BP6Z26_TICKS = Data1 аск/бид, BP6Z26_UPDN = Data2 ап/даунтик)
// и перебор порогов по одному. Повторяет цикл CalcBar из Strategy.cs без MultiCharts.
// Сборка и запуск: [SYM=EU6Z26] bash src/lra/sweep/run.sh [папка [only|cum|frac]]
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace PowerLanguage.Strategy
{
    public static partial class Sweep
    {
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;
        static TimeZoneInfo Chi = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
        static TimeZoneInfo Par = TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");
        static string[] News = { "14:30-15:30", "19:00-21:00" };
        // Символ — переменная окружения SYM (по умолчанию фунт). Тик — как в MultiCharts у этого контракта.
        static string Sym = Environment.GetEnvironmentVariable("SYM") ?? "BP6Z26";
        static double Tick = Sym.StartsWith("EU") ? 0.00005 : 0.0001;

        static List<LraBar> Load(string dir)
        {
            var up = new Dictionary<string, string[]>();
            foreach (string l in File.ReadLines(Path.Combine(dir, Sym + "_UPDN.csv")).Skip(1)) { var f = l.Split(','); up[f[0]] = f; }
            var res = new List<LraBar>();
            foreach (string l in File.ReadLines(Path.Combine(dir, Sym + "_TICKS.csv")).Skip(1))
            {
                var f = l.Split(',');
                DateTime t = DateTime.ParseExact(f[0], "yyyy-MM-dd HH:mm", CI);
                DateTime p;
                try { p = TimeZoneInfo.ConvertTime(t, Chi, Par); } catch { p = t; }
                double h = p.TimeOfDay.TotalHours;
                var b = new LraBar
                {
                    Time = t, Paris = p, Session = h < 8 ? "ASIA" : h < 14.5 ? "EUROPE" : h < 22 ? "AMERICA" : "OFF",
                    Open = D(f[3]), High = D(f[4]), Low = D(f[5]), Close = D(f[6]), Ask = D(f[7]), Bid = D(f[8]), NoData = true
                };
                b.Volume = b.Ask + b.Bid;
                var lv = f[10].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Split(':')).ToArray();
                b.LvPrice = lv.Select(x => D(x[0])).ToArray(); b.LvAsk = lv.Select(x => D(x[1])).ToArray(); b.LvBid = lv.Select(x => D(x[2])).ToArray();
                string[] u;
                if (b.Volume > 0 && up.TryGetValue(f[0], out u) && D(u[7]) + D(u[8]) > 0)
                { b.UpTicks = D(u[7]); b.DownTicks = D(u[8]); b.NoData = false; }
                res.Add(b);
            }
            return res;
        }
        static double D(string s) { return double.Parse(s, CI); }

        class Result { public int Sig, Taken, Trades, Win, Loss, QW, QL; public double Pips, MaxDd; }
        public static Func<IList<LraBar>, double, double, List<LraRange>, LraSettings, List<LraSignal>> Eval = LraScen.Evaluate;

        static Result Run(List<LraBar> all, LraSettings s)
        {
            var r = new Result();
            var h = new List<LraBar>(); var ranges = new List<LraRange>(); var pos = new Dictionary<string, LraPosition>();
            var tr = new List<double>();
            double eq = 0, peak = 0;
            foreach (LraBar b in all)
            {
                h.Add(b);
                int n = h.Count;
                tr.Add(n == 1 ? b.High - b.Low : Math.Max(b.High, h[n - 2].Close) - Math.Min(b.Low, h[n - 2].Close));
                double atr = tr.Skip(Math.Max(0, n - s.AtrPeriod)).Average();
                foreach (string book in pos.Keys.ToList())
                {
                    LraPosition p = pos[book];
                    foreach (LraExit x in LraPos.OnBar(h, p, atr, s))
                    {
                        double pips = p.Dir * (x.Price - x.Unit.Entry) / s.PipSize;
                        if (b.Time < CountFrom || b.Time >= CountTo) continue;
                        r.Trades++; r.Pips += pips; if (pips > 0) r.Win++; else if (pips < 0) r.Loss++;
                        eq += pips; peak = Math.Max(peak, eq); r.MaxDd = Math.Max(r.MaxDd, peak - eq);
                    }
                    if (p.Units.Count == 0) pos.Remove(book);
                }
                LraRanges.Update(h, ranges, s, new List<LraRange>());
                double dayAtr = LraLevels.DayAtr(h, s);
                bool weekend = b.Paris.DayOfWeek == DayOfWeek.Saturday || b.Paris.DayOfWeek == DayOfWeek.Sunday;
                bool news = News.Any(w => { var a = w.Split('-'); var tod = b.Paris.TimeOfDay; return tod >= TimeSpan.Parse(a[0], CI) && tod <= TimeSpan.Parse(a[1], CI); });
                foreach (LraSignal sg in Eval(h, atr, dayAtr, ranges, s))
                {
                    if (sg.Skip == "" && Math.Abs(sg.TP - sg.Entry) / s.PipSize < MinTp - 1e-9) sg.Skip = "SMALL_TP";
                    if (sg.Skip == "" && Math.Abs(sg.SL - sg.Entry) / s.PipSize < MinSl - 1e-9) sg.Skip = "SMALL_SL";
                    if (sg.Skip == "" && MaxRR > 0 && Math.Abs(sg.SL - sg.Entry) > MaxRR * Math.Abs(sg.TP - sg.Entry) + 1e-9) sg.Skip = "RR";
                    if (sg.Skip == "") { if (weekend) sg.Skip = "WEEKEND"; else if (news) sg.Skip = "NEWS"; else if (pos.ContainsKey(sg.Book)) sg.Skip = "IN_POSITION"; }
                    r.Sig++;
                    if (sg.Skip == "")
                    {
                        r.Taken++; pos[sg.Book] = LraPos.Open(sg, h, "");
                        // качество сигнала без сопровождения: +10 п. раньше −10 п. за 8 часов
                        int i0 = h.Count - 1; double e = sg.Entry, q = 10 * s.PipSize;
                        for (int k = i0 + 1; k < Math.Min(all.Count, i0 + 9); k++)
                        {
                            bool fav = sg.Dir > 0 ? all[k].High >= e + q : all[k].Low <= e - q;
                            bool adv = sg.Dir > 0 ? all[k].Low <= e - q : all[k].High >= e + q;
                            if (adv) { r.QL++; break; }
                            if (fav) { r.QW++; break; }
                        }
                    }
                }
            }
            return r;
        }

        public static double MaxRR = 0, MinTp = 0, MinSl = 0;
        public static DateTime CountFrom = DateTime.MinValue, CountTo = DateTime.MaxValue;
        static LraSettings Def() { return new LraSettings { Tick = Tick }; }

        static void Print(string name, Result r)
        {
            Console.WriteLine("{0,-34} взято {2,3}  сделок {3,3}  +{4,3} -{5,3}  итог {6,5:0} п.  на сделку {7,5:0.0}  просадка {8,4:0}  10/10: {9}/{10}",
                name, r.Sig, r.Taken, r.Trades, r.Win, r.Loss, r.Pips, r.Trades > 0 ? r.Pips / r.Trades : 0, r.MaxDd, r.QW, r.QL);
        }

        // Стоп и тейк по фракталам: ширина фрактала × глубина поиска × пределы стопа/тейка × пропуск сделок
        // с тейком (тмин) или стопом (смин) короче заданного.
        static void FracGrid(List<LraBar> bars)
        {
            var rows = new List<Tuple<string, Result>>();
            foreach (int fb in new[] { 1, 2, 3, 4, 5, 6 })
            foreach (int look in new[] { 24, 48, 96 })
            foreach (var lim in new[] { Tuple.Create(30.0, 15.0), Tuple.Create(40.0, 30.0) })
            foreach (double minTp in new[] { 0.0, 5.0, 8.0, 10.0 })
            foreach (double minSl in new[] { 0.0, 5.0, 8.0, 10.0 })
            {
                var s = Def(); s.FractalBars = fb; s.SwingLookback = look; s.MaxStopPips = lim.Item1; s.MaxTakePips = lim.Item2;
                MinTp = minTp; MinSl = minSl;
                rows.Add(Tuple.Create(string.Format(CI, "фр{0} глуб{1} лимит{2}/{3} тмин{4} смин{5}", fb, look, lim.Item1, lim.Item2, minTp, minSl), Run(bars, s)));
            }
            MinTp = 0; MinSl = 0;
            Console.WriteLine("\n--- среднее по вариантам для каждого значения ---");
            foreach (string key in new[] { "фр", "глуб", "лимит", "тмин", "смин" })
                foreach (var g in rows.GroupBy(r => r.Item1.Split(' ').First(x => x.StartsWith(key))))
                    Console.WriteLine("{0,-10} сделок {1,5:0.0}  итог {2,6:0} п.  на сделку {3,5:0.0}  просадка {4,4:0}", g.Key,
                        g.Average(r => r.Item2.Trades), g.Average(r => r.Item2.Pips),
                        g.Average(r => r.Item2.Trades > 0 ? r.Item2.Pips / r.Item2.Trades : 0), g.Average(r => r.Item2.MaxDd));
            Console.WriteLine("\n--- 20 лучших (сделок >= 6) ---");
            foreach (var r in rows.Where(r => r.Item2.Trades >= 6).OrderByDescending(r => r.Item2.Pips).Take(20)) Print(r.Item1, r.Item2);
            Console.WriteLine("\n--- все варианты ---");
            foreach (var r in rows) Print(r.Item1, r.Item2);
        }

        public static void Main(string[] args)
        {
            var bars = Load(args.Length > 0 ? args[0] : @"D:\TradeLog\Bars");
            Console.WriteLine("баров {0}, с данными {1}", bars.Count, bars.Count(b => !b.NoData));
            Print("ПО УМОЛЧАНИЮ", Run(bars, Def()));
            if (args.Length > 1 && args[1] == "cum") { CumGrid(bars); return; }
            if (args.Length > 1 && args[1] == "frac") { FracGrid(bars); return; }
            if (args.Length > 1) return;
            var grid = new Dictionary<string, object[]>
            {
                { "ImbBars",        new object[] { 1, 2, 3, 4, 6 } },
                { "MinShare",       new object[] { 0.02, 0.03, 0.05, 0.08, 0.1, 0.15 } },
                { "LevelImbPct",    new object[] { 0.1, 0.2, 0.3, 0.4, 0.5 } },
                { "MinLevels",      new object[] { 1, 2, 3, 4, 6 } },
                { "StallAtr",       new object[] { 0.0, 0.25, 0.5, 1.0 } },
                { "StrongAtr",      new object[] { 1.0, 1.5, 2.0, 3.0 } },
                { "MaxStopPips",    new object[] { 15.0, 20.0, 30.0, 40.0 } },
                { "MaxTakePips",    new object[] { 10.0, 15.0, 20.0, 30.0 } },
                { "AddStepPips",    new object[] { 5.0, 10.0, 15.0, 20.0, 1000.0 } },
                { "AddTargetMode",  new object[] { "SAME_TP", "ENTRY1" } },
                { "SwingLookback",  new object[] { 12, 24, 48, 96 } },
                { "FractalBars",    new object[] { 1, 2, 3 } },
                { "MaxRangePips",   new object[] { 30.0, 40.0, 50.0, 60.0, 80.0 } },
                { "MinRangeBars",   new object[] { 3, 4, 6, 8 } },
                { "MaxRangeAgeBars",new object[] { 24, 48, 120, 240 } },
                { "LraMinDistPips", new object[] { 15.0, 30.0, 45.0 } },
                { "DayTargetShare", new object[] { 0.3, 0.5, 0.7 } },
            };
            foreach (var kv in grid)
            {
                Console.WriteLine();
                FieldInfo fi = typeof(LraSettings).GetField(kv.Key);
                foreach (object v in kv.Value)
                {
                    var s = Def();
                    fi.SetValue(s, Convert.ChangeType(v, fi.FieldType, CI));
                    Print(kv.Key + " = " + Convert.ToString(v, CI), Run(bars, s));
                }
            }
        }
    }
}
