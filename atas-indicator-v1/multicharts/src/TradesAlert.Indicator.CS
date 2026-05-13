using System;
using System.Drawing;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using PowerLanguage.VolumeProfile;

namespace PowerLanguage.Indicator
{
    [UpdateOnEveryTick(true)]
    [SameAsSymbol(false)]
    public class TradesAlert : IndicatorObject
    {
        public TradesAlert(object ctx) : base(ctx)
        {
            AlertThresholdPct = 2.0;
            HysteresisPct     = 1.5;
            AlertFromUtcHour  = 7;
            AlertToUtcHour    = 21;
            BarWidth          = 6;
            EmailEnabled      = false;
            AlertToEmail      = "andan@mateos.lv";
            SmtpHost          = "192.168.1.211";
            SmtpPort          = 25;
            SmtpUseSsl        = false;
            SmtpUser          = "";
            SmtpPassword      = "";
        }

        [Input] public double AlertThresholdPct { get; set; }
        [Input] public double HysteresisPct     { get; set; }
        [Input] public int    AlertFromUtcHour  { get; set; }
        [Input] public int    AlertToUtcHour    { get; set; }
        [Input] public int    BarWidth          { get; set; }
        [Input] public bool   EmailEnabled      { get; set; }
        [Input] public string AlertToEmail      { get; set; }
        [Input] public string SmtpHost          { get; set; }
        [Input] public int    SmtpPort          { get; set; }
        [Input] public bool   SmtpUseSsl        { get; set; }
        [Input] public string SmtpUser          { get; set; }
        [Input] public string SmtpPassword      { get; set; }

        private bool _alertFired = false;
        private bool _resetReady = false;
        private int  _tickCount  = 0;

        private IPlotObject _deltaPlot;

        protected override void Create()
        {
            _deltaPlot = AddPlot(new PlotAttributes("Delta%", EPlotShapes.Histogram,
                                                    Color.LimeGreen, Color.Empty, BarWidth, 0, true));
        }

        protected override void StartCalc()
        {
            _alertFired = false;
            _resetReady = false;
            _tickCount  = 0;
        }

        private void SyncProfile()
        {
            var svc = this.VolumeProfile as IProfilesCollectionService;
            if (svc != null) svc.Syncronize();
        }

        private IProfile GetProfile()
        {
            SyncProfile();
            var vp = this.VolumeProfile;
            if (vp == null) return null;
            try
            {
                IProfile p = vp.ItemForBar(Bars.CurrentBar);
                if (p != null) return p;
            }
            catch { }
            foreach (IProfile pp in vp.Items) return pp;
            return null;
        }

        protected override void CalcBar()
        {
            bool isRT   = Environment.IsRealTimeCalc;
            bool isLast = Bars.LastBarOnChart;

            IProfile profile = GetProfile();
            if (profile == null || profile.Empty)
            {
                _deltaPlot.Set(0, 0.0);
                return;
            }

            decimal totalAsk = 0m, totalBid = 0m;
            foreach (ILevel level in profile.Values)
            {
                totalAsk += level.AskTradedValue;
                totalBid += level.BidTradedValue;
            }

            decimal delta = totalAsk - totalBid;
            decimal total = totalAsk + totalBid;
            double signedPct = total > 0m ? (double)delta / (double)total * 100.0 : 0.0;

            _deltaPlot.Set(0, signedPct);
            _deltaPlot.Colors[0] = delta >= 0m ? Color.LimeGreen : Color.Red;

            if (total <= 0m) return;
            double pct = Math.Abs(signedPct);

            _tickCount++;

            if (isRT) CheckAlert(pct, delta, totalAsk, totalBid);
        }

        private bool IsInTradingHours()
        {
            int h = DateTime.UtcNow.Hour;
            return h >= AlertFromUtcHour && h < AlertToUtcHour;
        }

        private void CheckAlert(double pct, decimal delta, decimal ask, decimal bid)
        {
            if (!IsInTradingHours()) return;
            double resetLevel = AlertThresholdPct - HysteresisPct;

            if (!_alertFired)
            {
                if (pct >= AlertThresholdPct)
                {
                    _alertFired = true;
                    _resetReady = false;
                    if (EmailEnabled) SendAlertEmail(pct, delta, ask, bid);
                }
            }
            else
            {
                if (!_resetReady && pct <= resetLevel)
                {
                    _resetReady = true;
                }
                if (_resetReady && pct >= AlertThresholdPct)
                {
                    _alertFired = true;
                    _resetReady = false;
                    if (EmailEnabled) SendAlertEmail(pct, delta, ask, bid);
                }
            }
        }

        private void SendAlertEmail(double pct, decimal delta, decimal ask, decimal bid)
        {
            string symbol = Bars.Info.Name;
            string side   = delta >= 0m ? "BUY" : "SELL";
            string subj   = string.Format("[{0}] {1} D{2}{3} ({4:F1}%)",
                                symbol, side, delta >= 0m ? "+" : "", delta, pct);
            string body   = string.Format(
                "Symbol:  {0}\nSide:    {1}\nAsk:     {2}\nBid:     {3}\n" +
                "Delta:   {4}{5}\nDelta%:  {6:F2}%\nUTC:     {7:yyyy-MM-dd HH:mm:ss}",
                symbol, side, ask, bid,
                delta >= 0m ? "+" : "", delta, pct, DateTime.UtcNow);

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
                catch {}
            });
        }
    }
}
