// DivergenceDetector.Indicator.MC.CS — дивергенция: цена vs Ask/Bid дельта (VolumeProfile)
// Версия: 2026-05-15-v4.1
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
using PowerLanguage.VolumeProfile;

namespace PowerLanguage.Indicator
{
    [UpdateOnEveryTick(false)]
    [SameAsSymbol(false)]
    public class DivergenceDetector : IndicatorObject
    {
        [Input] public int    NBars        { get; set; }
        [Input] public bool   LogEveryBar  { get; set; }
        [Input] public bool   EmailEnabled { get; set; }
        [Input] public string AlertToEmail { get; set; }
        [Input] public string SmtpHost     { get; set; }
        [Input] public int    SmtpPort     { get; set; }
        [Input] public bool   SmtpUseSsl   { get; set; }
        [Input] public string SmtpUser     { get; set; }
        [Input] public string SmtpPassword { get; set; }

        private IPlotObject    _plot;
        private List<decimal>  _deltaHistory;  // [0]=текущий, [1]=1 бар назад, ...
        private int            _lastAlertBar;

        public DivergenceDetector(object ctx) : base(ctx)
        {
            NBars        = 2;
            LogEveryBar  = false;
            EmailEnabled = false;
            AlertToEmail = "";
            SmtpHost     = "";
            SmtpPort     = 587;
            SmtpUseSsl   = true;
            SmtpUser     = "";
            SmtpPassword = "";
        }

        protected override void Create()
        {
            _plot = AddPlot(new PlotAttributes("Divergence", EPlotShapes.Histogram,
                                               Color.Gray, Color.Empty, 4, 0, true));
        }

        protected override void StartCalc()
        {
            if (NBars < 2) NBars = 2;
            _deltaHistory = new List<decimal>();
            _lastAlertBar = -1;
            Output.WriteLine("=== DivergenceDetector v4.1 START [{0}] {1} ===",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Bars.Info.Name);
            Output.WriteLine("    Params: NBars={0}", NBars);
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
            decimal curDelta = GetCurrentDelta();
            _deltaHistory.Insert(0, curDelta);
            if (_deltaHistory.Count > NBars + 1)
                _deltaHistory.RemoveAt(_deltaHistory.Count - 1);

            if (Bars.CurrentBar <= NBars + 1) { _plot.Set(0, 0); return; }

            int priceDir = GetPriceDir();
            int deltaDir = GetDeltaDir();

            int   signal = 0;
            Color color  = Color.Gray;

            if      (priceDir ==  1 && deltaDir == -1) { signal = 1; color = Color.Red;  }
            else if (priceDir == -1 && deltaDir ==  1) { signal = 1; color = Color.Lime; }
            else if (priceDir ==  0 && deltaDir == -1) { signal = 1; color = Color.Red;  }
            else if (priceDir ==  0 && deltaDir ==  1) { signal = 1; color = Color.Lime; }

            _plot.Set(0, signal);
            _plot.Colors[0] = color;

            if (signal == 1 && EmailEnabled && Bars.LastBarOnChart && _lastAlertBar != Bars.CurrentBar)
            {
                _lastAlertBar = Bars.CurrentBar;
                string signalType = color == Color.Red ? "BEARISH" : "BULLISH";
                SendAlertEmail(signalType, priceDir, deltaDir, curDelta);
            }

            if (LogEveryBar || Bars.LastBarOnChart)
            {
                Output.WriteLine("BAR#{0} price={1} delta={2} sig={3}  rawDelta={4}",
                    Bars.CurrentBar, priceDir, deltaDir, signal, curDelta);
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
            string user = SmtpUser; string pass = SmtpPassword; string to = AlertToEmail;
            string from = string.IsNullOrWhiteSpace(user) ? "alert@localhost" : user;

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

        // дельта текущего бара: ask - bid traded (только barsAgo=0)
        private decimal GetCurrentDelta()
        {
            var vp = this.VolumeProfile;
            if (vp == null) return 0;

            var svc = vp as IProfilesCollectionService;
            if (svc != null) svc.Syncronize();

            IProfile p = null;
            try { p = vp.ItemForBar(Bars.FullSymbolData.Current); } catch { }
            if (p == null || p.Empty) return 0;

            decimal ask = 0, bid = 0;
            foreach (ILevel lv in p.Values)
            {
                ask += lv.AskTradedValue;
                bid += lv.BidTradedValue;
            }
            return ask - bid;
        }
    }
}
