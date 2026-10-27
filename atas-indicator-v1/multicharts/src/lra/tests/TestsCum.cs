using System;
using System.Collections.Generic;
using PowerLanguage.Strategy;

public static class TestsCum
{
    static readonly double[] P3 = { 1.1000, 1.1001, 1.1002 };

    // Бар на ценах P3: на каждой цене ask/bid, сделки up/down, хай 1.1002 (или hi), лоу 1.1000.
    static LraBar Bar(double up, double down, double ask, double bid, double hi = 1.1002)
    {
        var b = new LraBar { UpTicks = up, DownTicks = down, High = hi, Low = 1.1000, Close = 1.1001, LvPrice = P3 };
        b.LvAsk = new[] { ask, ask, ask }; b.LvBid = new[] { bid, bid, bid };
        b.Ask = 3 * ask; b.Bid = 3 * bid; b.Volume = b.Ask + b.Bid;
        return b;
    }

    // 3 бара без перекоса, затем 3 бара роста покупок по сделкам (+60 на бар).
    static List<LraBar> Rise(double up = 80, double down = 20, double breakHi = 1.1002)
    {
        return new List<LraBar> {
            Bar(50, 50, 30, 30), Bar(50, 50, 30, 30), Bar(50, 50, 30, 30),
            Bar(up, down, 60, 20), Bar(up, down, 60, 20, breakHi), Bar(up, down, 60, 20) };
    }

    public static void Run()
    {
        var s = new LraSettings { Tick = 0.0001, PipSize = 0.0001 };
        int from;

        LraImbalance r = LraImb.Cum(Rise(), s, out from);
        TestMain.Check(r != null && r.Dir == 1 && from == 1, "Cum: рост покупок при стоящей цене -> Dir +1, окно от начала роста");

        TestMain.Check(LraImb.Cum(Rise(breakHi: 1.1005), s, out from) == null, "Cum: цена обновила хай начала роста -> нет толпы");
        TestMain.Check(LraImb.Cum(Rise(51, 49), s, out from) == null, "Cum: вес роста меньше CumWeight -> нет толпы");

        var noData = Rise(); noData[4].NoData = true;
        TestMain.Check(LraImb.Cum(noData, s, out from) == null, "Cum: бар без данных -> нет толпы");

        s.MinLevels = 4;   // перекошены только 3 цены
        TestMain.Check(LraImb.Cum(Rise(), s, out from) == null, "Cum: перекос меньше чем на MinLevels ценах -> нет толпы");
    }
}
