using System;
using System.Collections.Generic;

namespace PowerLanguage.Strategy
{
    // Дисбаланс окна и итоги дня (ТЗ §3).
    public static class LraImb
    {
        // Дисбаланс окна h[from..to] включительно; доли — от итогов дня бара h[to].
        public static LraImbalance Measure(IList<LraBar> h, int from, int to, LraSettings s) { throw new NotImplementedException(); }

        // Объём и число сделок (UpTicks + DownTicks) торгового дня бара h[idx] (дата Paris), от начала дня до idx включительно.
        public static void DayTotals(IList<LraBar> h, int idx, out double volume, out double ticks) { throw new NotImplementedException(); }
    }
}
