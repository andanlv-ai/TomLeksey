using System;
using System.Collections.Generic;
using PowerLanguage.Strategy;

// ТЗ §7 п. 3: добор (B) и ADVERSE_FLOW. Шорт AGAINST: вход 1.1000 на баре 2, толпа перед входом покупает.
public static class TestsPositionsAdd
{
    static readonly double[] P3 = { 1.1000, 1.1001, 1.1002 };

    static LraBar Bar(DateTime paris, double up, double down, double ask, double bid, double hi, double lo, double close)
    {
        var b = new LraBar { Paris = paris, Time = paris, UpTicks = up, DownTicks = down, High = hi, Low = lo, Close = close, Open = close };
        b.LvPrice = P3;
        b.LvAsk = new double[3];
        b.LvBid = new double[3];
        for (int i = 0; i < 3; i++) { b.LvAsk[i] = ask; b.LvBid[i] = bid; b.Ask += ask; b.Bid += bid; }
        b.Volume = b.Ask + b.Bid;
        return b;
    }

    // crowdBuys: на проверяемом баре 3 толпа покупает (Dir=+1) или продаёт (Dir=-1 — на нашей стороне)
    static List<LraBar> History(bool crowdBuys, double hi, double lo, double close)
    {
        var d = new DateTime(2026, 9, 29, 9, 0, 0);
        var bg = new LraBar { Paris = d.AddHours(-1), UpTicks = 430, DownTicks = 430, Volume = 1000, Ask = 500, Bid = 500 };
        var chk = crowdBuys ? Bar(d.AddHours(2), 160, 10, 30, 10, hi, lo, close) : Bar(d.AddHours(2), 10, 160, 10, 30, hi, lo, close);
        return new List<LraBar> { bg, Bar(d, 160, 10, 30, 10, 1.1005, 1.0995, 1.1000), Bar(d.AddHours(1), 160, 10, 30, 10, 1.1005, 1.0995, 1.1000), chk };
    }

    static LraPosition Short(List<LraBar> h)
    {
        var sig = new LraSignal { Book = "AGAINST", Dir = -1, Entry = 1.1000, SL = 1.1030, TP = 1.0990, WinFrom = 1 };
        return LraPos.Open(sig, h.GetRange(0, 3), "S1");
    }

    public static void Run()
    {
        var s = new LraSettings();
        double atr = 0.0010; // порог C = 0.0005, цена ушла на 0.0010 — C не срабатывает

        // цена +10 пипсов, толпа покупает -> часть 2, SAME_TP
        var h = History(true, 1.1012, 1.1000, 1.1010);
        var p = Short(h);
        var ex = LraPos.OnBar(h, p, atr, s);
        TestMain.Check(ex.Count == 0 && p.Units.Count == 2 && p.Units[1].N == 2, "B: добавлена часть 2");
        TestMain.Check(TestMain.Near(p.Units[1].Entry, 1.1010) && p.Units[1].OpenIdx == 3, "B: вход части 2 по Close, OpenIdx");
        TestMain.Check(TestMain.Near(p.Units[0].TP, 1.0990) && TestMain.Near(p.Units[1].TP, 1.0990), "B SAME_TP: TP одинаковые");

        // третьей части не бывает
        h.Add(Bar(new DateTime(2026, 9, 29, 12, 0, 0), 160, 10, 30, 10, 1.1025, 1.1010, 1.1020));
        ex = LraPos.OnBar(h, p, atr, s);
        TestMain.Check(ex.Count == 0 && p.Units.Count == 2, "B: третьей части нет");

        // после добора (2 части) толпа перешла на нашу сторону -> обе части ADVERSE_FLOW
        h = History(true, 1.1012, 1.1000, 1.1010);
        p = Short(h);
        LraPos.OnBar(h, p, atr, s);
        h.Add(Bar(new DateTime(2026, 9, 29, 12, 0, 0), 10, 400, 10, 100, 1.1015, 1.1005, 1.1012));
        ex = LraPos.OnBar(h, p, atr, s);
        TestMain.Check(ex.Count == 2 && ex.TrueForAll(e => e.Reason == "ADVERSE_FLOW" && TestMain.Near(e.Price, 1.1012)) && p.Units.Count == 0, "ADVERSE_FLOW: обе части после добора");

        // ENTRY1: TP обеих = вход части 1
        h = History(true, 1.1012, 1.1000, 1.1010);
        p = Short(h);
        var s2 = new LraSettings { AddTargetMode = "ENTRY1" };
        LraPos.OnBar(h, p, atr, s2);
        TestMain.Check(p.Units.Count == 2 && TestMain.Near(p.Units[0].TP, 1.1000) && TestMain.Near(p.Units[1].TP, 1.1000), "B ENTRY1: TP обеих = вход 1");

        // цена ушла меньше AddStepPips -> не добираем
        h = History(true, 1.1009, 1.1000, 1.1005);
        p = Short(h);
        ex = LraPos.OnBar(h, p, atr, s);
        TestMain.Check(ex.Count == 0 && p.Units.Count == 1, "B: +5 пипсов - без добора");

        // толпа на нашей стороне после входа -> ADVERSE_FLOW по Close
        h = History(false, 1.1012, 1.1000, 1.1010);
        p = Short(h);
        ex = LraPos.OnBar(h, p, atr, s);
        TestMain.Check(ex.Count == 1 && ex[0].Reason == "ADVERSE_FLOW" && TestMain.Near(ex[0].Price, 1.1010) && p.Units.Count == 0, "ADVERSE_FLOW: закрыто по Close");

        // MOMENTUM: B не работает
        h = History(true, 1.1012, 1.1000, 1.1010);
        var sigM = new LraSignal { Book = "MOMENTUM", Dir = -1, Entry = 1.1000, SL = 1.1030, TP = 1.0990, WinFrom = 1 };
        p = LraPos.Open(sigM, h.GetRange(0, 3), "S4");
        LraPos.OnBar(h, p, atr, s);
        TestMain.Check(p.Units.Count == 1, "B: MOMENTUM - без добора");
    }
}
