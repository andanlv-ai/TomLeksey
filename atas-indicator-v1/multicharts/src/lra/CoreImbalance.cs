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
