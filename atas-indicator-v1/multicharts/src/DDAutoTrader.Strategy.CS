// DDAutoTrader.Strategy.CS — виртуальный советник по дисбалансам (ТЗ: docs/DivergenceDetector/DDAutoTrader.TZ.md)
// Версия: 2026-09-26-v1.0
// Путь: C:\ProgramData\TS Support\MultiCharts .NET64 Special Edition\StudyServer\Techniques\CS\DDAutoTrader.Strategy.CS
//
// Режим: ВИРТУАЛЬНАЯ торговля — реальные ордера НЕ отправляются. Всё пишется в CSV в LogDir.
// Сигналы считаются здесь же, той же логикой, что в DivergenceDetector v4.6
// (стратегия не может вызывать индикатор). Нужен Volume Profile на графике, как для индикатора.
// Второй поток данных (минутки того же инструмента) — чтобы узнать, что сработало первым,
// если стоп и тейк задеты в одном часовом баре. Без него такой бар считается стопом, Ambiguous=1.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using PowerLanguage.Function;
using PowerLanguage.VolumeProfile;

namespace PowerLanguage.Strategy
{
    public class DDAutoTrader : SignalObject
    {
        // ---- Сигналы (как в DivergenceDetector) ----
        [Input] public int    NBars           { get; set; }
        [Input] public int    StreakBars      { get; set; }
        [Input] public bool   UseNarrowFilter { get; set; }
        [Input] public int    AtrPeriod       { get; set; }
        [Input] public double MaxRangeATR     { get; set; }
        // ---- Сделки и файлы ----
        [Input] public string LogDir             { get; set; }
        [Input] public double PipSize            { get; set; }
        [Input] public double CommissionUsd      { get; set; }  // за контракт туда и обратно
        [Input] public int    FractalOffsetTicks { get; set; }
        [Input] public int    FractalLookback    { get; set; }
        [Input] public string NewsWindows        { get; set; }  // время Парижа, "ЧЧ:ММ-ЧЧ:ММ;..."

        private const string Version = "1.0";
        private const int    Horizon = 24;
        private static readonly int[] Checkpoints = { 1, 3, 6, 12, 24 };
        private static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        private class Pos { public int Dir; public double Entry, SL, TP; public DateTime Time; public string Id, Book; }

        private class Pending
        {
            public string Id; public DateTime Time; public int Dir, Bars;
            public double Entry, SL, TP, Up, Dn, HypoPips;
            public double[] UpAt = new double[5], DnAt = new double[5];
            public string Hypo; public bool HypoAmb;
        }

        private List<decimal> _delta;   // [0] = текущий бар
        private List<bool>    _zero;    // у бара нет данных о покупках и продажах
        private decimal       _prevVolume;
        private Dictionary<string, Pos> _pos;
        private List<Pending> _pending;
        private Dictionary<string, DateTime> _watermark;         // файл → время последней строки на момент запуска
        private Dictionary<string, List<string>> _backlog;       // строки, которые не удалось записать (файл занят)
        private HashSet<string> _disabledBooks;
        private int _rulesVersion;
        private List<TimeSpan[]> _news;
        private TimeZoneInfo _chartTz, _paris;
        private string _sym;

        public DDAutoTrader(object ctx) : base(ctx)
        {
            NBars              = 2;
            StreakBars         = 3;
            UseNarrowFilter    = false;
            AtrPeriod          = 14;
            MaxRangeATR        = 0.7;
            LogDir             = @"D:\TradeLog";
            PipSize            = 0.0001;
            CommissionUsd      = 0;
            FractalOffsetTicks = 1;
            FractalLookback    = 100;
            NewsWindows        = "14:30-15:30;19:00-21:00";
        }

        protected override void StartCalc()
        {
            if (NBars < 2) NBars = 2;
            if (StreakBars < 3) StreakBars = 3;
            _delta = new List<decimal>(); _zero = new List<bool>(); _prevVolume = 0;
            _pos = new Dictionary<string, Pos>();
            _pending = new List<Pending>();
            _watermark = new Dictionary<string, DateTime>();
            _backlog = new Dictionary<string, List<string>>();
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

            Directory.CreateDirectory(LogDir);
            LoadRules();
            Write("runs.csv", "Time,Event,Symbol,Version,RulesVersion,Params", DateTime.MaxValue,
                string.Format(CI, "{0:yyyy-MM-dd HH:mm:ss},START,{1},{2},{3},NBars={4} StreakBars={5} Narrow={6} Pip={7} Comm={8} Offset={9} News={10} TZ={11} Data2={12}",
                    DateTime.Now, _sym, Version, _rulesVersion, NBars, StreakBars, UseNarrowFilter, PipSize,
                    CommissionUsd, FractalOffsetTicks, NewsWindows, Bars.Info.TimeZone, MaxDataStream >= 2));
        }

        protected override void StopCalc()
        {
            if (_sym == null) return;
            Write("runs.csv", null, DateTime.MaxValue,
                string.Format(CI, "{0:yyyy-MM-dd HH:mm:ss},STOP,{1},{2},{3},", DateTime.Now, _sym, Version, _rulesVersion));
        }

        protected override void CalcBar()
        {
            if (Bars.Status != EBarState.Close) return;

            decimal vol; bool zero;
            decimal d = GetDelta(out vol, out zero);
            _delta.Insert(0, d); _zero.Insert(0, zero);
            int maxHist = Math.Max(NBars, StreakBars) + 2;
            if (_delta.Count > maxHist) { _delta.RemoveAt(maxHist); _zero.RemoveAt(maxHist); }
            bool volCond = vol > _prevVolume;
            _prevVolume = vol;

            UpdatePending();
            CheckExits();

            if (Bars.CurrentBar <= maxHist) return;

            int priceDir = PriceDir(), deltaDir = DeltaDir();
            if      (priceDir ==  1 && deltaDir == -1 && volCond) OnSignal("DIV", -1, d, vol);
            else if (priceDir == -1 && deltaDir ==  1 && volCond) OnSignal("DIV",  1, d, vol);

            int s = StreakDir();
            if (s != 0 && (!UseNarrowFilter || IsNarrowRange()))
            {
                OnSignal("STR_WITH",    s, d, vol);
                OnSignal("STR_AGAINST", -s, d, vol);
            }
        }

        // ================= Сигнал и вход =================
        private void OnSignal(string book, int dir, decimal delta, decimal vol)
        {
            DateTime t = Bars.Time[0], paris = ToParis(t);
            string id = string.Format(CI, "{0}_{1:yyyyMMddHHmm}_{2}", _sym, t, book);
            double entry = Bars.Close[0];
            double sl = FindFractal(dir > 0 ? -1 : 1, entry);   // стоп — фрактал против сделки
            double tp = FindFractal(dir > 0 ? 1 : -1, entry);   // тейк — фрактал по ходу сделки
            bool news = InNews(paris);
            bool zeroData = false;
            for (int i = 0; i < _zero.Count; i++) zeroData |= _zero[i];

            Pos cur;
            _pos.TryGetValue(book, out cur);
            string skip = "";
            if (paris.DayOfWeek == DayOfWeek.Saturday || paris.DayOfWeek == DayOfWeek.Sunday) skip = "WEEKEND";
            else if (news)                        skip = "NEWS";
            else if (zeroData)                    skip = "ZERO_DATA";
            else if (_disabledBooks.Contains(book)) skip = "RULES";
            else if (cur != null && cur.Dir == dir) skip = "IN_POSITION";

            double atr = this.AverageTrueRange(AtrPeriod);
            Write("signals_" + _sym + ".csv",
                "Time,ParisTime,Symbol,SignalId,Book,Dir,Delta,Volume,RangeATR,Session,News,ZeroData,Entry,SL,TP,SL_pips,TP_pips,Taken,SkipReason,Source,RulesVersion",
                t, string.Format(CI, "{0:yyyy-MM-dd HH:mm},{1:yyyy-MM-dd HH:mm},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20}",
                    t, paris, _sym, id, book, dir, delta, vol,
                    atr > 0 ? F((Bars.High[0] - Bars.Low[0]) / atr) : "",
                    Session(paris), news ? 1 : 0, zeroData ? 1 : 0,
                    F(entry), F(sl), F(tp), F(Math.Abs(entry - sl) / PipSize), F(Math.Abs(tp - entry) / PipSize),
                    skip == "" ? 1 : 0, skip, Source(), _rulesVersion));

            _pending.Add(new Pending { Id = id, Time = t, Dir = dir, Entry = entry, SL = sl, TP = tp, Hypo = "NONE" });

            if (skip != "") return;
            if (cur != null) ClosePos(cur, entry, "SIGNAL", false);   // встречный сигнал — переворот
            _pos[book] = new Pos { Dir = dir, Entry = entry, SL = sl, TP = tp, Time = t, Id = id, Book = book };
        }

        // ================= Выходы =================
        private void CheckExits()
        {
            foreach (Pos p in new List<Pos>(_pos.Values))
            {
                bool amb;
                string hit = Hit(p.Dir, p.SL, p.TP, out amb);
                if (hit != null) ClosePos(p, hit == "SL" ? p.SL : p.TP, hit, amb);
            }
        }

        private void ClosePos(Pos p, double price, string exit, bool amb)
        {
            _pos.Remove(p.Book);
            DateTime t = Bars.Time[0];
            double diff = p.Dir * (price - p.Entry);
            Write("trades.csv",
                "CloseTime,ParisTime,Symbol,SignalId,Book,Dir,OpenTime,OpenPrice,SL,TP,ClosePrice,Exit,Pips,Usd,Ambiguous,Source,RulesVersion",
                t, string.Format(CI, "{0:yyyy-MM-dd HH:mm},{1:yyyy-MM-dd HH:mm},{2},{3},{4},{5},{6:yyyy-MM-dd HH:mm},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16}",
                    t, ToParis(t), _sym, p.Id, p.Book, p.Dir, p.Time, F(p.Entry), F(p.SL), F(p.TP), F(price), exit,
                    F(diff / PipSize), F(diff * Bars.Info.BigPointValue - CommissionUsd), amb ? 1 : 0, Source(), _rulesVersion));
        }

        // Что задето на текущем часовом баре: "SL", "TP" или null.
        private string Hit(int dir, double sl, double tp, out bool amb)
        {
            amb = false;
            bool hitSL = !double.IsNaN(sl) && (dir > 0 ? Bars.Low[0] <= sl : Bars.High[0] >= sl);
            bool hitTP = !double.IsNaN(tp) && (dir > 0 ? Bars.High[0] >= tp : Bars.Low[0] <= tp);
            if (hitSL && hitTP) return ResolveByMinutes(dir, sl, tp, out amb);
            return hitSL ? "SL" : hitTP ? "TP" : null;
        }

        // Стоп и тейк в одном часе: смотрим минутки (поток данных 2) от старой к новой.
        private string ResolveByMinutes(int dir, double sl, double tp, out bool amb)
        {
            amb = true;
            if (MaxDataStream < 2) return "SL";
            var m = BarsOfData(2);
            DateTime from = Bars.Time[1];
            int k = 0;
            while (k < 120 && m.CurrentBar > k && m.Time[k] > from) k++;
            for (int i = k - 1; i >= 0; i--)
            {
                bool s = dir > 0 ? m.Low[i] <= sl : m.High[i] >= sl;
                bool t = dir > 0 ? m.High[i] >= tp : m.Low[i] <= tp;
                if (s && t) return "SL";
                if (s || t) { amb = false; return s ? "SL" : "TP"; }
            }
            return "SL";
        }

        // ================= Ход цены после сигнала =================
        private void UpdatePending()
        {
            for (int n = _pending.Count - 1; n >= 0; n--)
            {
                Pending p = _pending[n];
                p.Bars++;
                p.Up = Math.Max(p.Up, Bars.High[0] - p.Entry);
                p.Dn = Math.Max(p.Dn, p.Entry - Bars.Low[0]);
                int c = Array.IndexOf(Checkpoints, p.Bars);
                if (c >= 0) { p.UpAt[c] = p.Up / PipSize; p.DnAt[c] = p.Dn / PipSize; }
                if (p.Hypo == "NONE")
                {
                    bool amb;
                    string hit = Hit(p.Dir, p.SL, p.TP, out amb);
                    if (hit != null)
                    {
                        p.Hypo = hit; p.HypoAmb = amb;
                        p.HypoPips = p.Dir * ((hit == "SL" ? p.SL : p.TP) - p.Entry) / PipSize;
                    }
                }
                if (p.Bars < Horizon) continue;
                if (p.Hypo == "NONE") p.HypoPips = p.Dir * (Bars.Close[0] - p.Entry) / PipSize;
                var cols = new List<string>();
                for (int i = 0; i < Checkpoints.Length; i++) { cols.Add(F(p.UpAt[i])); cols.Add(F(p.DnAt[i])); }
                Write("moves_" + _sym + ".csv",
                    "Time,ParisTime,Symbol,SignalId,Up1,Dn1,Up3,Dn3,Up6,Dn6,Up12,Dn12,Up24,Dn24,HypoResult,HypoPips,HypoAmbiguous",
                    p.Time, string.Format(CI, "{0:yyyy-MM-dd HH:mm},{1:yyyy-MM-dd HH:mm},{2},{3},{4},{5},{6},{7}",
                        p.Time, ToParis(p.Time), _sym, p.Id, string.Join(",", cols), p.Hypo, F(p.HypoPips), p.HypoAmb ? 1 : 0));
                _pending.RemoveAt(n);
            }
        }

        // ================= Фракталы =================
        // side=+1: ближайший по времени подтверждённый верхний фрактал выше цены (+ отступ);
        // side=-1: нижний фрактал ниже цены (- отступ). Нет — NaN.
        private double FindFractal(int side, double price)
        {
            double off = FractalOffsetTicks * Bars.Info.MinMove / Bars.Info.PriceScale;
            int last = Math.Min(FractalLookback, Bars.CurrentBar - 3);
            for (int i = 2; i <= last; i++)
            {
                if (side > 0)
                {
                    double h = Bars.High[i];
                    if (h > price && h > Bars.High[i - 1] && h > Bars.High[i - 2] && h > Bars.High[i + 1] && h > Bars.High[i + 2])
                        return h + off;
                }
                else
                {
                    double l = Bars.Low[i];
                    if (l < price && l < Bars.Low[i - 1] && l < Bars.Low[i - 2] && l < Bars.Low[i + 1] && l < Bars.Low[i + 2])
                        return l - off;
                }
            }
            return double.NaN;
        }

        // ================= Логика сигналов DivergenceDetector v4.6 =================
        private int PriceDir()
        {
            bool up = true, dn = true;
            for (int i = 0; i < NBars - 1; i++)
            {
                if (Bars.Close[i] <= Bars.Close[i + 1]) up = false;
                if (Bars.Close[i] >= Bars.Close[i + 1]) dn = false;
            }
            return up ? 1 : dn ? -1 : 0;
        }

        private int DeltaDir()
        {
            bool up = true, dn = true;
            for (int i = 0; i < NBars; i++)
            {
                if (_delta[i] <= 0) up = false;
                if (_delta[i] >= 0) dn = false;
            }
            return up ? 1 : dn ? -1 : 0;
        }

        private int StreakDir()
        {
            bool up = true, dn = true;
            for (int i = 1; i <= StreakBars; i++)
            {
                if (_delta[i] <= 0) up = false;
                if (_delta[i] >= 0) dn = false;
            }
            return up ? 1 : dn ? -1 : 0;
        }

        private bool IsNarrowRange()
        {
            if (Bars.CurrentBar <= Math.Max(StreakBars, AtrPeriod) + 1) return false;
            double hh = double.MinValue, ll = double.MaxValue;
            for (int i = 1; i <= StreakBars; i++)
            {
                hh = Math.Max(hh, Bars.High[i]);
                ll = Math.Min(ll, Bars.Low[i]);
            }
            double atr = this.AverageTrueRange(AtrPeriod);
            return atr > 0 && (hh - ll) / atr <= MaxRangeATR;
        }

        private decimal GetDelta(out decimal volume, out bool zero)
        {
            zero = true;
            volume = (decimal)Bars.Volume[0];
            var vp = this.VolumeProfile;
            if (vp == null) return 0;
            var svc = vp as IProfilesCollectionService;
            if (svc != null) svc.Syncronize();
            IProfile p = null;
            try { p = vp.ItemForBar(Bars.FullSymbolData.Current); } catch { }
            if (p == null || p.Empty) return 0;
            decimal ask = 0, bid = 0;
            foreach (ILevel lv in p.Values) { ask += lv.AskTradedValue; bid += lv.BidTradedValue; }
            volume = ask + bid;
            zero = volume == 0;
            return ask - bid;
        }

        // ================= Время, правила, файлы =================
        private DateTime ToParis(DateTime t)
        {
            try { return TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(t, DateTimeKind.Unspecified), _chartTz, _paris); }
            catch { return t; }   // несуществующий час при переводе часов
        }

        private bool InNews(DateTime paris)
        {
            foreach (TimeSpan[] w in _news)
                if (paris.TimeOfDay >= w[0] && paris.TimeOfDay <= w[1]) return true;
            return false;
        }

        private static string Session(DateTime paris)
        {
            double h = paris.TimeOfDay.TotalHours;
            return h < 8 ? "ASIA" : h < 14.5 ? "EUROPE" : h < 22 ? "AMERICA" : "OFF";
        }

        private string Source() { return Environment.IsRealTimeCalc ? "LIVE" : "HIST"; }

        private static string F(double v) { return double.IsNaN(v) ? "" : v.ToString("0.######", CI); }

        // rules.csv: "#version,N" и строки "Symbol,Book,Enabled"; Symbol "*" или начало имени инструмента.
        private void LoadRules()
        {
            _disabledBooks = new HashSet<string>();
            _rulesVersion = 0;
            string path = Path.Combine(LogDir, "rules.csv");
            if (!File.Exists(path)) return;
            foreach (string line in File.ReadAllLines(path))
            {
                string[] p = line.Split(',');
                if (p.Length < 2) continue;
                if (p[0].Trim() == "#version") { int.TryParse(p[1].Trim(), out _rulesVersion); continue; }
                if (p.Length < 3) continue;
                string s = p[0].Trim();
                if (s != "*" && !Bars.Info.Name.StartsWith(s, StringComparison.OrdinalIgnoreCase)) continue;
                if (p[2].Trim() == "0") _disabledBooks.Add(p[1].Trim());
                else _disabledBooks.Remove(p[1].Trim());
            }
        }

        // Дописывает строку, если её время новее последней записанной строки этого инструмента в файле
        // (пересчёт истории после перезапуска не дублирует строки). Файл занят (открыт в Excel) —
        // строка ждёт в очереди до следующей записи.
        private void Write(string file, string header, DateTime key, string line)
        {
            string path = Path.Combine(LogDir, file);
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
                Output.WriteLine("[DDAutoTrader] {0} занят, в очереди {1} строк: {2}", file, queue.Count, ex.Message);
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
