using System;
using System.Collections.Generic;

namespace PowerLanguage.Strategy
{
    // Виртуальные позиции: открытие, выходы, C, B (ТЗ §7).
    public static class LraPos
    {
        // Позиция из сигнала: часть 1 с TP и часть 2 с TP2, если TP2 не NaN. Вход по sig.Entry на баре h[h.Count-1].
        public static LraPosition Open(LraSignal sig, IList<LraBar> h, string signalId) { throw new NotImplementedException(); }

        // Сопровождение на текущем баре (ТЗ §7). Закрытые части убирает из p.Units и возвращает; может добавить часть (B).
        public static List<LraExit> OnBar(IList<LraBar> h, LraPosition p, double atr, LraSettings s) { throw new NotImplementedException(); }
    }
}
