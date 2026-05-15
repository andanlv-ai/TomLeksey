// DivergenceDetector.Indicator.MC.CS — дивергенция: цена vs Ask/Bid дельта (VolumeProfile)
// Версия: 2026-05-15-v4.0
// Путь: C:\ProgramData\TS Support\MultiCharts .NET64 Special Edition\StudyServer\Techniques\CS\DivergenceDetector.Indicator.CS
//
// Требует: на чарте включён Volume Profile (Format Instrument → Volume Profile):
//   - Show Volume Profile = ON
//   - Type = Total Volume
//   - Breakdown by = Ask vs Bid Traded Delta
//   - Time → Every: 1 Bar
//
// Ключевое отличие от v1.5: ItemForBar требует АБСОЛЮТНЫЙ bar number = Bars.FullSymbolData.Current

using System;
using System.Collections.Generic;
using System.Drawing;
using PowerLanguage.VolumeProfile;

namespace PowerLanguage.Indicator
{
    [UpdateOnEveryTick(false)]
    [SameAsSymbol(false)]
    public class DivergenceDetector : IndicatorObject
    {
        [Input] public int  NBars       { get; set; }
        [Input] public bool LogEveryBar { get; set; }

        private IPlotObject _plot;
        private List<IProfile> _itemsCache;

        public DivergenceDetector(object ctx) : base(ctx)
        {
            NBars       = 2;
            LogEveryBar = false;
        }

        protected override void Create()
        {
            _plot = AddPlot(new PlotAttributes("Divergence", EPlotShapes.Histogram,
                                               Color.Gray, Color.Empty, 4, 0, true));
        }

        protected override void StartCalc()
        {
            if (NBars < 2) NBars = 2;
            _itemsCache = null;
            Output.WriteLine("=== DivergenceDetector v4.0 START [{0}] {1} ===",
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

            if (LogEveryBar || Bars.LastBarOnChart)
            {
                decimal curDelta = GetBarDelta(0);
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

        // direction дельты по последним N барам: 1 = все ask>bid, -1 = все bid>ask, 0 = смешанные
        private int GetDeltaDir()
        {
            bool up = true, dn = true;
            for (int i = 0; i < NBars; i++)
            {
                decimal d = GetBarDelta(i);
                if (d <= 0) up = false;
                if (d >= 0) dn = false;
            }
            return up ? 1 : dn ? -1 : 0;
        }

        // дельта для бара (i баров назад от текущего): ask - bid traded
        private decimal GetBarDelta(int barsAgo)
        {
            var vp = this.VolumeProfile;
            if (vp == null) return 0;

            var svc = vp as IProfilesCollectionService;
            if (svc != null) svc.Syncronize();

            int absBar = Bars.FullSymbolData.Current - barsAgo;

            // Шаг 1: ItemForBar с абсолютным номером
            IProfile p = null;
            try { p = vp.ItemForBar(absBar); } catch { }

            // Шаг 2: fallback на bulk-кэш по относительному индексу
            if (p == null || p.Empty)
            {
                if (_itemsCache == null)
                {
                    _itemsCache = new List<IProfile>();
                    try { foreach (IProfile it in vp.Items) _itemsCache.Add(it); } catch { }
                }
                if (_itemsCache.Count > 0)
                {
                    int idx = _itemsCache.Count - 1 - barsAgo;
                    if (idx >= 0 && idx < _itemsCache.Count) p = _itemsCache[idx];
                }
            }

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
