// Шаги 2–3 ТЗ docs/LraTrader/tz-ticks.md: по тикам (<символ>_TRADES.csv) найти моменты набора позиций толпой,
// посмотреть, что было дальше, и смоделировать сделку против толпы.
// Запуск: bash src/lra/ticks/run.sh (из atas-indicator-v1/multicharts). Результат — D:\TradeLog\Ticks.
// Описано для «толпа покупает» (d = +1); «продаёт» (d = -1) — зеркально через d.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace PowerLanguage.Strategy
{
    public static class Ticks
    {
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;
        static readonly TimeZoneInfo Chi = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
        static readonly TimeZoneInfo Par = TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");
        const string BarsDir = @"D:\TradeLog\Bars", OutDir = @"D:\TradeLog\Ticks";

        static readonly int[] Horizons = { 8, 24, 48 };


        public static void Main(string[] args)
        {
            Directory.CreateDirectory(OutDir);
            var sum = new StringWriter();
            foreach (string sym in new[] { "BP6Z26", "EU6Z26" })
            {
                var k = new LraTicks(sym.StartsWith("EU") ? 0.00005 : 0.0001);
                List<LraBar> hours = LoadHours(Path.Combine(BarsDir, sym + "_TICKS.csv"));
                LraTicks.Tape fills = LraTicks.Load(Path.Combine(BarsDir, sym + "_TRADES.csv"));
                foreach (var v in new[] { Tuple.Create("F", fills), Tuple.Create("G", LraTicks.Group(fills)) })
                {
                    List<LraTicks.Ev> evs = k.Detect(v.Item2, hours);
                    foreach (LraTicks.Ev e in evs) k.Follow(v.Item2, hours, e);
                    Summary(sum, sym, v.Item1, v.Item2, evs);
                    foreach (string mode in new[] { "H0", "FRACTAL", "DAY", "WITH", "FAST", "FAST_SL", "FAST_TP", "SWEEP", "SWEEP_ALL" })
                        foreach (bool add in new[] { false, true })
                        {
                            if (mode != "H0" && mode != "FRACTAL" && mode != "DAY" && add) continue;
                            foreach (LraTicks.Ev e in evs) k.Trade(v.Item2, hours, e, mode, add);
                            LraTicks.Simulate(evs);
                            string name = v.Item1 + "_" + mode + (add ? "_ADD" : "");
                            Write(sym, name, evs);
                            TradeSummary(sum, mode + (add ? " + добор" : ""), evs);
                        }
                }
            }
            File.WriteAllText(Path.Combine(OutDir, "summary.txt"), sum.ToString());
            Console.Write(sum.ToString());
        }

        // Часовые бары BarRecorder: Time — закрытие бара; Ask/Bid — дельта по объёму
        static List<LraBar> LoadHours(string path)
        {
            var res = new List<LraBar>();
            foreach (string l in File.ReadLines(path).Skip(1))
            {
                var f = l.Split(',');
                res.Add(new LraBar { Time = DateTime.ParseExact(f[0], "yyyy-MM-dd HH:mm", CI), Open = D(f[3]), High = D(f[4]),
                    Low = D(f[5]), Close = D(f[6]), Ask = D(f[7]), Bid = D(f[8]) });
            }
            return res;
        }
        static double D(string s) { return double.Parse(s, CI); }

        // ---------- вывод ----------
        static string Ts(long t) { return t == 0 ? "" : new DateTime(t).ToString("yyyy-MM-dd HH:mm", CI); }
        static string Paris(long t) { return t == 0 ? "" : TimeZoneInfo.ConvertTime(new DateTime(t), Chi, Par).ToString("yyyy-MM-dd HH:mm", CI); }
        static string Hrs(long from, long t) { return t == 0 ? "" : ((t - from) / (double)TimeSpan.TicksPerHour).ToString("0.0", CI); }

        static void Write(string sym, string v, List<LraTicks.Ev> evs)
        {
            var w = new StringWriter();
            w.WriteLine("Time,ParisTime,Crowd,GrowthStart,LastSeen,Trades,Weight,SmallShare,Prices,ZoneLo,ZoneHi,ZoneImb,H0,Entry,Target," +
                        "First,H_Target,H_Cancel,H_Return,H_Close50,H_Close100,MaxFav,MaxAdv,Skip,SL,TP,Exit,Reason,Pips,EntryParis,ExitParis");
            foreach (LraTicks.Ev e in evs)
                w.WriteLine(string.Format(CI, "{0},{1},{2},{3},{4},{5},{6:0.000},{7:0.000},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21:0.0},{22:0.0},{23},{24},{25},{26},{27},{28},{29},{30}",
                    Ts(e.TFirst), Paris(e.TFirst), e.Side > 0 ? "BUY" : "SELL", Ts(e.TS), Ts(e.TLast), e.Trades, e.Weight,
                    (double)e.Small / Math.Max(1, e.Trades), e.Prices, e.ZoneLo, e.ZoneHi, e.Zone, e.H0, e.Entry,
                    double.IsNaN(e.Target) ? "" : e.Target.ToString(CI), e.First,
                    Hrs(e.TFirst, e.TTarget), Hrs(e.TFirst, e.TCancel), Hrs(e.TFirst, e.TReturn), Hrs(e.TFirst, e.TClose50), Hrs(e.TFirst, e.TClose100),
                    e.MaxFav, e.MaxAdv, e.Skip, e.Skip == "" ? e.SL.ToString(CI) : "", e.Skip == "" ? e.TP.ToString(CI) : "",
                    e.Skip == "" ? e.Exit.ToString(CI) : "", e.Reason + (e.Added ? "+ADD" : ""),e.Skip == "" ? e.Pips.ToString("0.0", CI) : "", e.Skip == "" ? Paris(e.TEntry) : "", e.Skip == "" ? Paris(e.TExit) : ""));
            File.WriteAllText(Path.Combine(OutDir, "events_" + sym + "_" + v + ".csv"), w.ToString());
        }

        static void Summary(StringWriter o, string sym, string v, LraTicks.Tape a, List<LraTicks.Ev> evs)
        {
            o.WriteLine("==== {0}, вариант {1} ({2}): сделок в ленте {3}, событий {4} (толпа покупает {5}, продаёт {6}), дней с событиями {7}",
                sym, v, v == "F" ? "каждое исполнение" : "группы исполнений", a.N, evs.Count, evs.Count(e => e.Side > 0), evs.Count(e => e.Side < 0),
                evs.Select(e => new DateTime(e.TFirst).Date).Distinct().Count());
            foreach (int h in Horizons)
            {
                long lim = h * TimeSpan.TicksPerHour;
                Func<Func<LraTicks.Ev, long>, string> pct = f => (100.0 * evs.Count(e => f(e) > 0 && f(e) - e.TFirst <= lim) / Math.Max(1, evs.Count)).ToString("0", CI) + "%";
                o.WriteLine("  за {0,2} ч: цель (стопы толпы) {1}, отмена {2}, возврат к зоне {3}, перекос зоны закрыт на 50% {4}, на 100% {5}",
                    h, pct(e => e.TTarget), pct(e => e.TCancel), pct(e => e.TReturn), pct(e => e.TClose50), pct(e => e.TClose100));
            }
            o.WriteLine("  что первым за 48 ч: " + string.Join(", ", evs.GroupBy(e => e.First == "" ? "ничего" : e.First)
                .OrderByDescending(g => g.Count()).Select(g => g.Key + " " + g.Count())));
        }

        static void TradeSummary(StringWriter o, string name, List<LraTicks.Ev> evs)
        {
            var tr = evs.Where(e => e.Skip == "").ToList();
            double eq = 0, peak = 0, dd = 0;
            foreach (LraTicks.Ev e in tr) { eq += e.Pips; peak = Math.Max(peak, eq); dd = Math.Max(dd, peak - eq); }
            o.WriteLine("  стоп {0}: сделки {1} (с добором {2}), плюс {3}, минус {4}, итог {5:0} п., на сделку {6:0.0}, просадка {7:0} п., стоп в среднем {8:0.0} п.; выходы: {9}; пропуски: {10}",
                name, tr.Count, tr.Count(e => e.Added), tr.Count(e => e.Pips > 0), tr.Count(e => e.Pips < 0), tr.Sum(e => e.Pips),
                tr.Count > 0 ? tr.Average(e => e.Pips) : 0, dd, tr.Count > 0 ? tr.Average(e => Math.Min(Math.Abs(e.SL - e.Entry) / LraTicks.Pip, 999)) : 0,
                string.Join(", ", tr.GroupBy(e => e.Reason).Select(g => g.Key + " " + g.Count())),
                string.Join(", ", evs.Where(e => e.Skip != "").GroupBy(e => e.Skip).Select(g => g.Key + " " + g.Count())));
        }
    }
}
