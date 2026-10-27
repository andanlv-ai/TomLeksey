using System;
using System.Collections.Generic;

namespace PowerLanguage.Strategy
{
    // Дисбаланс окна и итоги дня (ТЗ §3).
    public static class LraImb
    {
        // Дисбаланс окна h[from..to] включительно; доли — от итогов дня бара h[to].
        public static LraImbalance Measure(IList<LraBar> h, int from, int to, LraSettings s)
        {
            var r = new LraImbalance();
            if (from < 0 || to >= h.Count || from > to) { r.Reason = "NO_DATA"; return r; }

            var lv = new Dictionary<long, double[]>(); // ключ — цена в тиках, значение {Ask, Bid}
            bool noData = false;
            for (int i = from; i <= to; i++)
            {
                LraBar b = h[i];
                if (b.NoData) noData = true;
                r.VolDelta += b.Ask - b.Bid;
                r.TickDelta += b.UpTicks - b.DownTicks;
                for (int k = 0; k < b.LvPrice.Length; k++)
                {
                    long key = (long)Math.Round(b.LvPrice[k] / s.Tick);
                    double[] ab;
                    if (!lv.TryGetValue(key, out ab)) { ab = new double[2]; lv[key] = ab; }
                    ab[0] += b.LvAsk[k];
                    ab[1] += b.LvBid[k];
                }
            }
            if (noData) { r.Reason = "NO_DATA"; return r; }

            double dayVol, dayTicks;
            DayTotals(h, to, out dayVol, out dayTicks);
            r.VolShare = dayVol > 0 ? r.VolDelta / dayVol : 0;
            r.TickShare = dayTicks > 0 ? r.TickDelta / dayTicks : 0;

            foreach (double[] ab in lv.Values)
            {
                double sum = ab[0] + ab[1];
                if (sum <= 0) continue;
                double imb = (ab[0] - ab[1]) / sum;
                if (imb >= s.LevelImbPct) r.BuyLevels++;
                else if (imb <= -s.LevelImbPct) r.SellLevels++;
            }

            if (Math.Abs(r.VolShare) >= s.MinShare && r.VolShare * r.TickShare < 0) { r.Reason = "CONFLICT"; return r; }
            if (Math.Abs(r.TickShare) < s.MinShare) { r.Reason = "WEAK"; return r; }
            if (r.TickShare > 0 && r.BuyLevels >= s.MinLevels) r.Dir = 1;
            else if (r.TickShare < 0 && r.SellLevels >= s.MinLevels) r.Dir = -1;
            else r.Reason = "ONE_PRICE";
            return r;
        }

        // Толпа по кумулятивной дельте по сделкам (автор: В2 19:17, 22:05; В4 02:28).
        // Начало роста — минимум (толпа покупает) или максимум (продаёт) кумулятивной дельты за CumLookback баров,
        // не ближе CumMinBars баров к текущему. Толпа есть, если:
        // рост / объём с начала роста >= CumWeight; дельта по объёму за то же время не против (CONFLICT, как в Measure);
        // в сторону толпы перекошено >= MinLevels цен; цена за время роста не вышла за хай (лоу) бара начала роста.
        // Из двух сторон берётся та, у которой вес больше. Возвращает дисбаланс окна from..last с Dir толпы или null.
        public static LraImbalance Cum(IList<LraBar> h, LraSettings s, out int from)
        {
            from = -1;
            int last = h.Count - 1;
            int b0 = Math.Max(0, last - s.CumLookback);   // база: кумулятивная дельта здесь = 0
            if (last - b0 < s.CumMinBars) return null;
            var c = new double[last - b0 + 1];
            for (int k = b0 + 1; k <= last; k++)
            {
                if (h[k].NoData) return null;
                c[k - b0] = c[k - b0 - 1] + h[k].UpTicks - h[k].DownTicks;
            }
            int mn = b0, mx = b0;
            for (int k = b0; k <= last - s.CumMinBars; k++)
            {
                if (c[k - b0] < c[mn - b0]) mn = k;
                if (c[k - b0] > c[mx - b0]) mx = k;
            }

            LraImbalance best = null; double bestW = 0;
            foreach (int d in new[] { 1, -1 })
            {
                int m = d > 0 ? mn : mx;
                double g = c[last - b0] - c[m - b0], vol = 0, vd = 0;
                for (int k = m + 1; k <= last; k++) { vol += h[k].UpTicks + h[k].DownTicks; vd += h[k].Ask - h[k].Bid; }
                if (vol <= 0 || g * d <= 0) continue;
                double w = Math.Abs(g) / vol;
                if (w < s.CumWeight || w <= bestW) continue;
                if (-d * vd / vol >= s.MinShare) continue;
                bool broke = false;
                for (int k = m + 1; k <= last; k++) if (d > 0 ? h[k].High > h[m].High : h[k].Low < h[m].Low) broke = true;
                if (broke) continue;
                LraImbalance r = Measure(h, m + 1, last, s);
                if ((d > 0 ? r.BuyLevels : r.SellLevels) < s.MinLevels) continue;
                r.Dir = d; r.Reason = "";
                best = r; bestW = w; from = m + 1;
            }
            return best;
        }

        // Объём и число сделок (UpTicks + DownTicks) торгового дня бара h[idx] (дата Paris), от начала дня до idx включительно.
        public static void DayTotals(IList<LraBar> h, int idx, out double volume, out double ticks)
        {
            volume = 0; ticks = 0;
            DateTime day = h[idx].Paris.Date;
            for (int i = idx; i >= 0 && h[i].Paris.Date == day; i--)
            {
                volume += h[i].Volume;
                ticks += h[i].UpTicks + h[i].DownTicks;
            }
        }
    }
}
