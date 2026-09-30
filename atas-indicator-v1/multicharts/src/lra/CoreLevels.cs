using System;
using System.Collections.Generic;

namespace PowerLanguage.Strategy
{
    // Уровни стопов и ATR дня (ТЗ §5).
    public static class LraLevels
    {
        // Ближайший по времени свинг за ценой: side +1 — верхний свинг выше price (+ отступ), -1 — нижний ниже (- отступ). Нет — NaN.
        public static double Nearest(IList<LraBar> h, int side, double price, LraSettings s) { throw new NotImplementedException(); }

        // Средний размах торговых дней (дата Paris) за AtrPeriod последних полных дней; текущий день не считается. Меньше 3 дней — NaN.
        public static double DayAtr(IList<LraBar> h, LraSettings s) { throw new NotImplementedException(); }
    }
}
