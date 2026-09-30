using System;
using System.Collections.Generic;

namespace PowerLanguage.Strategy
{
    // Диапазоны ЛРА (ТЗ §4).
    public static class LraRanges
    {
        // Вызывать на каждом закрытом баре. Отмечает Done/Expired у прежних диапазонов (кладёт их в changed),
        // находит новый диапазон (добавляет в ranges и в changed, возвращает его) или возвращает null.
        public static LraRange Update(IList<LraBar> h, List<LraRange> ranges, LraSettings s, List<LraRange> changed) { throw new NotImplementedException(); }

        // Активный диапазон с минимальным |price - Poc| или null.
        public static LraRange NearestActive(List<LraRange> ranges, double price) { throw new NotImplementedException(); }
    }
}
