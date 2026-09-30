using System;
using System.Collections.Generic;

namespace PowerLanguage.Strategy
{
    // Сценарии входа A и D с фильтром ЛРА (ТЗ §6).
    public static class LraScen
    {
        // Сигналы на текущем баре h[h.Count-1]: 0-2 штуки (AGAINST, MOMENTUM). Skip ставит только NO_LEVEL / AGAINST_LRA.
        public static List<LraSignal> Evaluate(IList<LraBar> h, double atr, double dayAtr, List<LraRange> ranges, LraSettings s)
        {
            var res = new List<LraSignal>();
            int last = h.Count - 1;
            if (last - s.ImbBars < 0) return res;

            int winFrom = h.Count - s.ImbBars;
            LraImbalance crowd = LraImb.Measure(h, winFrom, last, s);
            if (crowd.Dir == 0) return res;

            double entry = h[last].Close;
            double move = entry - h[last - s.ImbBars].Close;
            bool strong = Math.Abs(move) >= s.StrongAtr * atr;
            bool a = move * crowd.Dir <= s.StallAtr * atr && !strong; // сильное движение — только D
            bool d = strong && Math.Sign(move) == -crowd.Dir;
            if (!a && !d) return res;

            // Направление, SL, TP, фильтр ЛРА — общие для A и D.
            int dir = -crowd.Dir;
            double pip = s.PipSize;
            double sl = LraLevels.Nearest(h, -dir, entry, s);
            double tp = LraLevels.Nearest(h, dir, entry, s);
            string skip = "";
            if (double.IsNaN(sl) || double.IsNaN(tp) || Math.Abs(sl - entry) / pip > s.MaxStopPips) skip = "NO_LEVEL";
            else if (Math.Abs(tp - entry) / pip > s.MaxTakePips) tp = entry + dir * s.MaxTakePips * pip;

            LraRange range = LraRanges.NearestActive(ranges, entry);
            double dist = double.NaN, tp2 = double.NaN;
            if (range != null)
            {
                dist = Math.Abs(entry - range.Poc) / pip;
                double toPoc = dir * (range.Poc - entry); // > 0: сделка идёт к Poc
                if (skip == "" && toPoc < 0) skip = "AGAINST_LRA";
                else if (skip == "" && toPoc > 0 && dist >= s.LraMinDistPips
                         && !double.IsNaN(dayAtr) && dist <= s.DayTargetShare * dayAtr / pip) tp2 = range.Poc;
            }

            foreach (string book in new[] { "AGAINST", "MOMENTUM" })
            {
                if (book == "AGAINST" ? !a : !d) continue;
                res.Add(new LraSignal
                {
                    Book = book, Dir = dir, Entry = entry, SL = sl, TP = tp, TP2 = tp2,
                    WinFrom = winFrom, Crowd = crowd, Range = range, RangeDistPips = dist, Skip = skip
                });
            }
            return res;
        }
    }
}
