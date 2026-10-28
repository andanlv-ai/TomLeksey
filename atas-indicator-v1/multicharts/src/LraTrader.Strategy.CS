// LraTrader.Strategy.CS — СОБРАНО tools/bundle.sh из src/lra/*.cs. Не править руками.
using PowerLanguage.Function;
using PowerLanguage.VolumeProfile;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System;

// ---- CoreCsv.cs ----

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

// ---- CoreImbalance.cs ----

namespace PowerLanguage.Strategy
{
    // Дисбаланс окна и итоги дня (ТЗ §3).
    public static class LraImb
    {
        // Дисбаланс окна h[from..to] включительно; доли — от итогов дня бара h[to].
        public static LraImbalance Measure(IList<LraBar> h, int from, int to, LraSettings s)
        {
            var r = new LraImbalance();
            if (from < 0 || to >= h.Count || from > to) { r.Reason = "NO_DATA"; return r; }

            var lv = new Dictionary<long, double[]>(); // ключ — цена в тиках, значение {Ask, Bid}
            bool noData = false;
            for (int i = from; i <= to; i++)
            {
                LraBar b = h[i];
                if (b.NoData) noData = true;
                r.VolDelta += b.Ask - b.Bid;
                r.TickDelta += b.UpTicks - b.DownTicks;
                for (int k = 0; k < b.LvPrice.Length; k++)
                {
                    long key = (long)Math.Round(b.LvPrice[k] / s.Tick);
                    double[] ab;
                    if (!lv.TryGetValue(key, out ab)) { ab = new double[2]; lv[key] = ab; }
                    ab[0] += b.LvAsk[k];
                    ab[1] += b.LvBid[k];
                }
            }
            if (noData) { r.Reason = "NO_DATA"; return r; }

            double dayVol, dayTicks;
            DayTotals(h, to, out dayVol, out dayTicks);
            r.VolShare = dayVol > 0 ? r.VolDelta / dayVol : 0;
            r.TickShare = dayTicks > 0 ? r.TickDelta / dayTicks : 0;

            foreach (double[] ab in lv.Values)
            {
                double sum = ab[0] + ab[1];
                if (sum <= 0) continue;
                double imb = (ab[0] - ab[1]) / sum;
                if (imb >= s.LevelImbPct) r.BuyLevels++;
                else if (imb <= -s.LevelImbPct) r.SellLevels++;
            }

            if (Math.Abs(r.VolShare) >= s.MinShare && r.VolShare * r.TickShare < 0) { r.Reason = "CONFLICT"; return r; }
            if (Math.Abs(r.TickShare) < s.MinShare) { r.Reason = "WEAK"; return r; }
            if (r.TickShare > 0 && r.BuyLevels >= s.MinLevels) r.Dir = 1;
            else if (r.TickShare < 0 && r.SellLevels >= s.MinLevels) r.Dir = -1;
            else r.Reason = "ONE_PRICE";
            return r;
        }

        // Толпа по кумулятивной дельте по сделкам (автор: В2 19:17, 22:05; В4 02:28).
        // Начало роста — минимум (толпа покупает) или максимум (продаёт) кумулятивной дельты за CumLookback баров,
        // не ближе CumMinBars баров к текущему. Толпа есть, если:
        // рост / объём с начала роста >= CumWeight; дельта по объёму за то же время не против (CONFLICT, как в Measure);
        // в сторону толпы перекошено >= MinLevels цен; цена за время роста не вышла за хай (лоу) бара начала роста.
        // Из двух сторон берётся та, у которой вес больше. Возвращает дисбаланс окна from..last с Dir толпы или null.
        public static LraImbalance Cum(IList<LraBar> h, LraSettings s, out int from)
        {
            from = -1;
            int last = h.Count - 1;
            int b0 = Math.Max(0, last - s.CumLookback);   // база: кумулятивная дельта здесь = 0
            if (last - b0 < s.CumMinBars) return null;
            var c = new double[last - b0 + 1];
            for (int k = b0 + 1; k <= last; k++)
            {
                if (h[k].NoData) return null;
                c[k - b0] = c[k - b0 - 1] + h[k].UpTicks - h[k].DownTicks;
            }
            int mn = b0, mx = b0;
            for (int k = b0; k <= last - s.CumMinBars; k++)
            {
                if (c[k - b0] < c[mn - b0]) mn = k;
                if (c[k - b0] > c[mx - b0]) mx = k;
            }

            LraImbalance best = null; double bestW = 0;
            foreach (int d in new[] { 1, -1 })
            {
                int m = d > 0 ? mn : mx;
                double g = c[last - b0] - c[m - b0], vol = 0, vd = 0;
                for (int k = m + 1; k <= last; k++) { vol += h[k].UpTicks + h[k].DownTicks; vd += h[k].Ask - h[k].Bid; }
                if (vol <= 0 || g * d <= 0) continue;
                double w = Math.Abs(g) / vol;
                if (w < s.CumWeight || w <= bestW) continue;
                if (-d * vd / vol >= s.MinShare) continue;
                bool broke = false;
                for (int k = m + 1; k <= last; k++) if (d > 0 ? h[k].High > h[m].High : h[k].Low < h[m].Low) broke = true;
                if (broke) continue;
                LraImbalance r = Measure(h, m + 1, last, s);
                if ((d > 0 ? r.BuyLevels : r.SellLevels) < s.MinLevels) continue;
                r.Dir = d; r.Reason = "";
                best = r; bestW = w; from = m + 1;
            }
            return best;
        }

        // Объём и число сделок (UpTicks + DownTicks) торгового дня бара h[idx] (дата Paris), от начала дня до idx включительно.
        public static void DayTotals(IList<LraBar> h, int idx, out double volume, out double ticks)
        {
            volume = 0; ticks = 0;
            DateTime day = h[idx].Paris.Date;
            for (int i = idx; i >= 0 && h[i].Paris.Date == day; i--)
            {
                volume += h[i].Volume;
                ticks += h[i].UpTicks + h[i].DownTicks;
            }
        }
    }
}

// ---- CoreLevels.cs ----

namespace PowerLanguage.Strategy
{
    // Уровни стопов и ATR дня (ТЗ §5).
    public static class LraLevels
    {
        // Ближайший по времени фрактал за ценой: side +1 — верхний выше price (+ отступ), -1 — нижний ниже (- отступ). Нет — NaN.
        // Фрактал — бар, чей хай выше (лоу ниже) FractalBars соседей с каждой стороны.
        public static double Nearest(IList<LraBar> h, int side, double price, LraSettings s)
        {
            // соседи i±FractalBars должны существовать; глубина — не дальше SwingLookback баров от текущего
            int w = Math.Max(1, s.FractalBars);
            int last = Math.Max(w, h.Count - 1 - s.SwingLookback);
            for (int i = h.Count - 1 - w; i >= last; i--)
            {
                if (side > 0)
                {
                    if (h[i].High > price && Fractal(h, i, w, 1))
                        return h[i].High + s.OffsetTicks * s.Tick;
                }
                else if (h[i].Low < price && Fractal(h, i, w, -1))
                    return h[i].Low - s.OffsetTicks * s.Tick;
            }
            return double.NaN;
        }

        static bool Fractal(IList<LraBar> h, int i, int w, int side)
        {
            for (int k = 1; k <= w; k++)
                if (side > 0 ? h[i].High <= h[i - k].High || h[i].High <= h[i + k].High
                             : h[i].Low >= h[i - k].Low || h[i].Low >= h[i + k].Low) return false;
            return true;
        }

        // Средний размах торговых дней (дата Paris) за AtrPeriod последних полных дней; текущий день не считается. Меньше 3 дней — NaN.
        // Возвращает цену (не пипсы): пипсы = результат / PipSize.
        public static double DayAtr(IList<LraBar> h, LraSettings s)
        {
            if (h.Count == 0) return double.NaN;
            DateTime cur = h[h.Count - 1].Paris.Date;
            var ranges = h.Where(b => b.Paris.Date < cur)
                          .GroupBy(b => b.Paris.Date).OrderBy(g => g.Key)
                          .Select(g => g.Max(b => b.High) - g.Min(b => b.Low)).ToList();
            if (ranges.Count < 3) return double.NaN;
            return ranges.Skip(Math.Max(0, ranges.Count - s.AtrPeriod)).Average();
        }
    }
}

// ---- CorePositions.cs ----

namespace PowerLanguage.Strategy
{
    // Виртуальные позиции: открытие, выходы, C, B (ТЗ §7).
    public static class LraPos
    {
        // Позиция из сигнала: часть 1 с TP и часть 2 с TP2, если TP2 не NaN. Вход по sig.Entry на баре h[h.Count-1].
        public static LraPosition Open(LraSignal sig, IList<LraBar> h, string signalId)
        {
            int idx = h.Count - 1;
            var p = new LraPosition
            {
                Book = sig.Book, SignalId = signalId, Dir = sig.Dir, WinFrom = sig.WinFrom, EntryIdx = idx, SL = sig.SL
            };
            p.Units.Add(NewUnit(1, h, sig.Entry, sig.TP));
            if (!double.IsNaN(sig.TP2)) p.Units.Add(NewUnit(2, h, sig.Entry, sig.TP2));
            return p;
        }

        static LraUnit NewUnit(int n, IList<LraBar> h, double entry, double tp)
        {
            int idx = h.Count - 1;
            return new LraUnit { N = n, OpenIdx = idx, OpenTime = h[idx].Time, Entry = entry, TP = tp };
        }

        // Сопровождение на текущем баре (ТЗ §7). Закрытые части убирает из p.Units и возвращает; может добавить часть (B).
        public static List<LraExit> OnBar(IList<LraBar> h, LraPosition p, double atr, LraSettings s)
        {
            var exits = new List<LraExit>();
            int last = h.Count - 1;
            if (last <= p.EntryIdx || p.Units.Count == 0) return exits;
            LraBar b = h[last];
            bool up = p.Dir > 0;

            // 1. Выход по уровню
            bool slHit = up ? b.Low <= p.SL : b.High >= p.SL;
            if (slHit)
            {
                foreach (LraUnit u in p.Units)
                    exits.Add(new LraExit { Unit = u, Price = p.SL, Reason = "SL", Ambiguous = up ? b.High >= u.TP : b.Low <= u.TP });
                p.Units.Clear();
                return exits;
            }
            foreach (LraUnit u in p.Units.ToArray())
                if (up ? b.High >= u.TP : b.Low <= u.TP)
                {
                    exits.Add(new LraExit { Unit = u, Price = u.TP, Reason = "TP" });
                    p.Units.Remove(u);
                }
            if (p.Units.Count == 0) return exits;

            // 2. C — дисбаланс исчез (только AGAINST)
            if (p.Book == "AGAINST")
            {
                LraImbalance imb = LraImb.Measure(h, p.WinFrom, last, s);
                LraUnit first = p.Units.Find(u => u.N == 1) ?? p.Units[0];
                if ((imb.Dir == 0 || imb.Dir == p.Dir) && Math.Abs(b.Close - first.Entry) < s.StallAtr * atr)
                {
                    foreach (LraUnit u in p.Units) exits.Add(new LraExit { Unit = u, Price = b.Close, Reason = "DISSOLVED" });
                    p.Units.Clear();
                    return exits;
                }
            }

            // B — добор и ADVERSE_FLOW (только AGAINST, на этом баре выходов не было)
            if (p.Book == "AGAINST" && exits.Count == 0)
            {
                LraUnit u1 = p.Units.Find(u => u.N == 1) ?? p.Units[0];
                double adverse = up ? u1.Entry - b.Close : b.Close - u1.Entry; // >0 — цена ушла против нас
                LraImbalance after = LraImb.Measure(h, p.EntryIdx + 1, last, s);
                if (adverse > 0 && after.Dir == p.Dir)
                {
                    foreach (LraUnit u in p.Units) exits.Add(new LraExit { Unit = u, Price = b.Close, Reason = "ADVERSE_FLOW" });
                    p.Units.Clear();
                }
                else if (p.Units.Count == 1 && p.Units[0].N == 1 && adverse / s.PipSize >= s.AddStepPips - 1e-9 && after.Dir != p.Dir)
                {
                    if (s.AddTargetMode == "ENTRY1") u1.TP = u1.Entry;
                    p.Units.Add(new LraUnit { N = 2, OpenIdx = last, OpenTime = b.Time, Entry = b.Close, TP = u1.TP });
                }
            }
            return exits;
        }
    }
}

// ---- CoreRanges.cs ----

namespace PowerLanguage.Strategy
{
    // Диапазоны ЛРА (ТЗ §4).
    public static class LraRanges
    {
        // Вызывать на каждом закрытом баре. Отмечает Done/Expired у прежних диапазонов (кладёт их в changed),
        // находит новый диапазон (добавляет в ranges и в changed, возвращает его) или возвращает null.
        public static LraRange Update(IList<LraBar> h, List<LraRange> ranges, LraSettings s, List<LraRange> changed)
        {
            int last = h.Count - 1;
            LraBar cur = h[last];

            // 1. Прежние активные: Done / Expired
            foreach (LraRange r in ranges)
            {
                if (!r.Active) continue;
                if (last > r.BornIdx && cur.Low <= r.Poc && r.Poc <= cur.High) r.Done = true;
                else if (last - r.BornIdx > s.MaxRangeAgeBars) r.Expired = true;
                else continue;
                changed.Add(r);
            }

            // 2. Кандидат из баров до текущего: наибольшее окно, заканчивающееся на last-1
            int end = last - 1;
            if (end < 0) return null;
            double hi = h[end].High, lo = h[end].Low;
            if ((hi - lo) / s.PipSize > s.MaxRangePips + 1e-9) return null;
            int start = end;
            while (start > 0)
            {
                double nh = Math.Max(hi, h[start - 1].High), nl = Math.Min(lo, h[start - 1].Low);
                if ((nh - nl) / s.PipSize > s.MaxRangePips + 1e-9) break;
                hi = nh; lo = nl; start--;
            }
            int n = end - start + 1;
            if (n < s.MinRangeBars) return null;
            int active = 0;
            for (int i = start; i <= end; i++)
                if (h[i].Session == "EUROPE" || h[i].Session == "AMERICA") active++;
            if (active < s.MinActiveBars) return null;

            int dir = cur.Close > hi ? 1 : cur.Close < lo ? -1 : 0;
            if (dir == 0) return null;
            foreach (LraRange r in ranges)
                if (r.Active && lo <= r.High && hi >= r.Low) return null;

            // Poc: цена с наибольшей суммой Ask+Bid по уровням всех баров окна
            var vol = new Dictionary<long, double>();
            var price = new Dictionary<long, double>();
            double total = 0;
            for (int i = start; i <= end; i++)
            {
                LraBar b = h[i];
                total += b.Volume;
                for (int k = 0; k < b.LvPrice.Length; k++)
                {
                    long key = (long)Math.Round(b.LvPrice[k] / s.Tick);
                    double old;
                    vol.TryGetValue(key, out old);
                    vol[key] = old + b.LvAsk[k] + b.LvBid[k];
                    if (!price.ContainsKey(key)) price[key] = b.LvPrice[k];
                }
            }
            double poc = (hi + lo) / 2, best = double.NegativeInfinity;
            foreach (var kv in vol)
                if (kv.Value > best || (kv.Value == best && price[kv.Key] < poc))
                { best = kv.Value; poc = price[kv.Key]; }

            var nr = new LraRange
            {
                StartIdx = start, EndIdx = end, BornIdx = last,
                Start = h[start].Time, End = h[end].Time,
                High = hi, Low = lo, Poc = poc, Volume = total, Bars = n, ExitDir = dir,
                Id = h[start].Time.ToString("yyyyMMddHHmm", System.Globalization.CultureInfo.InvariantCulture)
            };
            ranges.Add(nr);
            changed.Add(nr);
            return nr;
        }

        // Активный диапазон с минимальным |price - Poc| или null.
        public static LraRange NearestActive(List<LraRange> ranges, double price)
        {
            LraRange best = null;
            foreach (LraRange r in ranges)
                if (r.Active && (best == null || Math.Abs(price - r.Poc) < Math.Abs(price - best.Poc))) best = r;
            return best;
        }
    }
}

// ---- CoreScenarios.cs ----

namespace PowerLanguage.Strategy
{
    // Сценарии входа A и D с фильтром ЛРА (ТЗ §6).
    public static class LraScen
    {
        // Сигналы на текущем баре h[h.Count-1]: 0-2 штуки (AGAINST, MOMENTUM). Skip ставит только NO_LEVEL / AGAINST_LRA.
        public static List<LraSignal> Evaluate(IList<LraBar> h, double atr, double dayAtr, List<LraRange> ranges, LraSettings s)
        {
            var res = new List<LraSignal>();
            int last = h.Count - 1;
            double entry = h[last].Close;
            int winFrom;
            LraImbalance crowd;
            bool a, d;
            if (s.CrowdMode == "CUM")
            {
                // цена уже проверена в Cum: не вышла за хай/лоу начала роста. Сценарий D — только в режиме WINDOW.
                crowd = LraImb.Cum(h, s, out winFrom);
                if (crowd == null) return res;
                a = true; d = false;
            }
            else
            {
                if (last - s.ImbBars < 0) return res;
                winFrom = h.Count - s.ImbBars;
                crowd = LraImb.Measure(h, winFrom, last, s);
                if (crowd.Dir == 0) return res;
                double move = entry - h[last - s.ImbBars].Close;
                bool strong = Math.Abs(move) >= s.StrongAtr * atr;
                a = move * crowd.Dir <= s.StallAtr * atr && !strong; // сильное движение — только D
                d = strong && Math.Sign(move) == -crowd.Dir;
                if (!a && !d) return res;
            }

            // Направление, SL, TP, фильтр ЛРА — общие для A и D.
            int dir = -crowd.Dir;
            double pip = s.PipSize;
            double sl = LraLevels.Nearest(h, -dir, entry, s);
            double tp = LraLevels.Nearest(h, dir, entry, s);
            string skip = "";
            if (double.IsNaN(sl) || double.IsNaN(tp) || Math.Abs(sl - entry) / pip > s.MaxStopPips) skip = "NO_LEVEL";
            else if (Math.Abs(tp - entry) / pip > s.MaxTakePips) tp = entry + dir * s.MaxTakePips * pip;

            LraRange range = LraRanges.NearestActive(ranges, entry);
            double dist = double.NaN, tp2 = double.NaN;
            if (range != null)
            {
                dist = Math.Abs(entry - range.Poc) / pip;
                double toPoc = dir * (range.Poc - entry); // > 0: сделка идёт к Poc
                if (skip == "" && toPoc < 0) skip = "AGAINST_LRA";
                else if (skip == "" && toPoc > 0 && dist >= s.LraMinDistPips
                         && !double.IsNaN(dayAtr) && dist <= s.DayTargetShare * dayAtr / pip) tp2 = range.Poc;
            }

            foreach (string book in new[] { "AGAINST", "MOMENTUM" })
            {
                if (book == "AGAINST" ? !a : !d) continue;
                res.Add(new LraSignal
                {
                    Book = book, Dir = dir, Entry = entry, SL = sl, TP = tp, TP2 = tp2,
                    WinFrom = winFrom, Crowd = crowd, Range = range, RangeDistPips = dist, Skip = skip
                });
            }
            return res;
        }
    }
}

// ---- CoreTicks.cs ----
// Ядро анализа тиков (tz-ticks.md, шаги 2–3): события «толпа набирает» по ленте сделок <символ>_TRADES.csv
// и модели сделок. Общее для прогона src/lra/ticks/Ticks.cs и советника LraTrader (Algo = FAST_TP).
// Описано для «толпа покупает» (d = +1); «продаёт» (d = -1) — зеркально через d.

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
        public static Tape Load(string path) { return new TapeReader(path).Read(); }

        // Чтение ленты с дочитыванием: каждый Read() добавляет строки, дописанные в файл после прошлого чтения.
        // Строка без перевода строки в конце файла ещё дописывается — ждёт следующего Read().
        public class TapeReader
        {
            readonly string _path; long _pos; string _rest = "";
            readonly List<long> _t = new List<long>(); readonly List<double> _p = new List<double>();
            readonly List<int> _s = new List<int>(); readonly List<sbyte> _d = new List<sbyte>();
            public TapeReader(string path) { _path = path; }
            public Tape Read()
            {
                string chunk;
                // TickRecorder держит файл открытым на запись — читать с FileShare.ReadWrite
                using (var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    fs.Seek(_pos, SeekOrigin.Begin);
                    var buf = new byte[fs.Length - _pos];
                    int n = 0; for (int r; n < buf.Length && (r = fs.Read(buf, n, buf.Length - n)) > 0;) n += r;
                    _pos += n; chunk = System.Text.Encoding.ASCII.GetString(buf, 0, n);
                }
                string[] lines = (_rest + chunk).Split('\n');
                _rest = lines[lines.Length - 1];
                for (int i = 0; i < lines.Length - 1; i++)
                {
                    var f = lines[i].TrimEnd('\r').Split(',');
                    if (f.Length < 4 || f[0] == "Time") continue;
                    _t.Add(DateTime.ParseExact(f[0], "yyyy-MM-dd HH:mm:ss.fff", CI).Ticks);
                    _p.Add(double.Parse(f[1], CI)); _s.Add((int)double.Parse(f[2], CI)); _d.Add(sbyte.Parse(f[3], CI));
                }
                return new Tape { T = _t.ToArray(), P = _p.ToArray(), S = _s.ToArray(), D = _d.ToArray(), N = _t.Count };
            }
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

        // Перед выходными все сделки закрываются (решение пользователя, 2026-10-28): по первой сделке ленты в пятницу
        // с WeekendClose по времени биржи (Чикаго; лента пишется во времени биржи), CME закрывается в 16:00.
        // Вход с WeekendClose пятницы — пропуск WEEKEND. Праздничные ранние закрытия не учтены.
        public static readonly TimeSpan WeekendClose = new TimeSpan(15, 55, 0);
        // Проскальзывание (решение пользователя, 2026-10-28): заявки по рынку — вход, стоп, выход по времени и перед
        // выходными — исполняются на SlipTicks тиков хуже. Тейк — лимитная заявка, по своей цене. Комиссия — в долларах
        // (советник: CommissionUsd, прогон: pat/halves.py).
        public const int SlipTicks = 1;
        public static long FridayClose(long t)
        {
            DateTime d = new DateTime(t).Date;
            return d.AddDays(((int)DayOfWeek.Friday - (int)d.DayOfWeek + 7) % 7).Add(WeekendClose).Ticks;
        }
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
                e.Entry = a.P[j] + d * SlipTicks * Tick; e.TEntry = a.T[j]; e.SL = ext - d * Tick; e.TP = double.NaN;
                if (Math.Round(Math.Abs(e.SL - e.Entry) / Pip, 6) > MaxStopPips) { e.Skip = "FAR_STOP"; return; }
                long cutS = FridayClose(e.TEntry);
                if (e.TEntry >= cutS) { e.Skip = "WEEKEND"; return; }
                e.Reason = "TIME"; int lastK = j;
                for (int k = j; k < a.N && a.T[k] <= e.TEntry + FastHold.Ticks; k++)
                {
                    lastK = k;
                    if (a.T[k] >= cutS) { e.Reason = "WEEKEND"; e.Exit = a.P[k] - d * SlipTicks * Tick; e.TExit = a.T[k]; break; }
                    if (d * (a.P[k] - e.SL) <= 0) { e.Reason = "SL"; e.Exit = e.SL - d * SlipTicks * Tick; e.TExit = a.T[k]; break; }
                }
                if (e.Reason == "TIME") { e.Exit = a.P[lastK] - d * SlipTicks * Tick; e.TExit = a.T[lastK]; }
                if (e.Reason == "TIME" && a.T[a.N - 1] < Math.Min(e.TEntry + FastHold.Ticks, cutS)) { e.Reason = "OPEN"; e.Exit = a.P[lastK]; }
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
            e.Entry += dir * SlipTicks * Tick;
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
            else if (mode != "FAST" && Math.Round(Math.Abs(e.SL - e.Entry) / Pip, 6) > MaxStopPips) e.Skip = "FAR_STOP";
            if (e.Skip != "") return;
            long cut = FridayClose(e.TEntry), end = e.TFirst + (fast ? FastHold : Hold).Ticks;
            if (e.TEntry >= cut) { e.Skip = "WEEKEND"; return; }
            e.Reason = "TIME";
            double tp = e.TP, addAt = e.Entry - dir * AddPips * Pip, edge = e.Side > 0 ? e.ZoneHi : e.ZoneLo;
            int flow = 0, last = i;   // сделки толпы минус встречные с момента входа
            for (int k = i; k < a.N && a.T[k] <= end; k++)
            {
                last = k; flow += e.Side * a.D[k];
                if (a.T[k] >= cut) { e.Reason = "WEEKEND"; e.Exit = a.P[k] - dir * SlipTicks * Tick; e.TExit = a.T[k]; break; }
                if (dir * (a.P[k] - e.SL) <= 0) { e.Reason = "SL"; e.Exit = e.SL - dir * SlipTicks * Tick; e.TExit = a.T[k]; break; }
                if (dir * (a.P[k] - tp) >= 0) { e.Reason = "TP"; e.Exit = tp; e.TExit = a.T[k]; break; }
                if (add && !e.Added && dir * (a.P[k] - addAt) <= 0 && flow >= 0 && dir * (addAt - e.SL) > 0)
                { e.Added = true; tp = dir * (edge - addAt) > 0 ? edge : e.Entry; }
            }
            if (e.Reason == "TIME") { e.Exit = a.P[last] - dir * SlipTicks * Tick; e.TExit = a.T[last]; }
            // лента кончилась раньше срока выхода — сделка ещё открыта; Pips — по последней цене
            if (e.Reason == "TIME" && a.T[a.N - 1] < Math.Min(end, cut)) { e.Reason = "OPEN"; e.Exit = a.P[last]; }
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

    }
}

// ---- CoreTypes.cs ----

// Типы ядра LraTrader. Контракт между файлами — менять только по согласованию (interfaces.md).
// Ядро (Core*.cs) не использует типы MultiCharts: оно собирается и тестируется без них (tools/test.sh).
// История баров: IList<LraBar> h, h[0] — самый старый, h[h.Count - 1] — текущий закрытый бар.
// «Close[k]» в ТЗ = h[h.Count - 1 - k].Close.
namespace PowerLanguage.Strategy
{
    public class LraBar
    {
        public DateTime Time, Paris;          // время графика и время Парижа
        public string Session = "OFF";        // ASIA / EUROPE / AMERICA / OFF (ТЗ §2)
        public double Open, High, Low, Close;
        public double Volume, Ask, Bid;       // Volume = Ask + Bid по профилю
        public double UpTicks, DownTicks;     // число сделок вверх / вниз
        public double[] LvPrice = new double[0], LvAsk = new double[0], LvBid = new double[0]; // уровни профиля бара
        public bool NoData;                   // у бара нет данных профиля
    }

    // Параметры (ТЗ §9) + размеры тика и пипса. Значения по умолчанию — стартовые.
    public class LraSettings
    {
        public string CrowdMode = "CUM";          // CUM — кумулятивная дельта от начала роста; WINDOW — окно ImbBars
        public int    CumLookback = 12;            // CUM: начало роста ищется не дальше стольких баров назад
        public int    CumMinBars = 2;              // CUM: рост — не короче стольких баров
        public double CumWeight = 0.05;            // CUM: рост дельты по сделкам / объём с начала роста
        public int    ImbBars = 2;
        public double MinShare = 0.05;
        public double LevelImbPct = 0.3;
        public int    MinLevels = 3;
        public double MaxRangePips = 50;
        public int    MinRangeBars = 4;
        public int    MinActiveBars = 2;
        public int    MaxRangeAgeBars = 120;
        public int    SwingLookback = 48;
        public int    FractalBars = 2;             // фрактал: хай выше (лоу ниже) FractalBars соседей с каждой стороны
        public int    OffsetTicks = 1;
        public int    AtrPeriod = 14;
        public double StallAtr = 0.5;
        public double StrongAtr = 1.5;
        public double MaxStopPips = 30;
        public double MaxTakePips = 15;
        public double AddStepPips = 10;
        public string AddTargetMode = "SAME_TP";   // SAME_TP | ENTRY1
        public double LraMinDistPips = 30;
        public double DayTargetShare = 0.5;
        public double PipSize = 0.0001;
        public double Tick = 0.00005;               // цена одного тика инструмента
    }

    // Дисбаланс окна (ТЗ §3)
    public class LraImbalance
    {
        public double VolDelta, TickDelta, VolShare, TickShare;
        public int BuyLevels, SellLevels;
        public int Dir;             // +1 толпа покупает, -1 продаёт, 0 нет
        public string Reason = "";  // при Dir = 0: WEAK / ONE_PRICE / CONFLICT / NO_DATA
    }

    // Диапазон ЛРА (ТЗ §4)
    public class LraRange
    {
        public string Id;                     // Start в формате yyyyMMddHHmm
        public int StartIdx, EndIdx, BornIdx; // индексы в h: первый и последний бар диапазона, бар выхода
        public DateTime Start, End;
        public double High, Low, Poc, Volume;
        public int Bars, ExitDir;             // ExitDir +1 — вышли вверх, -1 — вниз
        public bool Done, Expired;
        public bool Active { get { return !Done && !Expired; } }
    }

    // Сигнал сценария (ТЗ §6)
    public class LraSignal
    {
        public string Book;                   // AGAINST | MOMENTUM
        public int Dir;                       // направление сделки
        public double Entry, SL, TP, TP2 = double.NaN;
        public int WinFrom;                   // индекс первого бара окна дисбаланса
        public LraImbalance Crowd;
        public LraRange Range;                // ближайший активный ЛРА или null
        public double RangeDistPips = double.NaN;
        public string Skip = "";              // "" — входим; NO_LEVEL / AGAINST_LRA / WEEKEND / NEWS / IN_POSITION
    }

    public class LraUnit { public int N; public int OpenIdx; public DateTime OpenTime; public double Entry, TP; }

    // Виртуальная позиция книги (ТЗ §7)
    public class LraPosition
    {
        public string Book, SignalId;
        public int Dir, WinFrom, EntryIdx;
        public double SL;
        public List<LraUnit> Units = new List<LraUnit>();
    }

    public class LraExit { public LraUnit Unit; public double Price; public string Reason; public bool Ambiguous; }
}

// ---- Strategy.cs ----

// LraTrader — виртуальный советник по паттернам автора (ТЗ: docs/LraTrader/tz.md). Склейка ядра с MultiCharts.
namespace PowerLanguage.Strategy
{
    [IOGMode(IOGMode.Enabled)]   // FAST_TP: расчёт и внутри бара — сделка рисуется, когда случилась (ордеров нет)
    public class LraTrader : SignalObject
    {
        // ---- Параметры (ТЗ §9) ----
        [Input] public string Algo            { get; set; }  // FAST_TP — по тикам (tz-ticks.md); LRA — прежний, по часовым профилям
        [Input] public string TicksDir        { get; set; }  // FAST_TP: папка <символ>_TRADES.csv (TickRecorder)
        [Input] public int    RefreshSec      { get; set; }  // FAST_TP: в реальном времени дочитывать ленту не чаще раза в столько секунд
        [Input] public string CrowdMode       { get; set; }  // CUM | WINDOW
        [Input] public int    CumLookback     { get; set; }
        [Input] public double CumWeight       { get; set; }
        [Input] public int    ImbBars         { get; set; }
        [Input] public double MinShare        { get; set; }
        [Input] public double LevelImbPct     { get; set; }
        [Input] public int    MinLevels       { get; set; }
        [Input] public double MaxRangePips    { get; set; }
        [Input] public int    MinRangeBars    { get; set; }
        [Input] public int    MinActiveBars   { get; set; }
        [Input] public int    MaxRangeAgeBars { get; set; }
        [Input] public int    SwingLookback   { get; set; }
        [Input] public int    FractalBars     { get; set; }
        [Input] public int    OffsetTicks     { get; set; }
        [Input] public int    AtrPeriod       { get; set; }
        [Input] public double StallAtr        { get; set; }
        [Input] public double StrongAtr       { get; set; }
        [Input] public double MaxStopPips     { get; set; }
        [Input] public double MaxTakePips     { get; set; }
        [Input] public double AddStepPips     { get; set; }
        [Input] public string AddTargetMode   { get; set; }  // SAME_TP | ENTRY1
        [Input] public double LraMinDistPips  { get; set; }
        [Input] public double DayTargetShare  { get; set; }
        [Input] public double PipSize         { get; set; }
        [Input] public double CommissionUsd   { get; set; }  // за контракт туда и обратно
        [Input] public string LogDir          { get; set; }
        [Input] public string NewsWindows     { get; set; }  // время Парижа, "ЧЧ:ММ-ЧЧ:ММ;..."
        // Письма об открытии и закрытии сделки FAST_TP — как в DivergenceDetector; пароль только из файла
        [Input] public bool   EmailEnabled     { get; set; }
        [Input] public string AlertToEmail     { get; set; }
        [Input] public string SmtpHost         { get; set; }
        [Input] public int    SmtpPort         { get; set; }
        [Input] public bool   SmtpUseSsl       { get; set; }
        [Input] public string SmtpUser         { get; set; }
        [Input] public string SmtpPasswordFile { get; set; }

        private const string Version = "0.1";
        private static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        private LraSettings _s;
        private List<LraBar> _h;
        private LraCsv _csv;
        private TimeZoneInfo _chartTz, _paris;
        private string _sym;
        private List<TimeSpan[]> _news;
        private readonly List<LraRange> _ranges = new List<LraRange>();
        private readonly Dictionary<string, LraPosition> _pos = new Dictionary<string, LraPosition>();  // книга -> позиция (ТЗ §7)
        private class UnitMarks { public ITrendLineObject Entry, Tp; public ITextObject TpLabel; }
        private readonly Dictionary<LraUnit, UnitMarks> _unitLines = new Dictionary<LraUnit, UnitMarks>();
        private readonly Dictionary<LraPosition, ITrendLineObject> _slLines = new Dictionary<LraPosition, ITrendLineObject>();
        private class FastMarks { public ITrendLineObject Entry, Sl, Tp; public bool Done; }
        private readonly Dictionary<long, FastMarks> _fast = new Dictionary<long, FastMarks>();  // TEntry -> линии сделки
        private readonly HashSet<long> _fastSignals = new HashSet<long>();
        private bool _noTicks;
        private readonly HashSet<long> _mailOpen = new HashSet<long>(), _mailClose = new HashSet<long>();  // TEntry
        private LraTicks _ticks;
        private LraTicks.TapeReader _tape;
        private List<LraTicks.Ev> _evs = new List<LraTicks.Ev>();
        private long _detectStep = -1;
        private DateTime _lastRefresh;
        private bool _vpWaiting;
        private int _vpPolls;

        private const string RangesHeader = "Time,ParisTime,Symbol,RangeId,Event,Start,End,High,Low,Poc,Volume,Bars,ExitDir";
        private const string TradesHeader = "CloseTime,ParisTime,Symbol,SignalId,Book,Unit,Dir,OpenTime,OpenPrice,SL,TP,ClosePrice,Exit,Pips,Usd,Ambiguous,Source,Version";
        private const string SignalsHeader = "Time,ParisTime,Symbol,SignalId,Book,Dir,Entry,SL,TP,TP2,SL_pips,TP_pips,VolDelta,TickDelta,VolShare,TickShare,BuyLevels,SellLevels,CrowdReason,Atr,DayAtr,RangeId,RangeDist_pips,Session,Taken,SkipReason,Source,Version";

        public LraTrader(object ctx) : base(ctx)
        {
            Algo            = "FAST_TP";
            TicksDir        = @"D:\TradeLog\Bars";
            RefreshSec      = 5;
            CrowdMode       = "CUM";
            CumLookback     = 12;
            CumWeight       = 0.05;
            ImbBars         = 2;
            MinShare        = 0.05;
            LevelImbPct     = 0.3;
            MinLevels       = 3;
            MaxRangePips    = 50;
            MinRangeBars    = 4;
            MinActiveBars   = 2;
            MaxRangeAgeBars = 120;
            SwingLookback   = 48;
            FractalBars     = 2;
            OffsetTicks     = 1;
            AtrPeriod       = 14;
            StallAtr        = 0.5;
            StrongAtr       = 1.5;
            MaxStopPips     = 30;
            MaxTakePips     = 15;
            AddStepPips     = 10;
            AddTargetMode   = "SAME_TP";
            LraMinDistPips  = 30;
            DayTargetShare  = 0.5;
            PipSize         = 0.0001;
            CommissionUsd   = 5;   // решение пользователя, 2026-10-28
            LogDir          = @"D:\TradeLog\LRA";
            NewsWindows     = "14:30-15:30;19:00-21:00";
            EmailEnabled     = true;
            AlertToEmail     = "andan@mateos.lv";
            SmtpHost         = "192.168.1.211";
            SmtpPort         = 587;
            SmtpUseSsl       = true;
            SmtpUser         = "andan@mateos.lv";
            SmtpPasswordFile = @"C:\Users\user\.smtp_pass";
        }

        protected override void StartCalc()
        {
            _s = new LraSettings
            {
                CrowdMode = CrowdMode, CumLookback = CumLookback, CumWeight = CumWeight,
                ImbBars = ImbBars, MinShare = MinShare, LevelImbPct = LevelImbPct, MinLevels = MinLevels,
                MaxRangePips = MaxRangePips, MinRangeBars = MinRangeBars, MinActiveBars = MinActiveBars,
                MaxRangeAgeBars = MaxRangeAgeBars, SwingLookback = SwingLookback, FractalBars = FractalBars, OffsetTicks = OffsetTicks,
                AtrPeriod = AtrPeriod, StallAtr = StallAtr, StrongAtr = StrongAtr, MaxStopPips = MaxStopPips,
                MaxTakePips = MaxTakePips, AddStepPips = AddStepPips, AddTargetMode = AddTargetMode,
                LraMinDistPips = LraMinDistPips, DayTargetShare = DayTargetShare, PipSize = PipSize,
                Tick = Bars.Info.MinMove / Bars.Info.PriceScale
            };
            _h = new List<LraBar>();

            _sym = Bars.Info.Name;
            foreach (char c in Path.GetInvalidFileNameChars()) _sym = _sym.Replace(c, '_');

            _paris = TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time");
            switch (Bars.Info.TimeZone)
            {
                case RequestTimeZone.GMT:      _chartTz = TimeZoneInfo.Utc; break;
                // ponytail: биржевое время = CME (Чикаго); для других бирж — добавить соответствие
                case RequestTimeZone.Exchange: _chartTz = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time"); break;
                default:                       _chartTz = TimeZoneInfo.Local; break;
            }

            _news = new List<TimeSpan[]>();
            foreach (string w in (NewsWindows ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] p = w.Split('-');
                if (p.Length == 2)
                    _news.Add(new[] { TimeSpan.Parse(p[0].Trim(), CI), TimeSpan.Parse(p[1].Trim(), CI) });
            }
            _ranges.Clear();
            _pos.Clear();
            _unitLines.Clear();
            _slLines.Clear();
            _fast.Clear();
            _fastSignals.Clear();
            _mailOpen.Clear();
            _mailClose.Clear();
            _noTicks = false;
            _ticks = new LraTicks(_s.Tick);
            _tape = null;
            _evs = new List<LraTicks.Ev>();
            _detectStep = -1;

            _csv = new LraCsv(LogDir, _sym);
            _csv.Write("runs.csv", "Time,Event,Symbol,Version,Params", DateTime.MaxValue,
                string.Format(CI, "{0:yyyy-MM-dd HH:mm:ss},START,{1},{2},Algo={31} TicksDir={32} ImbBars={3} MinShare={4} LevelImbPct={5} MinLevels={6} MaxRangePips={7} MinRangeBars={8} MinActiveBars={9} MaxRangeAgeBars={10} SwingLookback={11} OffsetTicks={12} FractalBars={27} AtrPeriod={13} StallAtr={14} StrongAtr={15} MaxStopPips={16} MaxTakePips={17} AddStepPips={18} AddTargetMode={19} LraMinDistPips={20} DayTargetShare={21} Pip={22} Comm={23} News={24} TZ={25} Tick={26} CrowdMode={28} CumLookback={29} CumWeight={30}",
                    DateTime.Now, _sym, Version, ImbBars, MinShare, LevelImbPct, MinLevels, MaxRangePips, MinRangeBars,
                    MinActiveBars, MaxRangeAgeBars, SwingLookback, OffsetTicks, AtrPeriod, StallAtr, StrongAtr,
                    MaxStopPips, MaxTakePips, AddStepPips, AddTargetMode, LraMinDistPips, DayTargetShare, PipSize,
                    CommissionUsd, NewsWindows, Bars.Info.TimeZone, _s.Tick, FractalBars, CrowdMode, CumLookback, CumWeight, Algo, TicksDir));
        }

        protected override void StopCalc()
        {
            if (_csv == null) return;
            _csv.Write("runs.csv", null, DateTime.MaxValue,
                string.Format(CI, "{0:yyyy-MM-dd HH:mm:ss},STOP,{1},{2},", DateTime.Now, _sym, Version));
        }

        protected override void CalcBar()
        {
            if (Bars.Status != EBarState.Close)
            {
                // внутри бара (только реальное время): дочитать ленту, открыть/закрыть сделку
                if (Algo == "FAST_TP" && _h.Count > 0 && Environment.IsRealTimeCalc &&
                    (DateTime.Now - _lastRefresh).TotalSeconds >= RefreshSec)
                    CalcFast(Bars.Time[0]);
                return;
            }
            if (!VpReady()) return;

            DateTime t = Bars.Time[0], paris = ToParis(t);
            var b = new LraBar
            {
                Time = t, Paris = paris, Session = Session(paris),
                Open = Bars.Open[0], High = Bars.High[0], Low = Bars.Low[0], Close = Bars.Close[0],
                UpTicks = Bars.UpTicks[0], DownTicks = Bars.DownTicks[0],
                Volume = Bars.Volume[0], NoData = true
            };
            FillProfile(b);
            _h.Add(b);
            // в истории последний бар графика ещё не закрыт и Close не приходит — считаем на последнем закрытом
            if (Algo == "FAST_TP") { if (Bars.LastBarOnChart || Bars.Time[0].AddHours(1) >= Bars.LastBarTime) CalcFast(t); return; }

            // ---- Сопровождение позиций (ТЗ §7, §8) ----
            double atr = this.AverageTrueRange(AtrPeriod);
            foreach (string book in new List<string>(_pos.Keys))
            {
                LraPosition p = _pos[book];
                foreach (LraExit x in LraPos.OnBar(_h, p, atr, _s))
                {
                    LraUnit u = x.Unit;
                    double move = p.Dir * (x.Price - u.Entry);
                    double usd = move * Bars.Info.BigPointValue - CommissionUsd;
                    _csv.Write("trades.csv", TradesHeader, t, string.Format(CI,
                        "{0:yyyy-MM-dd HH:mm},{1:yyyy-MM-dd HH:mm},{2},{3},{4},{5},{6},{7:yyyy-MM-dd HH:mm},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17}",
                        t, paris, _sym, p.SignalId, p.Book, u.N, p.Dir, u.OpenTime, F(u.Entry), F(p.SL), F(u.TP),
                        F(x.Price), x.Reason, F(move / PipSize), F(usd), x.Ambiguous ? 1 : 0,
                        Environment.IsRealTimeCalc ? "LIVE" : "HIST", Version));
                    DrawExit(p, x, t);
                }
                if (p.Units.Count == 0) { _pos.Remove(book); _slLines.Remove(p); }
            }

            // ---- Диапазоны ЛРА (ТЗ §4, §8) ----
            var changed = new List<LraRange>();
            LraRange born = LraRanges.Update(_h, _ranges, _s, changed);
            foreach (LraRange r in changed)
                _csv.Write("ranges_" + _sym + ".csv", RangesHeader, t, string.Format(CI,
                    "{0:yyyy-MM-dd HH:mm},{1:yyyy-MM-dd HH:mm},{2},{3},{4},{5:yyyy-MM-dd HH:mm},{6:yyyy-MM-dd HH:mm},{7},{8},{9},{10},{11},{12}",
                    t, paris, _sym, r.Id, r == born ? "NEW" : r.Done ? "DONE" : "EXPIRED", r.Start, r.End,
                    F(r.High), F(r.Low), F(r.Poc), F(r.Volume), r.Bars, r.ExitDir));

            // ---- Сигналы (ТЗ §6, §8) ----
            double dayAtr = LraLevels.DayAtr(_h, _s);
            bool weekend = paris.DayOfWeek == DayOfWeek.Saturday || paris.DayOfWeek == DayOfWeek.Sunday;
            bool news = InNews(paris);
            foreach (LraSignal sg in LraScen.Evaluate(_h, atr, dayAtr, _ranges, _s))
            {
                if (sg.Skip == "")
                {
                    if (weekend) sg.Skip = "WEEKEND";
                    else if (news) sg.Skip = "NEWS";
                    else if (_pos.ContainsKey(sg.Book)) sg.Skip = "IN_POSITION";
                }
                string id = string.Format(CI, "{0}_{1:yyyyMMddHHmm}_{2}", _sym, t, sg.Book);
                LraImbalance c = sg.Crowd;
                _csv.Write("signals_" + _sym + ".csv", SignalsHeader, t, string.Format(CI,
                    "{0:yyyy-MM-dd HH:mm},{1:yyyy-MM-dd HH:mm},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21},{22},{23},{24},{25},{26},{27}",
                    t, paris, _sym, id, sg.Book, sg.Dir, F(sg.Entry), F(sg.SL), F(sg.TP), F(sg.TP2),
                    F(Math.Abs(sg.Entry - sg.SL) / PipSize), F(Math.Abs(sg.TP - sg.Entry) / PipSize),
                    F(c.VolDelta), F(c.TickDelta), F(c.VolShare), F(c.TickShare), c.BuyLevels, c.SellLevels, c.Reason,
                    F(atr), F(dayAtr), sg.Range == null ? "" : sg.Range.Id, F(sg.RangeDistPips),
                    b.Session, sg.Skip == "" ? 1 : 0, sg.Skip, Environment.IsRealTimeCalc ? "LIVE" : "HIST", Version));

                if (sg.Skip == "")
                {
                    _pos[sg.Book] = LraPos.Open(sg, _h, id);
                    IArrowObject ar = DrwArrow.Create(new ChartPoint(t, sg.Entry), sg.Dir < 0);
                    ar.Color = sg.Dir > 0 ? Color.Blue : Color.Magenta;
                    ar.Text = sg.Book == "AGAINST" ? "A" : "D";
                }
            }
            DrawOpen(t);
        }

        // ---- Algo = FAST_TP (tz-ticks.md, шаг 3) ----
        // В конце истории лента читается целиком, дальше — дочитывается: на закрытии каждого бара и внутри бара не чаще
        // RefreshSec. События ищутся заново, когда лента перешла в новый 5-минутный шаг (события бывают только на шагах);
        // сделки пересчитываются при каждом дочитывании — так стоп и тейк срабатывают через секунды.
        // Ядро — CoreTicks.cs, тот же код, что прогон src/lra/ticks. Время ленты — время графика 1 Tick; часовой график
        // должен быть в том же поясе (Exchange).
        // ponytail: поиск событий — по всей ленте с начала (~1 с на месяц тиков раз в 5 мин); с последнего шага — если лента вырастет в разы
        private const string FastTradesHeader = "CloseTime,ParisTime,Symbol,Crowd,EntryTime,EntryParis,Dir,Entry,SL,TP,ExitTime,Exit,Reason,Pips,Usd,Source,Version";
        private const string FastSignalsHeader = "Time,ParisTime,Symbol,Crowd,GrowthStart,Trades,Weight,Prices,ZoneLo,ZoneHi,H0,Target,Taken,SkipReason,Source,Version";
        private void CalcFast(DateTime now)
        {
            string path = Path.Combine(TicksDir, _sym + "_TRADES.csv");
            if (!File.Exists(path)) { if (!_noTicks) RunEvent("NO_TICKS " + path); _noTicks = true; return; }
            _lastRefresh = DateTime.Now;
            if (_tape == null) _tape = new LraTicks.TapeReader(path);
            LraTicks.Tape a = _tape.Read();
            if (a.N == 0) return;
            long step = a.T[a.N - 1] / LraTicks.Step.Ticks;
            if (step != _detectStep)
            {
                _detectStep = step;
                _evs = _ticks.Detect(a, _h);
                foreach (LraTicks.Ev e in _evs) _ticks.Follow(a, _h, e);
            }
            foreach (LraTicks.Ev e in _evs) _ticks.Trade(a, _h, e, "FAST_TP", false);
            LraTicks.Simulate(_evs);
            List<LraTicks.Ev> evs = _evs;
            int drawn0 = _fast.Count;
            string src = Environment.IsRealTimeCalc ? "LIVE" : "HIST";
            foreach (LraTicks.Ev e in evs)
            {
                DateTime tf = new DateTime(e.TFirst);
                if (_fastSignals.Add(e.TFirst * 2 + (e.Side > 0 ? 1 : 0)))
                    _csv.Write("signals_fast_" + _sym + ".csv", FastSignalsHeader, tf, string.Format(CI,
                        "{0:yyyy-MM-dd HH:mm},{1:yyyy-MM-dd HH:mm},{2},{3},{4:yyyy-MM-dd HH:mm},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15}",
                        tf, ToParis(tf), _sym, e.Side > 0 ? "BUY" : "SELL", new DateTime(e.TS), e.Trades, F(e.Weight), e.Prices,
                        F(e.ZoneLo), F(e.ZoneHi), F(e.H0), F(e.Target), e.Skip == "" ? 1 : 0, e.Skip, src, Version));
                if (e.Skip != "") continue;
                FastMarks m;
                DateTime te = new DateTime(e.TEntry), bar = BarOf(te);
                if (Lost(e.TEntry)) { _fast.Remove(e.TEntry); RunEvent("FAST_LOST " + te.ToString("yyyy-MM-dd HH:mm", CI)); }
                if (!_fast.TryGetValue(e.TEntry, out m))
                {
                    int dir = e.Side;   // вместе с толпой
                    IArrowObject ar = DrwArrow.Create(new ChartPoint(bar, e.Entry), dir < 0);
                    ar.Color = dir > 0 ? Color.Blue : Color.Magenta;
                    ar.Text = "F";
                    _fast[e.TEntry] = m = new FastMarks
                    {
                        Entry = Line(new ChartPoint(bar, e.Entry), new ChartPoint(now, e.Entry), Color.DimGray, ETLStyle.ToolSolid),
                        Sl = Line(new ChartPoint(bar, e.SL), new ChartPoint(now, e.SL), Color.Red, ETLStyle.ToolSolid),
                        Tp = Line(new ChartPoint(bar, e.TP), new ChartPoint(now, e.TP), Color.Green, ETLStyle.ToolSolid)
                    };
                    Label(new ChartPoint(bar, e.SL), "SL", Color.Red);
                    Label(new ChartPoint(bar, e.TP), "TP", Color.Green);
                }
                // письмо — только о свежих событиях (не старше часа по ленте): старые сделки при пересчёте истории не шлются
                long tapeNow = a.T[a.N - 1];
                if (_mailOpen.Add(e.TEntry) && tapeNow - e.TEntry < TimeSpan.TicksPerHour) MailOpen(e, te);
                if (m.Done) continue;
                DateTime end = e.Reason == "OPEN" ? now : BarOf(new DateTime(e.TExit));
                m.Entry.End = new ChartPoint(end, e.Entry); m.Sl.End = new ChartPoint(end, e.SL); m.Tp.End = new ChartPoint(end, e.TP);
                if (e.Reason == "OPEN") continue;
                m.Done = true;
                double pips = Math.Round(e.Pips, 1);
                Color c = pips > 0 ? Color.Green : pips < 0 ? Color.Red : Color.Gray;
                Line(new ChartPoint(bar, e.Entry), new ChartPoint(end, e.Exit), c, ETLStyle.ToolDotted);
                ITextObject tx = DrwText.Create(new ChartPoint(end, e.Exit), string.Format(CI, " {0} {1:+0;-0;0} п.", e.Reason, pips));
                tx.Color = c; tx.HStyle = ETextStyleH.Right; tx.VStyle = ETextStyleV.Center;
                // время до минуты: метка CSV читается из файла с точностью до минуты, иначе строка запишется повторно
                DateTime tx0 = new DateTime(e.TExit - e.TExit % TimeSpan.TicksPerMinute);
                _csv.Write("trades_fast.csv", FastTradesHeader, tx0, string.Format(CI,
                    "{0:yyyy-MM-dd HH:mm},{1:yyyy-MM-dd HH:mm},{2},{3},{4:yyyy-MM-dd HH:mm},{5:yyyy-MM-dd HH:mm},{6},{7},{8},{9},{10:yyyy-MM-dd HH:mm},{11},{12},{13},{14},{15},{16}",
                    tx0, ToParis(tx0), _sym, e.Side > 0 ? "BUY" : "SELL", te, ToParis(te), e.Side, F(e.Entry), F(e.SL), F(e.TP),
                    tx0, F(e.Exit), e.Reason, F(pips), F(Usd(e)), src, Version));
                if (_mailClose.Add(e.TEntry) && tapeNow - e.TExit < TimeSpan.TicksPerHour) MailClose(e, te, evs);
            }
            if (_fast.Count != drawn0)   // диагностика: когда и сколько сделок нарисовано
                RunEvent(string.Format(CI, "FAST_DRAW status={0} rt={1} bar={2:yyyy-MM-dd HH:mm} trades={3}", Bars.Status,
                    Environment.IsRealTimeCalc, now, _fast.Count));
        }

        // MultiCharts мог стереть рисунки, созданные внутри бара, — тогда рисуем сделку заново
        private bool Lost(long key) { FastMarks m; return _fast.TryGetValue(key, out m) && !m.Entry.Exist; }

        private double Usd(LraTicks.Ev e) { return e.Pips * PipSize * Bars.Info.BigPointValue - CommissionUsd; }

        private void MailOpen(LraTicks.Ev e, DateTime te)
        {
            string side = e.Side > 0 ? "ПОКУПКА" : "ПРОДАЖА";
            SendMail(string.Format(CI, "[LraTrader {0}] ОТКРЫТА {1} {2}", _sym, side, F(e.Entry)), string.Format(CI,
                "Символ:   {0}\nСделка:   {1} вместе с толпой (FAST_TP)\nВход:     {2} в {3:yyyy-MM-dd HH:mm} по бирже ({4:HH:mm} Париж)\n" +
                "Стоп:     {5} ({6:0.0} п.)\nТейк:     {7} ({8:0.0} п.)\nВыход по времени: через 24 ч или в пятницу 15:55 по бирже\n\n" +
                "Толпа:    {9} с {10:yyyy-MM-dd HH:mm}, сделок {11}, перевес {12:0%}, цен {13}, зона {14}–{15}",
                _sym, side, F(e.Entry), te, ToParis(te), F(e.SL), Math.Abs(e.Entry - e.SL) / PipSize, F(e.TP),
                Math.Abs(e.TP - e.Entry) / PipSize, e.Side > 0 ? "покупает" : "продаёт", new DateTime(e.TS), e.Trades, e.Weight,
                e.Prices, F(e.ZoneLo), F(e.ZoneHi)));
        }

        private void MailClose(LraTicks.Ev e, DateTime te, List<LraTicks.Ev> evs)
        {
            DateTime tx = new DateTime(e.TExit);
            var done = evs.FindAll(x => x.Skip == "" && x.Reason != "OPEN" && x.Reason != "");
            double sumPips = 0, sumUsd = 0; int wins = 0;
            foreach (var x in done) { sumPips += x.Pips; sumUsd += Usd(x); if (x.Pips > 0) wins++; }
            SendMail(string.Format(CI, "[LraTrader {0}] ЗАКРЫТА {1} {2:+0.0;-0.0;0} п. ${3:+0.00;-0.00;0}", _sym, e.Reason, e.Pips, Usd(e)),
                string.Format(CI,
                "Символ:   {0}\nСделка:   {1}\nВход:     {2} в {3:yyyy-MM-dd HH:mm} по бирже ({4:HH:mm} Париж)\n" +
                "Выход:    {5} в {6:yyyy-MM-dd HH:mm} по бирже ({7:HH:mm} Париж), причина {8}\nСтоп / тейк: {9} / {10}\n" +
                "Итог:     {11:+0.0;-0.0;0} п., ${12:+0.00;-0.00;0} (комиссия ${13}, проскальзывание 1 тик учтено)\n\n" +
                "Всего по {0} с начала ленты: сделок {14}, в плюсе {15}, {16:+0.0;-0.0;0} п., ${17:+0.00;-0.00;0}",
                _sym, e.Side > 0 ? "ПОКУПКА" : "ПРОДАЖА", F(e.Entry), te, ToParis(te), F(e.Exit), tx, ToParis(tx), e.Reason,
                F(e.SL), F(e.TP), e.Pips, Usd(e), CommissionUsd, done.Count, wins, sumPips, sumUsd));
        }

        // Отправка в фоне, как в DivergenceDetector; ошибка — строка MAIL_ERROR в runs.csv
        private void SendMail(string subj, string body)
        {
            if (!EmailEnabled) return;
            string host = SmtpHost, user = SmtpUser, to = AlertToEmail, pass = "";
            int port = SmtpPort; bool ssl = SmtpUseSsl;
            string from = string.IsNullOrWhiteSpace(user) ? "alert@localhost" : user;
            try { if (!string.IsNullOrWhiteSpace(SmtpPasswordFile)) pass = File.ReadAllText(SmtpPasswordFile).Trim(); }
            catch (Exception ex) { RunEvent("MAIL_ERROR password file: " + ex.Message.Replace(',', ';')); return; }
            RunEvent("MAIL " + subj.Replace(',', ';'));
            Task.Run(() =>
            {
                try
                {
                    ServicePointManager.ServerCertificateValidationCallback = (sender, cert, chain, errors) => true;
                    using (var client = new SmtpClient(host, port))
                    {
                        client.EnableSsl = ssl;
                        client.DeliveryMethod = SmtpDeliveryMethod.Network;
                        client.Timeout = 10000;
                        if (!string.IsNullOrWhiteSpace(user)) client.Credentials = new NetworkCredential(user, pass);
                        using (var msg = new MailMessage(from, to, subj, body))
                        {
                            msg.SubjectEncoding = msg.BodyEncoding = Encoding.UTF8;
                            client.Send(msg);
                        }
                    }
                }
                catch (Exception ex) { RunEvent("MAIL_ERROR " + ex.Message.Replace(',', ';')); }
            });
        }

        // Часовой бар, в который попала сделка ленты: время бара — его закрытие
        private static DateTime BarOf(DateTime t) { return t.Date.AddHours(t.Hour + 1); }

        // ---- Сделки на графике: линии входа (серая), стопа (красная), тейка (зелёная) тянутся, пока часть открыта;
        // у начала линий подписи SL, TP1, TP2; на выходе — пунктир от входа к цене выхода и подпись «причина ±пункты»
        // (зелёная — плюс, красная — минус, серая — ноль).
        private void DrawOpen(DateTime t)
        {
            foreach (LraPosition p in _pos.Values)
            {
                ITrendLineObject sl;
                if (!_slLines.TryGetValue(p, out sl))
                {
                    var a = new ChartPoint(_h[p.EntryIdx].Time, p.SL);
                    _slLines[p] = Line(a, new ChartPoint(t, p.SL), Color.Red, ETLStyle.ToolSolid);
                    Label(a, "SL", Color.Red);
                }
                else sl.End = new ChartPoint(t, p.SL);
                foreach (LraUnit u in p.Units)
                {
                    UnitMarks m;
                    var tpAt = new ChartPoint(u.OpenTime, u.TP);
                    if (!_unitLines.TryGetValue(u, out m))
                        _unitLines[u] = new UnitMarks
                        {
                            Entry = Line(new ChartPoint(u.OpenTime, u.Entry), new ChartPoint(t, u.Entry), Color.DimGray, ETLStyle.ToolSolid),
                            Tp = Line(tpAt, new ChartPoint(t, u.TP), Color.Green, ETLStyle.ToolSolid),
                            TpLabel = Label(tpAt, "TP" + u.N, Color.Green)
                        };
                    else
                    {
                        m.Entry.End = new ChartPoint(t, u.Entry);
                        m.Tp.Begin = tpAt;   // TP части 1 меняется при доборе ENTRY1
                        m.Tp.End = new ChartPoint(t, u.TP);
                        m.TpLabel.Location = tpAt;
                    }
                }
            }
        }

        private void DrawExit(LraPosition p, LraExit x, DateTime t)
        {
            LraUnit u = x.Unit;
            UnitMarks m;
            if (_unitLines.TryGetValue(u, out m)) { m.Entry.End = new ChartPoint(t, u.Entry); m.Tp.End = new ChartPoint(t, u.TP); _unitLines.Remove(u); }
            ITrendLineObject sl;
            if (_slLines.TryGetValue(p, out sl)) sl.End = new ChartPoint(t, p.SL);
            double pips = Math.Round(p.Dir * (x.Price - u.Entry) / PipSize, 1);
            Color c = pips > 0 ? Color.Green : pips < 0 ? Color.Red : Color.Gray;
            Line(new ChartPoint(u.OpenTime, u.Entry), new ChartPoint(t, x.Price), c, ETLStyle.ToolDotted);
            ITextObject tx = DrwText.Create(new ChartPoint(t, x.Price), string.Format(CI, " {0} {1:+0;-0;0} п.", x.Reason, pips));
            tx.Color = c;
            tx.HStyle = ETextStyleH.Right;
            tx.VStyle = ETextStyleV.Center;
        }

        private ITextObject Label(ChartPoint at, string text, Color c)
        {
            ITextObject tx = DrwText.Create(at, text);
            tx.Color = c;
            tx.HStyle = ETextStyleH.Right;
            tx.VStyle = ETextStyleV.Above;
            return tx;
        }

        private ITrendLineObject Line(ChartPoint a, ChartPoint b, Color c, ETLStyle st)
        {
            ITrendLineObject l = DrwTrendLine.Create(a, b);
            l.Color = c; l.Style = st; l.Size = 1;
            return l;
        }

        // MC строит профиль объёма позже расчёта истории, сигнала о готовности (EChanged) не шлёт.
        // Пока профилей нет — бары пропускаем (иначе CSV запишутся с пустой дельтой и водяная метка
        // не даст их переписать) и опрашиваем последний бар; профиль появился — полный пересчёт.
        // ponytail: опрос раз в 2 с, не больше 60 раз; дальше — ручной Status Off/On
        private bool VpReady()
        {
            bool ready = HasProfiles(this.VolumeProfile) && (Algo == "FAST_TP" || HasProfiles(Vp2()));
            if (!ready)
            {
                _vpWaiting = true;
                if (Bars.LastBarOnChart && _vpPolls < 60)
                {
                    if (_vpPolls % 10 == 0) RunEvent("VP_WAIT poll=" + _vpPolls);
                    _vpPolls++;
                    ExecControl.RecalcLastBarAfter(TimeSpan.FromSeconds(2));
                }
                return false;
            }
            if (_vpWaiting) { _vpWaiting = false; _vpPolls = 0; RunEvent("VP_READY"); ExecControl.Recalculate(); return false; }
            return true;
        }

        private static bool HasProfiles(IProfilesCollection vp)
        {
            var svc = vp as IProfilesCollectionService;
            if (svc != null) svc.Syncronize();
            return vp != null && vp.Items != null && vp.Items.Count > 0;
        }

        // Data2 — тот же символ и период, его Volume Profile с Breakdown = Up vs Down Tick Delta.
        // Нет Data2 — профилей нет, в runs.csv пойдут VP_WAIT.
        private IProfilesCollection Vp2()
        {
            try { return VolumeProfileByDataStream(2); } catch { return null; }
        }

        private void RunEvent(string ev)
        {
            _csv.Write("runs.csv", null, DateTime.MaxValue,
                string.Format(CI, "{0:yyyy-MM-dd HH:mm:ss},{1},{2},{3},", DateTime.Now, ev, _sym, Version));
        }

        private static string F(double v) { return double.IsNaN(v) ? "" : v.ToString("0.######", CI); }

        private bool InNews(DateTime paris)
        {
            foreach (TimeSpan[] w in _news)
                if (paris.TimeOfDay >= w[0] && paris.TimeOfDay <= w[1]) return true;
            return false;
        }

        // Профиль бара: уровни и суммарные Ask/Bid. Нет профиля — NoData = true (как zero в DDAutoTrader.GetDelta).
        private void FillProfile(LraBar b)
        {
            var vp = this.VolumeProfile;
            if (vp == null) return;
            var svc = vp as IProfilesCollectionService;
            if (svc != null) svc.Syncronize();
            IProfile p = null;
            // ItemForBar(Current) отдаёт профиль следующего бара (сверено по High/Low в CSV BarRecorder), поэтому -1
            try { p = vp.ItemForBar(Bars.FullSymbolData.Current - 1); } catch { }
            if (p == null || p.Empty) return;

            var price = new List<double>(); var ask = new List<double>(); var bid = new List<double>();
            decimal a = 0, s = 0;
            foreach (ILevel lv in p.Values)
            {
                price.Add((double)lv.Price); ask.Add((double)lv.AskTradedValue); bid.Add((double)lv.BidTradedValue);
                a += lv.AskTradedValue; s += lv.BidTradedValue;
            }
            b.LvPrice = price.ToArray(); b.LvAsk = ask.ToArray(); b.LvBid = bid.ToArray();
            b.Ask = (double)a; b.Bid = (double)s;
            b.Volume = b.Ask + b.Bid;

            // Дельта по сделкам (ап/даунтик, автор В2 21:01) — из профиля Data2. CQG не даёт барам UpTicks/DownTicks.
            // Индекс бара — от Data1: BarsOfData(2).FullSymbolData.Current бросает исключение; символ и период те же
            IProfile p2 = null;
            try { p2 = Vp2().ItemForBar(Bars.FullSymbolData.Current - 1); } catch { }
            if (p2 == null || p2.Empty) return;
            decimal up = 0, dn = 0;
            foreach (ILevel lv in p2.Values) { up += lv.AskTradedValue; dn += lv.BidTradedValue; }
            b.UpTicks = (double)up; b.DownTicks = (double)dn;
            b.NoData = b.Volume == 0;
        }

        private DateTime ToParis(DateTime t)
        {
            try { return TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(t, DateTimeKind.Unspecified), _chartTz, _paris); }
            catch { return t; }   // несуществующий час при переводе часов
        }

        private static string Session(DateTime paris)
        {
            double h = paris.TimeOfDay.TotalHours;
            return h < 8 ? "ASIA" : h < 14.5 ? "EUROPE" : h < 22 ? "AMERICA" : "OFF";
        }
    }
}
