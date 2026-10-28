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

        // Пороги шага 2 (ТЗ, по умолчанию)
        static readonly TimeSpan Lookback = TimeSpan.FromHours(12), MinGrowth = TimeSpan.FromMinutes(30),
            PreHour = TimeSpan.FromHours(1), Step = TimeSpan.FromMinutes(5);
        const double W = 0.2, PriceShare = 0.6, MaxOnePrice = 0.5, VolConflict = 0.05;
        const int MinPrices = 3, MinTradesAtPrice = 20, SmallSize = 3;
        // Модель сделки (шаг 3)
        const double MaxTakePips = 15, MaxStopPips = 30, ReturnPips = 10, Pip = 0.0001;
        static readonly TimeSpan Hold = TimeSpan.FromHours(48);
        static readonly int[] Horizons = { 8, 24, 48 };

        // Сделки одного варианта: время (Ticks), цена, контракты, направление
        class Tape { public long[] T; public double[] P; public int[] S; public sbyte[] D; public int N; }

        class Ev
        {
            public int Side, S, Prices, Trades, Small; public long TS, TFirst, TLast;
            public double H0, Weight, ZoneLo, ZoneHi, Zone;  // Zone — перекос зоны в сделках (в сторону толпы)
            public HashSet<long> ZoneKeys;
            // шаг 3
            public double Target = double.NaN; public long TTarget, TCancel, TReturn, TClose50, TClose100; public string First = "";
            public double MaxFav, MaxAdv, Entry;
            // сделка
            public string Skip = ""; public double SL, TP, Exit, Pips; public string Reason = ""; public long TExit, TEntry; public bool Added;
        }

        static double Tick;

        public static void Main(string[] args)
        {
            Directory.CreateDirectory(OutDir);
            var sum = new StringWriter();
            foreach (string sym in new[] { "BP6Z26", "EU6Z26" })
            {
                Tick = sym.StartsWith("EU") ? 0.00005 : 0.0001;
                List<LraBar> hours = LoadHours(Path.Combine(BarsDir, sym + "_TICKS.csv"));
                Tape fills = Load(Path.Combine(BarsDir, sym + "_TRADES.csv"));
                foreach (var v in new[] { Tuple.Create("F", fills), Tuple.Create("G", Group(fills)) })
                {
                    List<Ev> evs = Detect(v.Item2, hours);
                    foreach (Ev e in evs) Follow(v.Item2, hours, e);
                    Summary(sum, sym, v.Item1, v.Item2, evs);
                    foreach (string mode in new[] { "H0", "FRACTAL", "DAY", "WITH", "FAST", "FAST_SL", "SWEEP", "SWEEP_ALL" })
                        foreach (bool add in new[] { false, true })
                        {
                            if (mode != "H0" && mode != "FRACTAL" && mode != "DAY" && add) continue;
                            foreach (Ev e in evs) Trade(v.Item2, hours, e, mode, add);
                            Simulate(evs);
                            string name = v.Item1 + "_" + mode + (add ? "_ADD" : "");
                            Write(sym, name, evs);
                            TradeSummary(sum, mode + (add ? " + добор" : ""), evs);
                        }
                }
            }
            File.WriteAllText(Path.Combine(OutDir, "summary.txt"), sum.ToString());
            Console.Write(sum.ToString());
        }

        // ---------- загрузка ----------
        static Tape Load(string path)
        {
            var t = new List<long>(); var p = new List<double>(); var s = new List<int>(); var d = new List<sbyte>();
            // TickRecorder держит файл открытым на запись — читать с FileShare.ReadWrite
            var lines = new List<string>();
            using (var r = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                for (string l; (l = r.ReadLine()) != null;) lines.Add(l);
            foreach (string l in lines.Skip(1))
            {
                var f = l.Split(',');
                if (f.Length < 4) continue;   // недописанная последняя строка
                t.Add(DateTime.ParseExact(f[0], "yyyy-MM-dd HH:mm:ss.fff", CI).Ticks);
                p.Add(double.Parse(f[1], CI)); s.Add((int)double.Parse(f[2], CI)); d.Add(sbyte.Parse(f[3], CI));
            }
            return new Tape { T = t.ToArray(), P = p.ToArray(), S = s.ToArray(), D = d.ToArray(), N = t.Count };
        }

        // Вариант G: подряд идущие исполнения с одним временем, направлением и ценой — одна сделка
        static Tape Group(Tape a)
        {
            var t = new List<long>(); var p = new List<double>(); var s = new List<int>(); var d = new List<sbyte>();
            for (int i = 0; i < a.N; i++)
            {
                int k = t.Count - 1;
                if (k >= 0 && t[k] == a.T[i] && d[k] == a.D[i] && p[k] == a.P[i]) { s[k] += a.S[i]; continue; }
                t.Add(a.T[i]); p.Add(a.P[i]); s.Add(a.S[i]); d.Add(a.D[i]);
            }
            return new Tape { T = t.ToArray(), P = p.ToArray(), S = s.ToArray(), D = d.ToArray(), N = t.Count };
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
        static long Key(double price) { return (long)Math.Round(price / Tick); }

        // ---------- шаг 2 ----------
        static List<Ev> Detect(Tape a, List<LraBar> hours)
        {
            var c = new int[a.N];   // кумулятивная дельта по количеству сделок после сделки i
            for (int i = 0; i < a.N; i++) c[i] = (i > 0 ? c[i - 1] : 0) + a.D[i];
            var evs = new Dictionary<long, Ev>();
            int e = -1, lo = 0, hi = -1;
            long t = new DateTime(a.T[0]).Date.AddHours(new DateTime(a.T[0]).Hour + 1).Ticks + Lookback.Ticks;
            for (; t <= a.T[a.N - 1]; t += Step.Ticks)
            {
                int e0 = e;
                while (e + 1 < a.N && a.T[e + 1] < t) e++;
                if (e == e0) continue;                                   // за 5 минут сделок не было (выходные)
                while (lo < a.N && a.T[lo] < t - Lookback.Ticks) lo++;
                while (hi + 1 < a.N && a.T[hi + 1] <= t - MinGrowth.Ticks) hi++;
                if (hi < lo) continue;
                foreach (int d in new[] { 1, -1 })
                {
                    int s = lo;                                          // начало роста: минимум (d=+1) / максимум дельты
                    for (int k = lo; k <= hi; k++) if (d * c[k] < d * c[s]) s = k;
                    int n = e - s;
                    if (n <= 0 || d * (c[e] - c[s]) < W * n) continue;  // вес
                    double h0 = a.P[s];                                  // хай (лоу) за час до начала роста
                    for (int k = s; k >= 0 && a.T[k] >= a.T[s] - PreHour.Ticks; k--) if (d * a.P[k] > d * h0) h0 = a.P[k];
                    bool broke = false;
                    for (int k = s + 1; k <= e && !broke; k++) if (d * a.P[k] > d * h0) broke = true;
                    if (broke) continue;                                 // цена вышла за H0 — не «стоит»
                    var at = new Dictionary<long, int[]>();              // цена -> {вверх, вниз}
                    int small = 0;
                    for (int k = s + 1; k <= e; k++)
                    {
                        int[] x; long key = Key(a.P[k]);
                        if (!at.TryGetValue(key, out x)) at[key] = x = new int[2];
                        x[a.D[k] > 0 ? 0 : 1]++;
                        if (a.S[k] <= SmallSize && a.D[k] == d) small++;
                    }
                    var zone = at.Where(kv => kv.Value[0] + kv.Value[1] >= MinTradesAtPrice &&
                        (double)(d > 0 ? kv.Value[0] : kv.Value[1]) / (kv.Value[0] + kv.Value[1]) >= PriceShare).ToList();
                    if (zone.Count < MinPrices) continue;                // несколько цен
                    double total = d * (c[e] - c[s]);
                    double top = at.Values.Max(x => d * (x[0] - x[1]));
                    if (top > MaxOnePrice * total) continue;             // не одна цена
                    if (VolAgainst(hours, a.T[s], t, d)) continue;       // объём против
                    long id = d * ((long)s + 1);
                    Ev ev;
                    if (!evs.TryGetValue(id, out ev))
                        evs[id] = ev = new Ev { Side = d, S = s, TS = a.T[s], TFirst = t, H0 = h0,
                            ZoneKeys = new HashSet<long>(zone.Select(z => z.Key)),
                            ZoneLo = zone.Min(z => z.Key) * Tick, ZoneHi = zone.Max(z => z.Key) * Tick,
                            Zone = zone.Sum(z => d * (z.Value[0] - z.Value[1])), Prices = zone.Count, Trades = n,
                            Weight = total / n, Small = small };
                    ev.TLast = t;
                }
            }
            return evs.Values.OrderBy(x => x.TFirst).ToList();
        }

        // Дельта по объёму (Ask-Bid) по закрытым часам внутри [s, t] против толпы на VolConflict и больше
        static bool VolAgainst(List<LraBar> hours, long s, long t, int d)
        {
            double ask = 0, bid = 0;
            foreach (LraBar b in hours)
                if (b.Time.Ticks - TimeSpan.TicksPerHour >= s && b.Time.Ticks <= t) { ask += b.Ask; bid += b.Bid; }
            return ask + bid > 0 && -d * (ask - bid) / (ask + bid) >= VolConflict;
        }

        // ---------- шаг 3: что было дальше ----------
        static void Follow(Tape a, List<LraBar> hours, Ev e)
        {
            var s = new LraSettings { Tick = Tick, FractalBars = 2 };
            var closed = hours.Where(b => b.Time.Ticks <= e.TFirst).ToList();
            // цель — ближайший фрактал часовика за зоной в сторону от толпы (там стопы толпы)
            e.Target = LraLevels.Nearest(closed, -e.Side, e.Side > 0 ? e.ZoneLo : e.ZoneHi, s);
            int i = Array.BinarySearch(a.T, e.TFirst); if (i < 0) i = ~i;
            if (i >= a.N) { e.Skip = "NO_DATA"; return; }
            e.Entry = a.P[i];
            double cancel = e.H0 + e.Side * Tick;
            bool away = false; double closedSum = 0;
            for (int k = i; k < a.N && a.T[k] <= e.TFirst + Hold.Ticks; k++)
            {
                double p = a.P[k]; long t = a.T[k];
                e.MaxFav = Math.Max(e.MaxFav, -e.Side * (p - e.Entry) / Pip);
                e.MaxAdv = Math.Max(e.MaxAdv, e.Side * (p - e.Entry) / Pip);
                if (e.TTarget == 0 && !double.IsNaN(e.Target) && -e.Side * (p - e.Target) >= 0) Mark(e, ref e.TTarget, t, "TARGET");
                if (e.TCancel == 0 && e.Side * (p - cancel) >= 0) Mark(e, ref e.TCancel, t, "CANCEL");
                double edge = e.Side > 0 ? e.ZoneLo : e.ZoneHi;
                if (!away && -e.Side * (p - edge) >= ReturnPips * Pip) away = true;
                if (away && e.TReturn == 0 && e.Side * (p - edge) >= 0) Mark(e, ref e.TReturn, t, "RETURN");
                if (e.ZoneKeys.Contains(Key(p))) closedSum += -e.Side * a.D[k];   // встречные минус попутные на ценах зоны
                if (e.TClose50 == 0 && closedSum >= 0.5 * e.Zone) Mark(e, ref e.TClose50, t, "CLOSE50");
                if (e.TClose100 == 0 && closedSum >= e.Zone) Mark(e, ref e.TClose100, t, "CLOSE100");
            }
        }

        // Сделка против толпы. Стоп (mode): H0 — хай (лоу) за час до начала роста; FRACTAL — ближайший фрактал часовика
        // за H0 (В1 01:15:41); DAY — хай (лоу) торгового дня с 17:00 Чикаго, не ближе H0 (В4 03:48–04:07). Всё + 1 тик.
        // add — сценарий B (В1 01:16:15–01:18:51): цена ушла против нас на AddPips, а встречных сделок с входа не больше,
        // чем сделок толпы, — добавляем 1 контракт; тейк обоих — возврат к краю зоны со стороны цены.
        const double AddPips = 10;
        static readonly TimeSpan FastSet = TimeSpan.FromMinutes(35), FastHold = TimeSpan.FromHours(24);
        static void Trade(Tape a, List<LraBar> hours, Ev e, string mode, bool add)
        {
            e.Skip = e.Reason = ""; e.Pips = 0; e.TExit = 0; e.Added = false;
            int i = Array.BinarySearch(a.T, e.TFirst); if (i < 0) i = ~i;
            if (i >= a.N) { e.Skip = "NO_DATA"; return; }
            e.Entry = a.P[i]; e.TEntry = a.T[i];
            // SWEEP — вместе с толпой после выноса её стопов (вариант пользователя, 2026-10-28): цена дошла до уровня стопов
            // толпы (цель), потом вернулась к краю зоны набора — вход; стоп за крайней ценой выноса + 1 тик; выход через 24 ч.
            // Выноса и возврата ждём FastHold. SWEEP — только быстрый набор (FastSet), SWEEP_ALL — все события.
            if (mode.StartsWith("SWEEP"))
            {
                if (mode == "SWEEP" && e.TFirst - e.TS > FastSet.Ticks) { e.Skip = "SLOW"; return; }
                if (double.IsNaN(e.Target)) { e.Skip = "NO_LEVEL"; return; }
                int d = e.Side, j = -1;
                double ext = double.NaN, near = d > 0 ? e.ZoneLo : e.ZoneHi;
                for (int k = i; k < a.N && a.T[k] <= e.TFirst + FastHold.Ticks; k++)
                {
                    if (double.IsNaN(ext)) { if (-d * (a.P[k] - e.Target) >= 0) ext = a.P[k]; continue; }
                    if (-d * (a.P[k] - ext) > 0) ext = a.P[k];
                    if (d * (a.P[k] - near) >= 0) { j = k; break; }
                }
                if (j < 0) { e.Skip = double.IsNaN(ext) ? "NO_SWEEP" : "NO_RETURN"; return; }
                e.Entry = a.P[j]; e.TEntry = a.T[j]; e.SL = ext - d * Tick; e.TP = double.NaN;
                if (Math.Abs(e.SL - e.Entry) / Pip > MaxStopPips) { e.Skip = "FAR_STOP"; return; }
                e.Reason = "TIME"; int lastK = j;
                for (int k = j; k < a.N && a.T[k] <= e.TEntry + FastHold.Ticks; k++)
                {
                    lastK = k;
                    if (d * (a.P[k] - e.SL) <= 0) { e.Reason = "SL"; e.Exit = e.SL; e.TExit = a.T[k]; break; }
                }
                if (e.Reason == "TIME") { e.Exit = a.P[lastK]; e.TExit = a.T[lastK]; }
                e.Pips = d * (e.Exit - e.Entry) / Pip;
                return;
            }
            // WITH — вместе с толпой: стоп за уровнем стопов толпы (цель сделки против неё), тейк 15 п.
            // FAST — только быстрый набор (событие не позже FastSet от начала роста), вместе с толпой, без стопа и тейка,
            // выход через 24 ч; FAST_SL — то же со стопом за уровнем стопов толпы (tz-ticks.md, поиск закономерностей).
            bool fast = mode.StartsWith("FAST");
            if (fast && e.TFirst - e.TS > FastSet.Ticks) { e.Skip = "SLOW"; return; }
            int dir = mode == "WITH" || fast ? e.Side : -e.Side;
            if (mode == "FAST") e.SL = e.Entry - dir * 1.0;   // недостижимо
            else if (mode == "WITH" || mode == "FAST_SL") e.SL = e.Target;
            else if (mode == "H0") e.SL = e.H0 + e.Side * Tick;
            else if (mode == "FRACTAL")
                e.SL = LraLevels.Nearest(hours.Where(b => b.Time.Ticks <= e.TFirst).ToList(), e.Side, e.H0,
                    new LraSettings { Tick = Tick, FractalBars = 2 });
            else
            {
                DateTime t0 = new DateTime(e.TFirst), day = t0.Date.AddHours(17);
                if (day > t0) day = day.AddDays(-1);
                double ext = e.H0;
                for (int k = i - 1; k >= 0 && a.T[k] >= day.Ticks; k--) if (e.Side * a.P[k] > e.Side * ext) ext = a.P[k];
                e.SL = ext + e.Side * Tick;
            }
            if (double.IsNaN(e.SL)) { e.Skip = "NO_STOP"; return; }
            if (mode != "FAST" && double.IsNaN(e.Target)) { e.Skip = "NO_LEVEL"; return; }
            e.TP = fast ? e.Entry + dir * 1.0 : mode == "WITH" || Math.Abs(e.Target - e.Entry) / Pip > MaxTakePips ? e.Entry + dir * MaxTakePips * Pip : e.Target;
            if (dir * (e.TP - e.Entry) <= 0) e.Skip = "TP_PASSED";
            else if (dir * (e.Entry - e.SL) <= 0) e.Skip = "SL_PASSED";
            else if (mode != "FAST" && Math.Abs(e.SL - e.Entry) / Pip > MaxStopPips) e.Skip = "FAR_STOP";
            if (e.Skip != "") return;
            e.Reason = "TIME";
            double tp = e.TP, addAt = e.Entry - dir * AddPips * Pip, edge = e.Side > 0 ? e.ZoneHi : e.ZoneLo;
            int flow = 0, last = i;   // сделки толпы минус встречные с момента входа
            for (int k = i; k < a.N && a.T[k] <= e.TFirst + (fast ? FastHold : Hold).Ticks; k++)
            {
                last = k; flow += e.Side * a.D[k];
                if (dir * (a.P[k] - e.SL) <= 0) { e.Reason = "SL"; e.Exit = e.SL; e.TExit = a.T[k]; break; }
                if (dir * (a.P[k] - tp) >= 0) { e.Reason = "TP"; e.Exit = tp; e.TExit = a.T[k]; break; }
                if (add && !e.Added && dir * (a.P[k] - addAt) <= 0 && flow >= 0 && dir * (addAt - e.SL) > 0)
                { e.Added = true; tp = dir * (edge - addAt) > 0 ? edge : e.Entry; }
            }
            if (e.Reason == "TIME") { e.Exit = a.P[last]; e.TExit = a.T[last]; }
            e.Pips = dir * (e.Exit - e.Entry) / Pip + (e.Added ? dir * (e.Exit - addAt) / Pip : 0);
        }

        static void Mark(Ev e, ref long field, long t, string name) { field = t; if (e.First == "") e.First = name; }

        // Одна позиция на символ: событие во время открытой сделки — пропуск
        static void Simulate(List<Ev> evs)
        {
            long busy = 0;
            foreach (Ev e in evs)
            {
                if (e.Skip != "") continue;
                if (e.TEntry < busy) { e.Skip = "IN_POSITION"; continue; }
                busy = e.TExit;
            }
        }

        // ---------- вывод ----------
        static string Ts(long t) { return t == 0 ? "" : new DateTime(t).ToString("yyyy-MM-dd HH:mm", CI); }
        static string Paris(long t) { return t == 0 ? "" : TimeZoneInfo.ConvertTime(new DateTime(t), Chi, Par).ToString("yyyy-MM-dd HH:mm", CI); }
        static string Hrs(long from, long t) { return t == 0 ? "" : ((t - from) / (double)TimeSpan.TicksPerHour).ToString("0.0", CI); }

        static void Write(string sym, string v, List<Ev> evs)
        {
            var w = new StringWriter();
            w.WriteLine("Time,ParisTime,Crowd,GrowthStart,LastSeen,Trades,Weight,SmallShare,Prices,ZoneLo,ZoneHi,ZoneImb,H0,Entry,Target," +
                        "First,H_Target,H_Cancel,H_Return,H_Close50,H_Close100,MaxFav,MaxAdv,Skip,SL,TP,Exit,Reason,Pips,EntryParis,ExitParis");
            foreach (Ev e in evs)
                w.WriteLine(string.Format(CI, "{0},{1},{2},{3},{4},{5},{6:0.000},{7:0.000},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21:0.0},{22:0.0},{23},{24},{25},{26},{27},{28},{29},{30}",
                    Ts(e.TFirst), Paris(e.TFirst), e.Side > 0 ? "BUY" : "SELL", Ts(e.TS), Ts(e.TLast), e.Trades, e.Weight,
                    (double)e.Small / Math.Max(1, e.Trades), e.Prices, e.ZoneLo, e.ZoneHi, e.Zone, e.H0, e.Entry,
                    double.IsNaN(e.Target) ? "" : e.Target.ToString(CI), e.First,
                    Hrs(e.TFirst, e.TTarget), Hrs(e.TFirst, e.TCancel), Hrs(e.TFirst, e.TReturn), Hrs(e.TFirst, e.TClose50), Hrs(e.TFirst, e.TClose100),
                    e.MaxFav, e.MaxAdv, e.Skip, e.Skip == "" ? e.SL.ToString(CI) : "", e.Skip == "" ? e.TP.ToString(CI) : "",
                    e.Skip == "" ? e.Exit.ToString(CI) : "", e.Reason + (e.Added ? "+ADD" : ""),e.Skip == "" ? e.Pips.ToString("0.0", CI) : "", e.Skip == "" ? Paris(e.TEntry) : "", e.Skip == "" ? Paris(e.TExit) : ""));
            File.WriteAllText(Path.Combine(OutDir, "events_" + sym + "_" + v + ".csv"), w.ToString());
        }

        static void Summary(StringWriter o, string sym, string v, Tape a, List<Ev> evs)
        {
            o.WriteLine("==== {0}, вариант {1} ({2}): сделок в ленте {3}, событий {4} (толпа покупает {5}, продаёт {6}), дней с событиями {7}",
                sym, v, v == "F" ? "каждое исполнение" : "группы исполнений", a.N, evs.Count, evs.Count(e => e.Side > 0), evs.Count(e => e.Side < 0),
                evs.Select(e => new DateTime(e.TFirst).Date).Distinct().Count());
            foreach (int h in Horizons)
            {
                long lim = h * TimeSpan.TicksPerHour;
                Func<Func<Ev, long>, string> pct = f => (100.0 * evs.Count(e => f(e) > 0 && f(e) - e.TFirst <= lim) / Math.Max(1, evs.Count)).ToString("0", CI) + "%";
                o.WriteLine("  за {0,2} ч: цель (стопы толпы) {1}, отмена {2}, возврат к зоне {3}, перекос зоны закрыт на 50% {4}, на 100% {5}",
                    h, pct(e => e.TTarget), pct(e => e.TCancel), pct(e => e.TReturn), pct(e => e.TClose50), pct(e => e.TClose100));
            }
            o.WriteLine("  что первым за 48 ч: " + string.Join(", ", evs.GroupBy(e => e.First == "" ? "ничего" : e.First)
                .OrderByDescending(g => g.Count()).Select(g => g.Key + " " + g.Count())));
        }

        static void TradeSummary(StringWriter o, string name, List<Ev> evs)
        {
            var tr = evs.Where(e => e.Skip == "").ToList();
            double eq = 0, peak = 0, dd = 0;
            foreach (Ev e in tr) { eq += e.Pips; peak = Math.Max(peak, eq); dd = Math.Max(dd, peak - eq); }
            o.WriteLine("  стоп {0}: сделки {1} (с добором {2}), плюс {3}, минус {4}, итог {5:0} п., на сделку {6:0.0}, просадка {7:0} п., стоп в среднем {8:0.0} п.; выходы: {9}; пропуски: {10}",
                name, tr.Count, tr.Count(e => e.Added), tr.Count(e => e.Pips > 0), tr.Count(e => e.Pips < 0), tr.Sum(e => e.Pips),
                tr.Count > 0 ? tr.Average(e => e.Pips) : 0, dd, tr.Count > 0 ? tr.Average(e => Math.Min(Math.Abs(e.SL - e.Entry) / Pip, 999)) : 0,
                string.Join(", ", tr.GroupBy(e => e.Reason).Select(g => g.Key + " " + g.Count())),
                string.Join(", ", evs.Where(e => e.Skip != "").GroupBy(e => e.Skip).Select(g => g.Key + " " + g.Count())));
        }
    }
}
