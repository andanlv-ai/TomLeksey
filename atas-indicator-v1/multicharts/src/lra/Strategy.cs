using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using PowerLanguage.Function;
using PowerLanguage.VolumeProfile;

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
