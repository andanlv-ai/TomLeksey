using System;
using System.Collections.Generic;
using PowerLanguage.Strategy;

public static class TestsPositions
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

    // Бары 0..2: фон и окно из двух баров, где толпа продаёт (Dir=-1); бар 2 — бар входа. Бар 3 — проверяемый.
    static List<LraBar> History(LraBar check)
    {
        var d = new DateTime(2026, 9, 29, 9, 0, 0);
        var bg = new LraBar { Paris = d.AddHours(-1), UpTicks = 430, DownTicks = 430, Volume = 1000, Ask = 500, Bid = 500 };
        check.Paris = d.AddHours(2);
        return new List<LraBar> { bg, Bar(d, 10, 60, 10, 30, 1.1005, 1.0995, 1.1000), Bar(d.AddHours(1), 10, 60, 10, 30, 1.1005, 1.0995, 1.1000), check };
    }

    static LraPosition Long(string book, List<LraBar> h)
    {
        var sig = new LraSignal { Book = book, Dir = 1, Entry = 1.1000, SL = 1.0970, TP = 1.1010, TP2 = 1.1020, WinFrom = 1 };
        return LraPos.Open(sig, h.GetRange(0, 3), "S1");
    }

    // бар 3: толпа всё ещё продаёт (Dir=-1) или NoData (Dir=0)
    static LraBar Check(double hi, double lo, double close, bool noData)
    {
        var b = Bar(DateTime.MinValue, 10, 60, 10, 30, hi, lo, close);
        b.NoData = noData;
        return b;
    }

    public static void Run()
    {
        var s = new LraSettings(); // StallAtr 0.5
        double atr = 0.0020;       // порог C = 0.0010

        // Open
        var h = History(Check(1.1005, 1.0995, 1.1000, false));
        var p = Long("MOMENTUM", h);
        TestMain.Check(p.Units.Count == 2 && p.EntryIdx == 2 && p.Units[0].N == 1 && p.Units[1].N == 2, "open: две части, EntryIdx");
        TestMain.Check(TestMain.Near(p.Units[0].TP, 1.1010) && TestMain.Near(p.Units[1].TP, 1.1020), "open: TP частей");
        var sig1 = new LraSignal { Book = "AGAINST", Dir = 1, Entry = 1.1, SL = 1.097, TP = 1.101 };
        TestMain.Check(LraPos.Open(sig1, h.GetRange(0, 3), "S2").Units.Count == 1, "open: TP2=NaN - одна часть");

        // бар входа: ничего не делаем, даже если задет SL
        var he = History(Check(1.1005, 1.0995, 1.1000, false)).GetRange(0, 3);
        he[2].Low = 1.0900;
        TestMain.Check(LraPos.OnBar(he, p, atr, s).Count == 0 && p.Units.Count == 2, "бар входа: без выходов");

        // TP части 1: выход по TP, часть 2 осталась
        h = History(Check(1.1012, 1.0990, 1.1008, false)); p = Long("MOMENTUM", h);
        var ex = LraPos.OnBar(h, p, atr, s);
        TestMain.Check(ex.Count == 1 && ex[0].Reason == "TP" && ex[0].Unit.N == 1 && TestMain.Near(ex[0].Price, 1.1010) && !ex[0].Ambiguous, "TP части 1");
        TestMain.Check(p.Units.Count == 1 && p.Units[0].N == 2, "TP части 1: часть 2 осталась");

        // SL: обе части по SL
        h = History(Check(1.1005, 1.0965, 1.0990, false)); p = Long("MOMENTUM", h);
        ex = LraPos.OnBar(h, p, atr, s);
        TestMain.Check(ex.Count == 2 && ex.TrueForAll(e => e.Reason == "SL" && TestMain.Near(e.Price, 1.0970) && !e.Ambiguous), "SL: обе части");
        TestMain.Check(p.Units.Count == 0, "SL: Units пуст");

        // SL и TP на одном баре: SL, Ambiguous
        h = History(Check(1.1012, 1.0965, 1.0990, false)); p = Long("MOMENTUM", h);
        ex = LraPos.OnBar(h, p, atr, s);
        TestMain.Check(ex.Count == 2 && ex.TrueForAll(e => e.Reason == "SL"), "SL+TP: обе части по SL");
        TestMain.Check(ex[0].Unit.N == 1 && ex[0].Ambiguous && ex[1].Unit.N == 2 && !ex[1].Ambiguous, "SL+TP1: Ambiguous только у части 1");

        // шорт: зеркально, TP по Low
        var sigS = new LraSignal { Book = "MOMENTUM", Dir = -1, Entry = 1.1000, SL = 1.1030, TP = 1.0990, WinFrom = 1 };
        h = History(Check(1.1005, 1.0988, 1.0995, false));
        var ps = LraPos.Open(sigS, h.GetRange(0, 3), "S3");
        ex = LraPos.OnBar(h, ps, atr, s);
        TestMain.Check(ex.Count == 1 && ex[0].Reason == "TP" && TestMain.Near(ex[0].Price, 1.0990), "шорт: TP по Low");

        // C: AGAINST, толпа исчезла, цена рядом со входом -> DISSOLVED по Close
        h = History(Check(1.1005, 1.0995, 1.1002, true)); p = Long("AGAINST", h);
        ex = LraPos.OnBar(h, p, atr, s);
        TestMain.Check(ex.Count == 2 && ex.TrueForAll(e => e.Reason == "DISSOLVED" && TestMain.Near(e.Price, 1.1002)), "C: обе части DISSOLVED по Close");
        TestMain.Check(p.Units.Count == 0, "C: Units пуст");

        // MOMENTUM в той же ситуации не закрывается
        h = History(Check(1.1005, 1.0995, 1.1002, true)); p = Long("MOMENTUM", h);
        TestMain.Check(LraPos.OnBar(h, p, atr, s).Count == 0 && p.Units.Count == 2, "C: MOMENTUM не закрывается");

        // AGAINST, толпа осталась (Dir=-1) -> не закрываем
        h = History(Check(1.1005, 1.0995, 1.1002, false)); p = Long("AGAINST", h);
        TestMain.Check(LraPos.OnBar(h, p, atr, s).Count == 0, "C: толпа осталась - не закрываем");

        // AGAINST, толпа перешла на нашу сторону (Dir=+1 = p.Dir) -> закрываем
        var flip = Bar(DateTime.MinValue, 900, 10, 100, 0, 1.1005, 1.0995, 1.1002);
        h = History(flip); p = Long("AGAINST", h);
        ex = LraPos.OnBar(h, p, atr, s);
        TestMain.Check(ex.Count == 2 && ex[0].Reason == "DISSOLVED", "C: толпа перешла на нашу сторону");

        // AGAINST, толпа исчезла, но цена далеко (|Close-Entry| = 0.0015 >= 0.0010) -> не закрываем
        h = History(Check(1.1005, 1.0985, 1.0985, true)); p = Long("AGAINST", h);
        TestMain.Check(LraPos.OnBar(h, p, atr, s).Count == 0, "C: цена далеко - не закрываем");
    }
}
