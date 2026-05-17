// DivergenceDetector.Indicator.MC.CS — дивергенция: цена vs Ask/Bid дельта (VolumeProfile)
// Версия: 2026-05-17-v4.6
// Путь: C:\ProgramData\TS Support\MultiCharts .NET64 Special Edition\StudyServer\Techniques\CS\DivergenceDetector.Indicator.CS
//
// Требует: на чарте включён Volume Profile (Format Instrument → Volume Profile):
//   - Show Volume Profile = ON
//   - Type = Total Volume
//   - Breakdown by = Ask vs Bid Traded Delta
//   - Time → Every: 1 Bar
//
// v4.1 fix: delta-история через собственный rolling buffer.
//   GetBarDelta() вызывается только с barsAgo=0 (текущий бар — ItemForBar работает надёжно).
//   Для проверки N предыдущих баров используется _deltaHistory, а не повторный ItemForBar(absBar-N),
//   который на исторических барах возвращает null и ломает N-bar consecutive логику.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using PowerLanguage.Function;
using PowerLanguage.VolumeProfile;

namespace PowerLanguage.Indicator
{
    [UpdateOnEveryTick(false)]
    [SameAsSymbol(false)]
    public class DivergenceDetector : IndicatorObject
    {
        [Input] public int    NBars        { get; set; }
        [Input] public int    StreakBars   { get; set; }
        [Input] public int    AtrPeriod    { get; set; }
        [Input] public double MaxRangeATR  { get; set; }
        [Input] public bool   UseNRFilter  { get; set; }
        [Input] public int    NRLookback   { get; set; }
        [Input] public bool   LogEveryBar  { get; set; }
        [Input] public bool   EmailEnabled { get; set; }
        [Input] public string AlertToEmail { get; set; }
        [Input] public string SmtpHost     { get; set; }
        [Input] public int    SmtpPort     { get; set; }
        [Input] public bool   SmtpUseSsl   { get; set; }
        [Input] public string SmtpUser         { get; set; }
        [Input] public string SmtpPasswordFile { get; set; }  // путь к файлу с паролем

        private IPlotObject   _plot;
        private IPlotObject   _plotStreak;
        private List<decimal> _deltaHistory;  // [0]=текущий, [1]=1 бар назад, ...
        private decimal       _prevVolume;
        private int           _lastAlertBar;
        private bool          _prevEmailEnabled;

        public DivergenceDetector(object ctx) : base(ctx)
        {
            NBars            = 2;
            StreakBars       = 3;
            AtrPeriod        = 14;
            MaxRangeATR      = 0.7;
            UseNRFilter      = false;
            NRLookback       = 7;
            LogEveryBar      = false;
            EmailEnabled     = false;
            AlertToEmail     = "andan@mateos.lv";
            SmtpHost         = "192.168.1.211";
            SmtpPort         = 587;
            SmtpUseSsl       = true;
            SmtpUser         = "andan@mateos.lv";
            SmtpPasswordFile = @"C:\Users\user\.smtp_pass";
        }

        protected override void Create()
        {
            _plot       = AddPlot(new PlotAttributes("Divergence", EPlotShapes.Histogram,
                                                     Color.Gray, Color.Empty, 4, 0, true));
            _plotStreak = AddPlot(new PlotAttributes("Streak",     EPlotShapes.Histogram,
                                                     Color.Blue,   Color.Empty, 4, 0, true));
        }

        protected override void StartCalc()
        {
            if (NBars < 2) NBars = 2;
            if (StreakBars < 3) StreakBars = 3;
            if (AtrPeriod < 1) AtrPeriod = 1;
            if (NRLookback < 2) NRLookback = 2;
            _deltaHistory = new List<decimal>();
            _prevVolume   = 0;
            _lastAlertBar = -1;
            Output.WriteLine("=== DivergenceDetector v4.6 START [{0}] {1} ===",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Bars.Info.Name);
            Output.WriteLine("    Params: NBars={0} StreakBars={1} AtrPeriod={2} MaxRangeATR={3} UseNRFilter={4}",
                NBars, StreakBars, AtrPeriod, MaxRangeATR, UseNRFilter);

            if (EmailEnabled && !_prevEmailEnabled)
                SendAlertEmail("TEST_EMAIL_OK", 0, 0, 0);
            _prevEmailEnabled = EmailEnabled;
        }

        protected override void CalcBar()
        {
            int absBar = Bars.FullSymbolData.Current;

            // диагностика первых 5 баров
            if (Bars.CurrentBar <= 5)
            {
                var vpDiag = this.VolumeProfile;
                int itemsCount = -1;
                bool pEmpty = true;
                decimal askD = 0, bidD = 0;
                try
                {
                    var svc = vpDiag as IProfilesCollectionService;
                    if (svc != null) svc.Syncronize();
                    if (vpDiag != null)
                    {
                        var items = vpDiag.Items;
                        if (items != null) { foreach (var _ in items) itemsCount = (itemsCount < 0 ? 1 : itemsCount + 1); }
                        IProfile pp = null;
                        try { pp = vpDiag.ItemForBar(absBar); } catch { }
                        if (pp != null)
                        {
                            pEmpty = pp.Empty;
                            if (!pEmpty)
                            {
                                foreach (ILevel lv in pp.Values)
                                {
                                    askD += lv.AskTradedValue;
                                    bidD += lv.BidTradedValue;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex) { Output.WriteLine("DIAG ex: {0}", ex.Message); }

                Output.WriteLine("DIAG BAR#{0} absBar={1} items={2} pEmpty={3} ask={4} bid={5}",
                    Bars.CurrentBar, absBar, itemsCount, pEmpty, askD, bidD);
            }

            // Обновляем историю дельт (только barsAgo=0 — надёжно работает)
            decimal curVolume = 0;
            decimal curDelta  = GetCurrentDeltaAndVolume(out curVolume);
            _deltaHistory.Insert(0, curDelta);
            int maxHistory = Math.Max(NBars + 1, StreakBars + 1);
            if (_deltaHistory.Count > maxHistory)
                _deltaHistory.RemoveAt(_deltaHistory.Count - 1);

            bool volCondition = curVolume > _prevVolume;
            _prevVolume = curVolume;

            if (Bars.CurrentBar <= NBars + 1) { _plot.Set(0, 0); _plotStreak.Set(0, 0); return; }

            int priceDir = GetPriceDir();
            int deltaDir = GetDeltaDir();

            // Дивергенция: Red выше нуля (+1), Green ниже нуля (-1)
            int   signal = 0;
            Color color  = Color.Gray;

            if      (priceDir ==  1 && deltaDir == -1 && volCondition) { signal = 1; color = Color.Red;  }
            else if (priceDir == -1 && deltaDir ==  1 && volCondition) { signal = 1; color = Color.Lime; }

            _plot.Set(0, signal);
            _plot.Colors[0] = color;

            // Streak: синий ниже нуля когда StreakBars+ баров дельта одного знака + цена в узком диапазоне
            int streakDir = GetStreakDir();
            bool narrow = streakDir != 0 && IsNarrowRange();
            if (narrow)
            {
                _plotStreak.Set(0, -1);
                _plotStreak.Colors[0] = Color.Blue;
            }
            else
            {
                _plotStreak.Set(0, 0);
                _plotStreak.Colors[0] = Color.Gray;
            }

            if (signal == 1 && EmailEnabled && Bars.LastBarOnChart && _lastAlertBar != Bars.CurrentBar)
            {
                _lastAlertBar = Bars.CurrentBar;
                string signalType = color == Color.Red ? "BEARISH" : "BULLISH";
                SendAlertEmail(signalType, priceDir, deltaDir, curDelta);
            }

            if (LogEveryBar || Bars.LastBarOnChart)
            {
                double atrVal = this.AverageTrueRange(AtrPeriod);
                double hh = double.MinValue, ll = double.MaxValue;
                for (int i = 1; i <= StreakBars; i++)
                {
                    if (Bars.High[i] > hh) hh = Bars.High[i];
                    if (Bars.Low[i]  < ll) ll = Bars.Low[i];
                }
                double rangeRatio = atrVal > 0 ? (hh - ll) / atrVal : -1;
                Output.WriteLine("BAR#{0} price={1} delta={2} sig={3} streak={4} narrow={5} rangeATR={6:F3} rawDelta={7} vol={8}",
                    Bars.CurrentBar, priceDir, deltaDir, signal, streakDir, narrow, rangeRatio, curDelta, curVolume);
            }
        }

        private int GetPriceDir()
        {
            bool up = true, dn = true;
            for (int i = 0; i < NBars - 1; i++)
            {
                if (Bars.Close[i] <= Bars.Close[i + 1]) up = false;
                if (Bars.Close[i] >= Bars.Close[i + 1]) dn = false;
            }
            return up ? 1 : dn ? -1 : 0;
        }

        // direction дельты по N последним барам — читает из _deltaHistory, не вызывает ItemForBar
        private int GetDeltaDir()
        {
            if (_deltaHistory.Count < NBars) return 0;
            bool up = true, dn = true;
            for (int i = 0; i < NBars; i++)
            {
                decimal d = _deltaHistory[i];
                if (d <= 0) up = false;
                if (d >= 0) dn = false;
            }
            return up ? 1 : dn ? -1 : 0;
        }

        // StreakBars+ предыдущих баров (без текущего) одного знака дельты
        private int GetStreakDir()
        {
            if (_deltaHistory.Count <= StreakBars) return 0;
            bool up = true, dn = true;
            for (int i = 1; i <= StreakBars; i++)
            {
                if (_deltaHistory[i] <= 0) up = false;
                if (_deltaHistory[i] >= 0) dn = false;
            }
            return up ? 1 : dn ? -1 : 0;
        }

        // Возвращает true если размах окна StreakBars предыдущих баров узкий относительно ATR
        private bool IsNarrowRange()
        {
            if (Bars.CurrentBar <= Math.Max(StreakBars, AtrPeriod) + 1) return false;

            double hh = double.MinValue, ll = double.MaxValue;
            for (int i = 1; i <= StreakBars; i++)
            {
                if (Bars.High[i] > hh) hh = Bars.High[i];
                if (Bars.Low[i]  < ll) ll = Bars.Low[i];
            }

            double atrVal = this.AverageTrueRange(AtrPeriod);
            if (atrVal <= 0) return false;
            if ((hh - ll) / atrVal > MaxRangeATR) return false;

            if (UseNRFilter)
            {
                double curRange = Bars.High[0] - Bars.Low[0];
                for (int i = 1; i <= NRLookback; i++)
                    if ((Bars.High[i] - Bars.Low[i]) <= curRange) return false;
            }
            return true;
        }

        private void SendAlertEmail(string signalType, int priceDir, int deltaDir, decimal delta)
        {
            string symbol = Bars.Info.Name;
            string subj   = string.Format("[{0}] DIVERGENCE {1}", symbol, signalType);
            string body   = string.Format(
                "Symbol:    {0}\nSignal:    {1} divergence\nPriceDir:  {2}\nDeltaDir:  {3}\nDelta:     {4}\nUTC:       {5:yyyy-MM-dd HH:mm:ss}",
                symbol, signalType,
                priceDir ==  1 ? "UP" : priceDir == -1 ? "DOWN" : "FLAT",
                deltaDir ==  1 ? "UP" : deltaDir == -1 ? "DOWN" : "FLAT",
                delta, DateTime.UtcNow);

            string host = SmtpHost; int port = SmtpPort; bool ssl = SmtpUseSsl;
            string user = SmtpUser; string to = AlertToEmail;
            string from = string.IsNullOrWhiteSpace(user) ? "alert@localhost" : user;
            string pass = "";
            try { if (!string.IsNullOrWhiteSpace(SmtpPasswordFile)) pass = System.IO.File.ReadAllText(SmtpPasswordFile).Trim(); }
            catch (Exception ex) { Output.WriteLine("SMTP password file error: {0}", ex.Message); }

            Task.Run(() =>
            {
                try
                {
                    ServicePointManager.ServerCertificateValidationCallback =
                        (sender, cert, chain, errors) => true;
                    using (var client = new SmtpClient(host, port))
                    {
                        client.EnableSsl      = ssl;
                        client.DeliveryMethod = SmtpDeliveryMethod.Network;
                        client.Timeout        = 10000;
                        if (!string.IsNullOrWhiteSpace(user))
                            client.Credentials = new NetworkCredential(user, pass);
                        using (var msg = new MailMessage(from, to, subj, body))
                            client.Send(msg);
                    }
                }
                catch { }
            });
        }

        // дельта и объём текущего бара из VolumeProfile; fallback объёма — Bars.Volume[0]
        private decimal GetCurrentDeltaAndVolume(out decimal volume)
        {
            volume = 0;
            var vp = this.VolumeProfile;
            if (vp != null)
            {
                var svc = vp as IProfilesCollectionService;
                if (svc != null) svc.Syncronize();

                IProfile p = null;
                try { p = vp.ItemForBar(Bars.FullSymbolData.Current); } catch { }
                if (p != null && !p.Empty)
                {
                    decimal ask = 0, bid = 0;
                    foreach (ILevel lv in p.Values)
                    {
                        ask += lv.AskTradedValue;
                        bid += lv.BidTradedValue;
                    }
                    volume = ask + bid;
                    return ask - bid;
                }
            }
            volume = (decimal)Bars.Volume[0];
            return 0;
        }
    }
}
