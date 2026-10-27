// Прототип: толпа по кумулятивной дельте по сделкам, как у автора (В2 19:17, 22:05; В4 02:28).
// Окно — от начала текущего роста (минимум/максимум кумулятивной дельты), вес — рост / объём с начала роста.
// Стоп, тейк, фильтр ЛРА — как в LraScen (только сценарий A).
using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerLanguage.Strategy
{
    public static partial class Sweep
    {
        static int CLook, CMinBars = 2;       // CLook: часов назад искать начало роста, 0 — с начала дня
        static double CWeight, CStall;        // CStall < 0 — строго: за время роста цена не обновила свой хай/лоу
        static bool CLevels;

        static List<LraSignal> CumEval(IList<LraBar> h, double atr, double dayAtr, List<LraRange> ranges, LraSettings s)
        {
            var res = new List<LraSignal>();
            int last = h.Count - 1;
            if (last < 50 || h[last].NoData) return res;
            int l0 = last;
            if (CLook == 0) while (l0 > 0 && h[l0 - 1].Paris.Date == h[last].Paris.Date) l0--;
            else l0 = Math.Max(1, last - CLook + 1);
            // c[k] — кумулятивная дельта по сделкам с базой 0 на баре l0-1
            int b0 = l0 - 1;
            var c = new double[last + 1];
            for (int k = l0; k <= last; k++) c[k] = c[k - 1 < b0 ? b0 : k - 1] + (h[k].NoData ? double.NaN : h[k].UpTicks - h[k].DownTicks);
            if (double.IsNaN(c[last])) return res;
            int mn = b0, mx = b0;
            for (int k = b0; k <= last - CMinBars; k++) { if (c[k] < c[mn]) mn = k; if (c[k] > c[mx]) mx = k; }

            LraSignal best = null; double bestW = 0;
            foreach (int d in new[] { 1, -1 })
            {
                int m = d > 0 ? mn : mx;
                if (last - m < CMinBars) continue;
                double g = c[last] - c[m], vol = 0, vd = 0;
                for (int k = m + 1; k <= last; k++) { vol += h[k].UpTicks + h[k].DownTicks; vd += h[k].Ask - h[k].Bid; }
                if (vol <= 0 || g * d <= 0) continue;
                double w = Math.Abs(g) / vol;
                if (w < CWeight || w <= bestW) continue;
                if (-d * vd / vol >= s.MinShare) continue;                       // дельта по объёму против — противоречие
                LraImbalance lv = LraImb.Measure(h, m + 1, last, s);
                if (CLevels && (d > 0 ? lv.BuyLevels : lv.SellLevels) < s.MinLevels) continue;
                if (CStall < 0)
                {
                    bool broke = false;
                    for (int k = m + 1; k <= last; k++) if (d > 0 ? h[k].High > h[m].High : h[k].Low < h[m].Low) broke = true;
                    if (broke) continue;
                }
                else if ((h[last].Close - h[m].Close) * d > CStall * atr) continue;

                int dir = -d; double entry = h[last].Close, pip = s.PipSize;
                double sl = LraLevels.Nearest(h, -dir, entry, s), tp = LraLevels.Nearest(h, dir, entry, s), tp2 = double.NaN;
                string skip = "";
                if (double.IsNaN(sl) || double.IsNaN(tp) || Math.Abs(sl - entry) / pip > s.MaxStopPips) skip = "NO_LEVEL";
                else if (Math.Abs(tp - entry) / pip > s.MaxTakePips) tp = entry + dir * s.MaxTakePips * pip;
                LraRange range = LraRanges.NearestActive(ranges, entry);
                if (range != null)
                {
                    double dist = Math.Abs(entry - range.Poc) / pip, toPoc = dir * (range.Poc - entry);
                    if (skip == "" && toPoc < 0) skip = "AGAINST_LRA";
                    else if (skip == "" && toPoc > 0 && dist >= s.LraMinDistPips && !double.IsNaN(dayAtr) && dist <= s.DayTargetShare * dayAtr / pip) tp2 = range.Poc;
                }
                lv.Dir = d;
                best = new LraSignal { Book = "AGAINST", Dir = dir, Entry = entry, SL = sl, TP = tp, TP2 = tp2, WinFrom = m + 1, Crowd = lv, Range = range, Skip = skip };
                bestW = w;
            }
            if (best != null) res.Add(best);
            return res;
        }

        static void CumGrid(List<LraBar> bars)
        {
            if (Environment.GetEnvironmentVariable("HALVES") == "1") { Halves(bars); return; }
            var rows = new List<Tuple<string, Result>>();
            foreach (int look in new[] { 0, 12, 24, 48 })
            foreach (int mb in new[] { 2, 3 })
            foreach (double w in new[] { 0.05, 0.1, 0.15, 0.2, 0.3 })
            foreach (double st in new[] { -1.0, 0.0, 0.5 })
            foreach (bool lvl in new[] { true, false })
            {
                CLook = look; CMinBars = mb; CWeight = w; CStall = st; CLevels = lvl;
                Eval = CumEval;
                rows.Add(Tuple.Create(string.Format(CI, "окно{0} мин{1} вес{2} цена{3} уровни{4}", look == 0 ? "день" : look + "ч", mb, w, st < 0 ? "строго" : st.ToString(CI), lvl ? "+" : "-"), Run(bars, Def())));
            }
            Eval = LraScen.Evaluate;
            var baseR = Run(bars, Def());
            Console.WriteLine("вариантов {0}; лучше текущего по итогу: {1}; в плюсе: {2}", rows.Count,
                rows.Count(r => r.Item2.Pips > baseR.Pips), rows.Count(r => r.Item2.Pips > 0));
            Console.WriteLine("\n--- среднее по вариантам для каждого значения ---");
            string[] keys = { "окно", "мин", "вес", "цена", "уровни" };
            foreach (string key in keys)
                foreach (var g in rows.GroupBy(r => r.Item1.Split(' ').First(x => x.StartsWith(key))))
                    Console.WriteLine("{0,-16} сделок {1,5:0.0}  итог {2,6:0} п.  на сделку {3,5:0.0}  10/10 доля {4,4:0}%", g.Key,
                        g.Average(r => r.Item2.Trades), g.Average(r => r.Item2.Pips),
                        g.Average(r => r.Item2.Trades > 0 ? r.Item2.Pips / r.Item2.Trades : 0),
                        100.0 * g.Sum(r => r.Item2.QW) / Math.Max(1, g.Sum(r => r.Item2.QW + r.Item2.QL)));
            Console.WriteLine("\n--- 15 лучших (сделок >= 10) ---");
            foreach (var r in rows.Where(r => r.Item2.Trades >= 10).OrderByDescending(r => r.Item2.Pips).Take(15)) Print(r.Item1, r.Item2);
        }

        static void Halves(List<LraBar> bars)
        {
            var mid = new DateTime(2026, 10, 12);
            var cands = new[] {
                Tuple.Create(0, 0, 0.0, 0.0, false), Tuple.Create(12, 2, 0.05, -1.0, false), Tuple.Create(12, 2, 0.05, 0.0, false),
                Tuple.Create(12, 2, 0.1, -1.0, false), Tuple.Create(0, 2, 0.05, -1.0, false), Tuple.Create(12, 2, 0.05, -1.0, true) };
            foreach (var c in cands)
            {
                if (c.Item1 == 0 && c.Item2 == 0) Eval = LraScen.Evaluate;
                else { Eval = CumEval; CLook = c.Item1; CMinBars = c.Item2; CWeight = c.Item3; CStall = c.Item4; CLevels = c.Item5; }
                string name = c.Item2 == 0 ? "ТЕКУЩИЙ" : string.Format(CI, "окно{0} вес{1} цена{2} уровни{3}", c.Item1 == 0 ? "день" : c.Item1 + "ч", c.Item3, c.Item4 < 0 ? "строго" : "0", c.Item5 ? "+" : "-");
                CountFrom = DateTime.MinValue; CountTo = mid; Print(name + " | до 12.10", Run(bars, Def()));
                CountFrom = mid; CountTo = DateTime.MaxValue; Print(name + " | с 12.10", Run(bars, Def()));
            }
        }
    }
}
