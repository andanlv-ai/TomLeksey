using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using PowerLanguage.Function;
using PowerLanguage.VolumeProfile;

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
                    tx0, F(e.Exit), e.Reason, F(pips), F(e.Pips * PipSize * Bars.Info.BigPointValue - CommissionUsd), src, Version));
            }
            if (_fast.Count != drawn0)   // диагностика: когда и сколько сделок нарисовано
                RunEvent(string.Format(CI, "FAST_DRAW status={0} rt={1} bar={2:yyyy-MM-dd HH:mm} trades={3}", Bars.Status,
                    Environment.IsRealTimeCalc, now, _fast.Count));
        }

        // MultiCharts мог стереть рисунки, созданные внутри бара, — тогда рисуем сделку заново
        private bool Lost(long key) { FastMarks m; return _fast.TryGetValue(key, out m) && !m.Entry.Exist; }

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
