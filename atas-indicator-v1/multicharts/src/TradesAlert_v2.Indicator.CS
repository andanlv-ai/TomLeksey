using System;
using System.Collections.Generic;
using System.Drawing;
using PowerLanguage.VolumeProfile;

namespace PowerLanguage.Indicator
{
    [UpdateOnEveryTick(true)]
    [SameAsSymbol(false)]
    public class TradesAlert_v2 : IndicatorObject
        // === VERSION: 2026-04-27-01 ===
    {
        // Ask or Bid must occupy >= this % of level TotalValue to count as imbalance
        [Input] public double LevelImbalancePct { get; set; }

        // Minimum consecutive imbalance levels to fire a signal
        [Input] public int MinStackHeight { get; set; }

        // Print level log once per N ticks on last bar (0 = disabled)
        [Input] public int LogEveryNTicks { get; set; }

        private IPlotObject _signal;
        private int _tickCount;

        public TradesAlert_v2(object ctx) : base(ctx)
        {
            LevelImbalancePct = 30.0;
            MinStackHeight    = 3;
            LogEveryNTicks    = 50;
        }

        protected override void Create()
        {
            // +1 = Buy imbalance stack found, -1 = Sell, 0 = none
            _signal = AddPlot(new PlotAttributes("ImbalanceSignal", EPlotShapes.Histogram,
                                                  Color.Gray, Color.Empty, 6, 0, true));
        }

        protected override void StartCalc()
        {
            _tickCount = 0;
            // Логирование отключено
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



            // 1. Найти максимальный TotalValue среди всех уровней
            decimal maxTotalValue = 0m;
            foreach (ILevel level in profile.Values)
            {
                decimal total = level.TotalValue > 0m ? level.TotalValue : (level.AskTradedValue + level.BidTradedValue);
                if (total > maxTotalValue) maxTotalValue = total;
            }
            double minTotalValue = (double)maxTotalValue * 0.35;

            // 2. Считаем признаки с учётом фильтра по объёму
            var signs = new List<int>();
            var deltaPcts = new List<double>();
            var totalValues = new List<double>();
            foreach (ILevel level in profile.Values)
            {
                decimal total = level.TotalValue > 0m ? level.TotalValue : (level.AskTradedValue + level.BidTradedValue);
                double totalD = (double)total;
                totalValues.Add(totalD);
                if (totalD <= 0.0) { signs.Add(0); deltaPcts.Add(0.0); continue; }

                double ask = (double)level.AskTradedValue;
                double bid = (double)level.BidTradedValue;
                double delta = ask - bid;
                double deltaPct = delta / (ask + bid) * 100.0;
                deltaPcts.Add(deltaPct);
                // Только если объём >= 35% от максимального
                if (totalD >= minTotalValue) {
                    if      (deltaPct >= LevelImbalancePct) signs.Add(1);
                    else if (deltaPct <= -LevelImbalancePct) signs.Add(-1);
                    else                                    signs.Add(0);
                } else {
                    signs.Add(0);
                }
            }

            // 3. Поиск максимальных стэков Buy/Sell
            int maxBuy = 0, maxSell = 0, i = 0;
            while (i < signs.Count)
            {
                if (signs[i] == 0) { i++; continue; }
                int sign = signs[i], start = i;
                while (i < signs.Count && signs[i] == sign) i++;
                int len = i - start;
                if (sign ==  1 && len > maxBuy)  maxBuy  = len;
                if (sign == -1 && len > maxSell) maxSell = len;
            }

            bool hasBuy  = maxBuy  >= MinStackHeight;
            bool hasSell = maxSell >= MinStackHeight;

            if (hasBuy && !hasSell)
            {
                _signal.Set(0, 1);
                _signal.Colors[0] = Color.Lime;
            }
            else if (hasSell && !hasBuy)
            {
                _signal.Set(0, -1);
                _signal.Colors[0] = Color.Red;
            }
            else if (hasBuy && hasSell)
            {
                // Both present — show the longer one
                _signal.Set(0, maxBuy >= maxSell ? 1 : -1);
                _signal.Colors[0] = maxBuy >= maxSell ? Color.Lime : Color.Red;
            }
            else
            {
                _signal.Set(0, 0);
                _signal.Colors[0] = Color.Gray;
            }

            // Логирование отключено
        }
    }
}