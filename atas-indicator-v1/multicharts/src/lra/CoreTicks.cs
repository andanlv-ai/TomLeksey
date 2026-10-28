// Ядро анализа тиков (tz-ticks.md, шаги 2–3): события «толпа набирает» по ленте сделок <символ>_TRADES.csv
// и модели сделок. Общее для прогона src/lra/ticks/Ticks.cs и советника LraTrader (Algo = FAST_TP).
// Описано для «толпа покупает» (d = +1); «продаёт» (d = -1) — зеркально через d.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace PowerLanguage.Strategy
{
    public class LraTicks
    {
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;
        public readonly double Tick;   // цена тика инструмента
        public LraTicks(double tick) { Tick = tick; }

        // Пороги шага 2 (ТЗ, по умолчанию)
        public static readonly TimeSpan Lookback = TimeSpan.FromHours(12), MinGrowth = TimeSpan.FromMinutes(30),
            PreHour = TimeSpan.FromHours(1), Step = TimeSpan.FromMinutes(5);
        public const double W = 0.2, PriceShare = 0.6, MaxOnePrice = 0.5, VolConflict = 0.05;
        public const int MinPrices = 3, MinTradesAtPrice = 20, SmallSize = 3;
        // Модель сделки (шаг 3)
        public const double MaxTakePips = 15, MaxStopPips = 30, ReturnPips = 10, Pip = 0.0001;
        public static readonly TimeSpan Hold = TimeSpan.FromHours(48);

        // Сделки одного варианта: время (Ticks), цена, контракты, направление
        public class Tape { public long[] T; public double[] P; public int[] S; public sbyte[] D; public int N; }

        public class Ev
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


        // ---------- загрузка ----------
        public static Tape Load(string path)
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
        public static Tape Group(Tape a)
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

        public long Key(double price) { return (long)Math.Round(price / Tick); }

        // ---------- шаг 2 ----------
        public List<Ev> Detect(Tape a, List<LraBar> hours)
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
        public static bool VolAgainst(List<LraBar> hours, long s, long t, int d)
        {
            double ask = 0, bid = 0;
            foreach (LraBar b in hours)
                if (b.Time.Ticks - TimeSpan.TicksPerHour >= s && b.Time.Ticks <= t) { ask += b.Ask; bid += b.Bid; }
            return ask + bid > 0 && -d * (ask - bid) / (ask + bid) >= VolConflict;
        }

        // ---------- шаг 3: что было дальше ----------
        public void Follow(Tape a, List<LraBar> hours, Ev e)
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
        public const double AddPips = 10;
        public static readonly TimeSpan FastSet = TimeSpan.FromMinutes(35), FastHold = TimeSpan.FromHours(24);
        public void Trade(Tape a, List<LraBar> hours, Ev e, string mode, bool add)
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
            // выход через 24 ч; FAST_SL — то же со стопом за уровнем стопов толпы (tz-ticks.md, поиск закономерностей);
            // FAST_TP — FAST_SL + тейк на ближайшем фрактале часовика по ходу сделки (за входом).
            bool fast = mode.StartsWith("FAST");
            if (fast && e.TFirst - e.TS > FastSet.Ticks) { e.Skip = "SLOW"; return; }
            int dir = mode == "WITH" || fast ? e.Side : -e.Side;
            if (mode == "FAST") e.SL = e.Entry - dir * 1.0;   // недостижимо
            else if (mode == "WITH" || mode == "FAST_SL" || mode == "FAST_TP") e.SL = e.Target;
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
            if (mode == "FAST_TP")
            {
                e.TP = LraLevels.Nearest(hours.Where(b => b.Time.Ticks <= e.TFirst).ToList(), dir, e.Entry,
                    new LraSettings { Tick = Tick, FractalBars = 2 });
                if (double.IsNaN(e.TP)) { e.Skip = "NO_TP"; return; }
            }
            else e.TP = fast ? e.Entry + dir * 1.0 : mode == "WITH" || Math.Abs(e.Target - e.Entry) / Pip > MaxTakePips ? e.Entry + dir * MaxTakePips * Pip : e.Target;
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
            // лента кончилась раньше срока выхода — сделка ещё открыта; Pips — по последней цене
            if (e.Reason == "TIME" && a.T[a.N - 1] < e.TFirst + (fast ? FastHold : Hold).Ticks) e.Reason = "OPEN";
            e.Pips = dir * (e.Exit - e.Entry) / Pip + (e.Added ? dir * (e.Exit - addAt) / Pip : 0);
        }

        public static void Mark(Ev e, ref long field, long t, string name) { field = t; if (e.First == "") e.First = name; }

        // Одна позиция на символ: событие во время открытой сделки — пропуск
        public static void Simulate(List<Ev> evs)
        {
            long busy = 0;
            foreach (Ev e in evs)
            {
                if (e.Skip != "") continue;
                if (e.TEntry < busy) { e.Skip = "IN_POSITION"; continue; }
                busy = e.TExit;
            }
        }

        // Советник LraTrader, Algo = FAST_TP: события по ленте сделок (вариант F — каждое исполнение) и сделка FAST_TP
        // (tz-ticks.md, шаг 3): быстрый набор — вместе с толпой, стоп за уровнем стопов толпы, тейк на ближайшем фрактале.
        public List<Ev> FastTp(Tape a, List<LraBar> hours)
        {
            if (a.N == 0) return new List<Ev>();
            List<Ev> evs = Detect(a, hours);
            foreach (Ev e in evs) { Follow(a, hours, e); Trade(a, hours, e, "FAST_TP", false); }
            Simulate(evs);
            return evs;
        }

    }
}
