// TradesAlert_v2.Indicator.CS — копия для MultiCharts
// Автоматически скопировано 01.05.2026
// Путь для копирования: C:\ProgramData\TS Support\MultiCharts .NET64 Special Edition\StudyServer\Techniques\CS\TradesAlert_v2.Indicator.CS
// Версия: v3.2 — алерт по тику N нового бара (не при закрытии), лог показывает сигнал в шапке

using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using PowerLanguage.VolumeProfile;

namespace PowerLanguage.Indicator

{
    [UpdateOnEveryTick(true)]
    [SameAsSymbol(false)]
    public class TradesAlert_v2 : IndicatorObject
    {
        // === VERSION: 2026-05-01-v3.2 ===
        [Input] public double LevelImbalancePct { get; set; }
        [Input] public int    MinStackHeight    { get; set; }
        [Input] public int    LogEveryNTicks    { get; set; }

        private IPlotObject _signal;
        private int _tickCount;
        private int _barTickCount;
        private int _lastCalcBar;

        public TradesAlert_v2(object ctx) : base(ctx)
        {
            LevelImbalancePct = 35.0;
            MinStackHeight    = 3;
            LogEveryNTicks    = 50;
        }

        protected override void Create()
        {
            _signal = AddPlot(new PlotAttributes("ImbalanceSignal", EPlotShapes.Histogram,
                                                  Color.Gray, Color.Empty, 6, 0, true));
        }

        protected override void StartCalc()
        {
            _tickCount    = 0;
            _barTickCount = 0;
            _lastCalcBar  = -1;
            Output.WriteLine("=== TradesAlert_v2 v3.2 START [{0}] {1} ===",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Bars.Info.Name);
            Output.WriteLine("    Логика: сигнал = {0} подряд уровней где |Ask-Bid| >= {1}% от maxTotal",
                MinStackHeight, LevelImbalancePct);
            Output.WriteLine("    Params: LevelImbalancePct={0}% MinStackHeight={1} LogEveryNTicks={2}",
                LevelImbalancePct, MinStackHeight, LogEveryNTicks);
        }

        private IProfile GetProfile()
        {
            var svc = this.VolumeProfile as IProfilesCollectionService;
            if (svc != null) svc.Syncronize();
            var vp = this.VolumeProfile;
            if (vp == null) return null;
            try { IProfile p = vp.ItemForBar(Bars.CurrentBar); if (p != null) return p; }
            catch { }
            foreach (IProfile pp in vp.Items) return pp;
            return null;
        }

        protected override void CalcBar()
        {
            IProfile profile = GetProfile();
            if (profile == null || profile.Empty)
            {
                _signal.Set(0, 0);
                return;
            }

            bool isLastBar = Bars.LastBarOnChart;

            if (isLastBar)
            {
                if (Bars.CurrentBar != _lastCalcBar)
                {
                    _lastCalcBar  = Bars.CurrentBar;
                    _barTickCount = 0;
                }
                _barTickCount++;
            }

            // --- Шаг 1: Собираем уровни, находим maxTotal ---
            var levels = new List<LevelData>();
            double maxTotal = 0.0;

            foreach (ILevel level in profile.Values)
            {
                double ask   = (double)level.AskTradedValue;
                double bid   = (double)level.BidTradedValue;
                double total = ask + bid;
                if (total <= 0.0) continue;
                if (total > maxTotal) maxTotal = total;
                levels.Add(new LevelData { Price = (double)level.Price, Ask = ask, Bid = bid, Total = total });
            }

            // --- Шаг 2: Считаем порог в единицах объёма и знак каждого уровня ---
            // Условие дисбаланса: |Ask - Bid| >= LevelImbalancePct% от maxTotal
            double threshold = maxTotal * LevelImbalancePct / 100.0;

            foreach (var lv in levels)
            {
                double netDelta    = lv.Ask - lv.Bid;
                double absNetDelta = Math.Abs(netDelta);
                if (absNetDelta >= threshold)
                    lv.Sign = netDelta > 0 ? 1 : -1;
                else
                    lv.Sign = 0;
            }

            // --- Шаг 3: Поиск максимальных непрерывных стэков Buy и Sell ---
            int maxBuyStack  = 0;
            int maxSellStack = 0;
            int buyStackStart  = -1;
            int sellStackStart = -1;

            int i = 0;
            while (i < levels.Count)
            {
                if (levels[i].Sign == 0) { i++; continue; }
                int sign  = levels[i].Sign;
                int start = i;
                while (i < levels.Count && levels[i].Sign == sign) i++;
                int len = i - start;
                if (sign == 1 && len > maxBuyStack)  { maxBuyStack  = len; buyStackStart  = start; }
                if (sign == -1 && len > maxSellStack) { maxSellStack = len; sellStackStart = start; }
            }

            bool hasBuy  = maxBuyStack  >= MinStackHeight;
            bool hasSell = maxSellStack >= MinStackHeight;

            // --- Шаг 4: Выставляем сигнал ---
            int    plotVal   = 0;
            string plotSide  = "NONE";
            int    plotStart = -1;
            int    plotLen   = 0;

            if (hasBuy && !hasSell)
            {
                plotVal = 1; plotSide = "BUY"; plotStart = buyStackStart; plotLen = maxBuyStack;
                _signal.Set(0, 1);
                _signal.Colors[0] = Color.Lime;
            }
            else if (hasSell && !hasBuy)
            {
                plotVal = -1; plotSide = "SELL"; plotStart = sellStackStart; plotLen = maxSellStack;
                _signal.Set(0, -1);
                _signal.Colors[0] = Color.Red;
            }
            else if (hasBuy && hasSell)
            {
                if (maxBuyStack >= maxSellStack)
                {
                    plotVal = 1; plotSide = "BUY"; plotStart = buyStackStart; plotLen = maxBuyStack;
                    _signal.Set(0, 1);
                    _signal.Colors[0] = Color.Lime;
                }
                else
                {
                    plotVal = -1; plotSide = "SELL"; plotStart = sellStackStart; plotLen = maxSellStack;
                    _signal.Set(0, -1);
                    _signal.Colors[0] = Color.Red;
                }
            }
            else
            {
                _signal.Set(0, 0);
                _signal.Colors[0] = Color.Gray;
            }

            _tickCount++;

            // --- Логирование и алерт ---
            // Алерт и лог: на формирующемся баре, после LogEveryNTicks тиков
            bool isAlertTick = isLastBar && _barTickCount == LogEveryNTicks;
            bool logFull     = isLastBar && (isAlertTick || _barTickCount % LogEveryNTicks == 0);
            if (logFull)
            {
                string instrument = Bars.Info.Name;
                string signalStr  = plotVal == 1 ? "BUY" : (plotVal == -1 ? "SELL" : "NONE");
                Output.WriteLine("=== CalcBar [{0}] BarTick#{1} TotalTick#{2} | {3} | SIGNAL={4} ===",
                    DateTime.Now.ToString("HH:mm:ss"), _barTickCount, _tickCount, instrument, signalStr);
                Output.WriteLine("    maxTotal={0:F1} threshold={1:F1} ({2}% от maxTotal)",
                    maxTotal, threshold, LevelImbalancePct);

                Output.WriteLine("[RESULT] maxBuyStack={0} maxSellStack={1} MinStackHeight={2} => hasBuy={3} hasSell={4} => Signal={5}",
                    maxBuyStack, maxSellStack, MinStackHeight, hasBuy, hasSell, plotSide);

                // Все уровни с разметкой
                Output.WriteLine("[LEVELS] Цена     | Ask     | Bid     | Total   | NetDelta | |ND|/max%  | Порог пройден | Знак");
                for (int k = 0; k < levels.Count; k++)
                {
                    var lv          = levels[k];
                    double netDelta = lv.Ask - lv.Bid;
                    double ndPct    = maxTotal > 0.0 ? Math.Abs(netDelta) / maxTotal * 100.0 : 0.0;
                    bool   passed   = Math.Abs(netDelta) >= threshold;
                    string signStr  = lv.Sign == 1 ? "+BUY" : (lv.Sign == -1 ? "-SELL" : " 0  ");
                    string flag     = passed ? "YES" : "no ";

                    // Помечаем уровни входящие в сигнальный стэк
                    string inStack = "";
                    if (plotStart >= 0 && k >= plotStart && k < plotStart + plotLen) inStack = " <<< STACK";

                    Output.WriteLine("  [{0,3}] {1,10:F5} | {2,7:F1} | {3,7:F1} | {4,7:F1} | {5,8:F1} | {6,9:F1}% | {7}            | {8}{9}",
                        k, lv.Price, lv.Ask, lv.Bid, lv.Total, netDelta, ndPct, flag, signStr, inStack);
                }

                // Цепочка знаков
                string chain = string.Join("", levels.Select(lv =>
                    lv.Sign == 1 ? "+" : (lv.Sign == -1 ? "-" : "0")));
                Output.WriteLine("[SIGNS] {0}", chain);

                if (plotVal != 0)
                {
                    Output.WriteLine("[SIGNAL FIRED] {0} | стэк из {1} уровней начиная с индекса {2} (цена {3:F2})",
                        plotSide, plotLen, plotStart, levels[plotStart].Price);
                    if (isAlertTick)
                    {
                        var sb = new System.Text.StringBuilder();
                        sb.AppendFormat("=== SIGNAL: {0} | {1} | {2} ===\n", plotSide, instrument, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                        sb.AppendFormat("Stack: {0} levels from index {1}, price {2:F5}\n", plotLen, plotStart, levels[plotStart].Price);
                        sb.AppendFormat("maxBuyStack={0} maxSellStack={1} MinStackHeight={2}\n", maxBuyStack, maxSellStack, MinStackHeight);
                        sb.AppendFormat("maxTotal={0:F1} threshold={1:F1} ({2}% of maxTotal)\n", maxTotal, threshold, LevelImbalancePct);
                        sb.AppendFormat("BarTick={0}\n\n", _barTickCount);
                        sb.AppendLine("Price        | Ask     | Bid     | Total   | NetDelta | ND%      | Sign");
                        for (int k = 0; k < levels.Count; k++)
                        {
                            var lv         = levels[k];
                            double nd      = lv.Ask - lv.Bid;
                            double ndPct2  = maxTotal > 0.0 ? Math.Abs(nd) / maxTotal * 100.0 : 0.0;
                            string signStr2 = lv.Sign == 1 ? "+BUY" : (lv.Sign == -1 ? "-SELL" : " 0  ");
                            string inStack2 = (plotStart >= 0 && k >= plotStart && k < plotStart + plotLen) ? " <<<" : "";
                            sb.AppendFormat("{0,12:F5} | {1,7:F1} | {2,7:F1} | {3,7:F1} | {4,8:F1} | {5,7:F1}% | {6}{7}\n",
                                lv.Price, lv.Ask, lv.Bid, lv.Total, nd, ndPct2, signStr2, inStack2);
                        }
                        sb.AppendFormat("\nSigns: {0}", chain);
                        this.Alerts.Alert(sb.ToString());
                    }
                }
                else
                {
                    Output.WriteLine("[NO SIGNAL] maxBuyStack={0} maxSellStack={1} — не достигли MinStackHeight={2}",
                        maxBuyStack, maxSellStack, MinStackHeight);
                }
            }
        }

        private class LevelData
        {
            public double Price;
            public double Ask;
            public double Bid;
            public double Total;
            public int    Sign;
        }

    }
}
