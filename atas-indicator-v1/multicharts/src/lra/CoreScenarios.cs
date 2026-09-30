using System;
using System.Collections.Generic;

namespace PowerLanguage.Strategy
{
    // Сценарии входа A и D с фильтром ЛРА (ТЗ §6).
    public static class LraScen
    {
        // Сигналы на текущем баре h[h.Count-1]: 0-2 штуки (AGAINST, MOMENTUM). Skip ставит только NO_LEVEL / AGAINST_LRA.
        public static List<LraSignal> Evaluate(IList<LraBar> h, double atr, double dayAtr, List<LraRange> ranges, LraSettings s) { throw new NotImplementedException(); }
    }
}
