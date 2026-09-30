using System;
using System.Collections.Generic;

namespace PowerLanguage.Strategy
{
    // Виртуальные позиции: открытие, выходы, C, B (ТЗ §7).
    public static class LraPos
    {
        // Позиция из сигнала: часть 1 с TP и часть 2 с TP2, если TP2 не NaN. Вход по sig.Entry на баре h[h.Count-1].
        public static LraPosition Open(LraSignal sig, IList<LraBar> h, string signalId)
        {
            int idx = h.Count - 1;
            var p = new LraPosition
            {
                Book = sig.Book, SignalId = signalId, Dir = sig.Dir, WinFrom = sig.WinFrom, EntryIdx = idx, SL = sig.SL
            };
            p.Units.Add(NewUnit(1, h, sig.Entry, sig.TP));
            if (!double.IsNaN(sig.TP2)) p.Units.Add(NewUnit(2, h, sig.Entry, sig.TP2));
            return p;
        }

        static LraUnit NewUnit(int n, IList<LraBar> h, double entry, double tp)
        {
            int idx = h.Count - 1;
            return new LraUnit { N = n, OpenIdx = idx, OpenTime = h[idx].Time, Entry = entry, TP = tp };
        }

        // Сопровождение на текущем баре (ТЗ §7). Закрытые части убирает из p.Units и возвращает; может добавить часть (B).
        public static List<LraExit> OnBar(IList<LraBar> h, LraPosition p, double atr, LraSettings s)
        {
            var exits = new List<LraExit>();
            int last = h.Count - 1;
            if (last <= p.EntryIdx || p.Units.Count == 0) return exits;
            LraBar b = h[last];
            bool up = p.Dir > 0;

            // 1. Выход по уровню
            bool slHit = up ? b.Low <= p.SL : b.High >= p.SL;
            if (slHit)
            {
                foreach (LraUnit u in p.Units)
                    exits.Add(new LraExit { Unit = u, Price = p.SL, Reason = "SL", Ambiguous = up ? b.High >= u.TP : b.Low <= u.TP });
                p.Units.Clear();
                return exits;
            }
            foreach (LraUnit u in p.Units.ToArray())
                if (up ? b.High >= u.TP : b.Low <= u.TP)
                {
                    exits.Add(new LraExit { Unit = u, Price = u.TP, Reason = "TP" });
                    p.Units.Remove(u);
                }
            if (p.Units.Count == 0) return exits;

            // 2. C — дисбаланс исчез (только AGAINST)
            if (p.Book == "AGAINST")
            {
                LraImbalance imb = LraImb.Measure(h, p.WinFrom, last, s);
                LraUnit first = p.Units.Find(u => u.N == 1) ?? p.Units[0];
                if ((imb.Dir == 0 || imb.Dir == p.Dir) && Math.Abs(b.Close - first.Entry) < s.StallAtr * atr)
                {
                    foreach (LraUnit u in p.Units) exits.Add(new LraExit { Unit = u, Price = b.Close, Reason = "DISSOLVED" });
                    p.Units.Clear();
                    return exits;
                }
            }

            // B — тикет 07
            return exits;
        }
    }
}
