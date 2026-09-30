// LraTrader.Strategy.CS — СОБРАНО tools/bundle.sh из src/lra/*.cs. Не править руками.
using PowerLanguage.Function;
using PowerLanguage.VolumeProfile;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
        // Ближайший по времени свинг за ценой: side +1 — верхний свинг выше price (+ отступ), -1 — нижний ниже (- отступ). Нет — NaN.
        public static double Nearest(IList<LraBar> h, int side, double price, LraSettings s)
        {
            // i+1 должен существовать (i <= Count-2), i-1 тоже (i >= 1); глубина — не дальше SwingLookback баров от текущего
            int last = Math.Max(1, h.Count - 1 - s.SwingLookback);
            for (int i = h.Count - 2; i >= last; i--)
            {
                if (side > 0)
                {
                    if (h[i].High > h[i - 1].High && h[i].High > h[i + 1].High && h[i].High > price)
                        return h[i].High + s.OffsetTicks * s.Tick;
                }
                else if (h[i].Low < h[i - 1].Low && h[i].Low < h[i + 1].Low && h[i].Low < price)
                    return h[i].Low - s.OffsetTicks * s.Tick;
            }
            return double.NaN;
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
            if (last - s.ImbBars < 0) return res;

            int winFrom = h.Count - s.ImbBars;
            LraImbalance crowd = LraImb.Measure(h, winFrom, last, s);
            if (crowd.Dir == 0) return res;

            double entry = h[last].Close;
            double move = entry - h[last - s.ImbBars].Close;
            bool strong = Math.Abs(move) >= s.StrongAtr * atr;
            bool a = move * crowd.Dir <= s.StallAtr * atr && !strong; // сильное движение — только D
            bool d = strong && Math.Sign(move) == -crowd.Dir;
            if (!a && !d) return res;

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
        public int    ImbBars = 2;
        public double MinShare = 0.05;
        public double LevelImbPct = 0.3;
        public int    MinLevels = 3;
        public double MaxRangePips = 50;
        public int    MinRangeBars = 4;
        public int    MinActiveBars = 2;
        public int    MaxRangeAgeBars = 120;
        public int    SwingLookback = 48;
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
    public class LraTrader : SignalObject
    {
        // ---- Параметры (ТЗ §9) ----
        [Input] public int    ImbBars         { get; set; }
        [Input] public double MinShare        { get; set; }
        [Input] public double LevelImbPct     { get; set; }
        [Input] public int    MinLevels       { get; set; }
        [Input] public double MaxRangePips    { get; set; }
        [Input] public int    MinRangeBars    { get; set; }
        [Input] public int    MinActiveBars   { get; set; }
        [Input] public int    MaxRangeAgeBars { get; set; }
        [Input] public int    SwingLookback   { get; set; }
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

        private const string RangesHeader = "Time,ParisTime,Symbol,RangeId,Event,Start,End,High,Low,Poc,Volume,Bars,ExitDir";
        private const string TradesHeader = "CloseTime,ParisTime,Symbol,SignalId,Book,Unit,Dir,OpenTime,OpenPrice,SL,TP,ClosePrice,Exit,Pips,Usd,Ambiguous,Source,Version";
        private const string SignalsHeader = "Time,ParisTime,Symbol,SignalId,Book,Dir,Entry,SL,TP,TP2,SL_pips,TP_pips,VolDelta,TickDelta,VolShare,TickShare,BuyLevels,SellLevels,CrowdReason,Atr,DayAtr,RangeId,RangeDist_pips,Session,Taken,SkipReason,Source,Version";

        public LraTrader(object ctx) : base(ctx)
        {
            ImbBars         = 2;
            MinShare        = 0.05;
            LevelImbPct     = 0.3;
            MinLevels       = 3;
            MaxRangePips    = 50;
            MinRangeBars    = 4;
            MinActiveBars   = 2;
            MaxRangeAgeBars = 120;
            SwingLookback   = 48;
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
            CommissionUsd   = 0;
            LogDir          = @"D:\TradeLog\LRA";
            NewsWindows     = "14:30-15:30;19:00-21:00";
        }

        protected override void StartCalc()
        {
            _s = new LraSettings
            {
                ImbBars = ImbBars, MinShare = MinShare, LevelImbPct = LevelImbPct, MinLevels = MinLevels,
                MaxRangePips = MaxRangePips, MinRangeBars = MinRangeBars, MinActiveBars = MinActiveBars,
                MaxRangeAgeBars = MaxRangeAgeBars, SwingLookback = SwingLookback, OffsetTicks = OffsetTicks,
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

            _csv = new LraCsv(LogDir, _sym);
            _csv.Write("runs.csv", "Time,Event,Symbol,Version,Params", DateTime.MaxValue,
                string.Format(CI, "{0:yyyy-MM-dd HH:mm:ss},START,{1},{2},ImbBars={3} MinShare={4} LevelImbPct={5} MinLevels={6} MaxRangePips={7} MinRangeBars={8} MinActiveBars={9} MaxRangeAgeBars={10} SwingLookback={11} OffsetTicks={12} AtrPeriod={13} StallAtr={14} StrongAtr={15} MaxStopPips={16} MaxTakePips={17} AddStepPips={18} AddTargetMode={19} LraMinDistPips={20} DayTargetShare={21} Pip={22} Comm={23} News={24} TZ={25} Tick={26}",
                    DateTime.Now, _sym, Version, ImbBars, MinShare, LevelImbPct, MinLevels, MaxRangePips, MinRangeBars,
                    MinActiveBars, MaxRangeAgeBars, SwingLookback, OffsetTicks, AtrPeriod, StallAtr, StrongAtr,
                    MaxStopPips, MaxTakePips, AddStepPips, AddTargetMode, LraMinDistPips, DayTargetShare, PipSize,
                    CommissionUsd, NewsWindows, Bars.Info.TimeZone, _s.Tick));
        }

        protected override void StopCalc()
        {
            if (_csv == null) return;
            _csv.Write("runs.csv", null, DateTime.MaxValue,
                string.Format(CI, "{0:yyyy-MM-dd HH:mm:ss},STOP,{1},{2},", DateTime.Now, _sym, Version));
        }

        protected override void CalcBar()
        {
            if (Bars.Status != EBarState.Close) return;

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
                }
                if (p.Units.Count == 0) _pos.Remove(book);
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

                if (sg.Skip == "") _pos[sg.Book] = LraPos.Open(sg, _h, id);
            }
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
            try { p = vp.ItemForBar(Bars.FullSymbolData.Current); } catch { }
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
