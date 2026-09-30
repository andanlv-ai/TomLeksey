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

            // Логика — тикеты 09, 10
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
