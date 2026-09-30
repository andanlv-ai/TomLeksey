using System;
using System.Collections.Generic;
using PowerLanguage.Strategy;

public static class TestsImbalance
{
    static readonly double[] P3 = { 1.1000, 1.1001, 1.1002 };

    static LraBar Bar(DateTime paris, double up, double down, double[] px, double ask, double bid)
    {
        var b = new LraBar { Paris = paris, Time = paris, UpTicks = up, DownTicks = down };
        b.LvPrice = px;
        b.LvAsk = new double[px.Length];
        b.LvBid = new double[px.Length];
        for (int i = 0; i < px.Length; i++) { b.LvAsk[i] = ask; b.LvBid[i] = bid; b.Ask += ask; b.Bid += bid; }
        b.Volume = b.Ask + b.Bid;
        return b;
    }

    // День: бар 0 — фон (тики 430/430, объём 1000), дальше окно из двух баров. Итого тиков дня 1000.
    static List<LraBar> Day(LraBar w1, LraBar w2)
    {
        var d = new DateTime(2026, 9, 29, 9, 0, 0);
        var bg = new LraBar { Paris = d.AddHours(-1), UpTicks = 430, DownTicks = 430, Volume = 1000, Ask = 500, Bid = 500 };
        w1.Paris = d; w2.Paris = d.AddHours(1);
        return new List<LraBar> { bg, w1, w2 };
    }

    public static void Run()
    {
        var s = new LraSettings(); // MinShare 0.05, LevelImbPct 0.3, MinLevels 3

        // покупки: тики +100 из 1000 = 10%, 3 уровня за покупку, VolDelta +120
        var h = Day(Bar(DateTime.MinValue, 60, 10, P3, 30, 10), Bar(DateTime.MinValue, 60, 10, P3, 30, 10));
        var r = LraImb.Measure(h, 1, 2, s);
        TestMain.Check(r.Dir == 1 && r.Reason == "", "buy: Dir=+1");
        TestMain.Check(TestMain.Near(r.TickShare, 0.1), "buy: TickShare 10%");
        TestMain.Check(TestMain.Near(r.VolDelta, 120) && TestMain.Near(r.TickDelta, 100), "buy: deltas");
        TestMain.Check(r.BuyLevels == 3 && r.SellLevels == 0, "buy: levels");

        // зеркально
        h = Day(Bar(DateTime.MinValue, 10, 60, P3, 10, 30), Bar(DateTime.MinValue, 10, 60, P3, 10, 30));
        r = LraImb.Measure(h, 1, 2, s);
        TestMain.Check(r.Dir == -1 && r.Reason == "", "sell: Dir=-1");
        TestMain.Check(TestMain.Near(r.TickShare, -0.1) && r.SellLevels == 3 && r.BuyLevels == 0, "sell: share, levels");

        // уровней 2
        var p2 = new[] { 1.1000, 1.1001 };
        h = Day(Bar(DateTime.MinValue, 60, 10, p2, 30, 10), Bar(DateTime.MinValue, 60, 10, p2, 30, 10));
        r = LraImb.Measure(h, 1, 2, s);
        TestMain.Check(r.Dir == 0 && r.Reason == "ONE_PRICE", "2 levels: ONE_PRICE");

        // доля мала: тики +10 из 870 < 5%
        h = Day(Bar(DateTime.MinValue, 6, 1, P3, 30, 10), Bar(DateTime.MinValue, 6, 1, P3, 30, 10));
        r = LraImb.Measure(h, 1, 2, s);
        TestMain.Check(r.Dir == 0 && r.Reason == "WEAK", "small share: WEAK");

        // дельты спорят: тики +10%, объём -9.7%
        h = Day(Bar(DateTime.MinValue, 60, 10, P3, 10, 30), Bar(DateTime.MinValue, 60, 10, P3, 10, 30));
        r = LraImb.Measure(h, 1, 2, s);
        TestMain.Check(r.VolShare < -0.05 && r.TickShare > 0.05, "conflict: setup");
        TestMain.Check(r.Dir == 0 && r.Reason == "CONFLICT", "conflict: CONFLICT");

        // NoData в окне
        h = Day(Bar(DateTime.MinValue, 60, 10, P3, 30, 10), Bar(DateTime.MinValue, 60, 10, P3, 30, 10));
        h[2].NoData = true;
        r = LraImb.Measure(h, 1, 2, s);
        TestMain.Check(r.Dir == 0 && r.Reason == "NO_DATA", "NoData: NO_DATA");

        // цены суммируются по окну: +20/-20 на уровне взаимно гасятся
        h = Day(Bar(DateTime.MinValue, 60, 10, P3, 30, 10), Bar(DateTime.MinValue, 60, 10, P3, 10, 30));
        r = LraImb.Measure(h, 1, 2, s);
        TestMain.Check(r.BuyLevels == 0 && r.SellLevels == 0, "levels are summed over window");

        // DayTotals не берёт прошлую дату
        var d1 = new DateTime(2026, 9, 28, 23, 0, 0);
        var d2 = new DateTime(2026, 9, 29, 0, 0, 0);
        var hd = new List<LraBar>
        {
            new LraBar { Paris = d1, Volume = 500, UpTicks = 70, DownTicks = 30 },
            new LraBar { Paris = d2, Volume = 100, UpTicks = 6, DownTicks = 4 },
            new LraBar { Paris = d2.AddHours(1), Volume = 200, UpTicks = 15, DownTicks = 5 },
            new LraBar { Paris = d2.AddHours(2), Volume = 400, UpTicks = 50, DownTicks = 50 },
        };
        double vol, ticks;
        LraImb.DayTotals(hd, 2, out vol, out ticks);
        TestMain.Check(TestMain.Near(vol, 300) && TestMain.Near(ticks, 30), "DayTotals: no previous date");
        LraImb.DayTotals(hd, 0, out vol, out ticks);
        TestMain.Check(TestMain.Near(vol, 500) && TestMain.Near(ticks, 100), "DayTotals: first bar of day");
    }
}
