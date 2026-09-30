using System;
using System.Collections.Generic;
using PowerLanguage.Strategy;

public static class TestsRanges
{
    static readonly DateTime T0 = new DateTime(2026, 9, 1, 8, 0, 0);

    // Бар в полосе 1.1000..1.1020; уровни: 1.1005 (один бар даст максимум 200), 1.1010 (60 на бар, в сумме больше)
    static LraBar InBand(int i, string session, double pocLevelVol)
    {
        return new LraBar
        {
            Time = T0.AddHours(i), Paris = T0.AddHours(i), Session = session,
            Open = 1.1005, High = 1.1020, Low = 1.1000, Close = 1.1010, Volume = 300,
            LvPrice = new[] { 1.1000, 1.1005, 1.1010 },
            LvAsk = new[] { 10.0, i == 0 ? 100.0 : 5.0, pocLevelVol / 2 },
            LvBid = new[] { 10.0, i == 0 ? 100.0 : 5.0, pocLevelVol / 2 }
        };
    }

    static LraBar Exit(int i, double close)
    {
        return new LraBar { Time = T0.AddHours(i), Paris = T0.AddHours(i), Session = "EUROPE",
            Open = 1.1020, High = close + 0.0005, Low = 1.1015, Close = close };
    }

    static List<LraBar> Bars(int n, string session)
    {
        var h = new List<LraBar>();
        for (int i = 0; i < n; i++) h.Add(InBand(i, session, 60));
        return h;
    }

    public static void Run()
    {
        var s = new LraSettings();

        // 5 баров EUROPE, 6-й закрылся выше -> рождён
        var h = Bars(5, "EUROPE");
        h.Add(Exit(5, 1.1030));
        var ranges = new List<LraRange>(); var changed = new List<LraRange>();
        LraRange r = LraRanges.Update(h, ranges, s, changed);
        TestMain.Check(r != null && ranges.Count == 1 && changed.Count == 1, "диапазон рождён");
        if (r != null)
        {
            TestMain.Check(r.ExitDir == 1, "ExitDir=+1");
            TestMain.Check(TestMain.Near(r.Poc, 1.1010), "Poc = цена с наибольшей суммой объёма");
            TestMain.Check(TestMain.Near(r.High, 1.1020) && TestMain.Near(r.Low, 1.1000), "High/Low");
            TestMain.Check(r.Bars == 5 && r.StartIdx == 0 && r.EndIdx == 4 && r.BornIdx == 5, "индексы и Bars");
            TestMain.Check(TestMain.Near(r.Volume, 1500), "Volume");
            TestMain.Check(r.Id == "202609010800" && r.Active, "Id и активен");
        }

        // выход вниз
        h = Bars(5, "EUROPE"); h.Add(Exit(5, 1.0990));
        r = LraRanges.Update(h, new List<LraRange>(), s, new List<LraRange>());
        TestMain.Check(r != null && r.ExitDir == -1, "ExitDir=-1");

        // те же бары в ASIA -> не рождён
        h = Bars(5, "ASIA"); h.Add(Exit(5, 1.1030));
        TestMain.Check(LraRanges.Update(h, new List<LraRange>(), s, new List<LraRange>()) == null, "ASIA: не рождён");

        // 3 бара -> не рождён
        h = Bars(3, "EUROPE"); h.Add(Exit(3, 1.1030));
        TestMain.Check(LraRanges.Update(h, new List<LraRange>(), s, new List<LraRange>()) == null, "3 бара: не рождён");

        // закрытие внутри границ -> не рождён
        h = Bars(5, "EUROPE"); h.Add(Exit(5, 1.1015));
        TestMain.Check(LraRanges.Update(h, new List<LraRange>(), s, new List<LraRange>()) == null, "закрытие внутри: не рождён");

        // активный диапазон пересекается -> второй не создаётся
        h = Bars(5, "EUROPE"); h.Add(Exit(5, 1.1030));
        ranges = new List<LraRange>();
        LraRanges.Update(h, ranges, s, new List<LraRange>());
        h.Add(Exit(6, 1.1040));
        LraRanges.Update(h, ranges, s, new List<LraRange>());
        TestMain.Check(ranges.Count == 1, "пересечение с активным: нового нет");

        // касание Poc -> Done, попал в changed
        h = Bars(5, "EUROPE"); h.Add(Exit(5, 1.1030));
        ranges = new List<LraRange>(); changed = new List<LraRange>();
        r = LraRanges.Update(h, ranges, s, changed);
        changed.Clear();
        h.Add(new LraBar { Time = T0.AddHours(6), Session = "EUROPE", High = 1.1025, Low = 1.1008, Close = 1.1020 });
        LraRanges.Update(h, ranges, s, changed);
        TestMain.Check(r.Done && !r.Active && changed.Contains(r), "касание Poc: Done и в changed");
        TestMain.Check(LraRanges.NearestActive(ranges, 1.1010) == null, "Done не активен для NearestActive");

        // бар выхода сам Poc не отрабатывает: Exit(5) с Low=1.1015 не касается, но проверим Low<=Poc на баре рождения
        h = Bars(5, "EUROPE"); h.Add(new LraBar { Time = T0.AddHours(5), Session = "EUROPE", High = 1.1035, Low = 1.1005, Close = 1.1030 });
        ranges = new List<LraRange>();
        r = LraRanges.Update(h, ranges, s, new List<LraRange>());
        TestMain.Check(r != null && r.Active, "бар выхода не закрывает свой диапазон");

        // возраст: MaxRangeAgeBars+1 баров -> Expired
        s = new LraSettings { MaxRangeAgeBars = 5 };
        h = Bars(5, "EUROPE"); h.Add(Exit(5, 1.1030));
        ranges = new List<LraRange>(); changed = new List<LraRange>();
        r = LraRanges.Update(h, ranges, s, changed);
        for (int k = 1; k <= 6; k++)
        {
            changed.Clear();
            h.Add(new LraBar { Time = T0.AddHours(5 + k), Session = "ASIA", High = 1.1050, Low = 1.1040, Close = 1.1045 });
            LraRanges.Update(h, ranges, s, changed);
            if (k == 5) TestMain.Check(r.Active && changed.Count == 0, "возраст = MaxRangeAgeBars: ещё активен");
        }
        TestMain.Check(r.Expired && !r.Done && !r.Active && changed.Contains(r), "возраст MaxRangeAgeBars+1: Expired");

        // NearestActive
        var a = new LraRange { Poc = 1.1000 }; var b = new LraRange { Poc = 1.1100 };
        var c = new LraRange { Poc = 1.1052, Done = true };
        var list = new List<LraRange> { a, b, c };
        TestMain.Check(LraRanges.NearestActive(list, 1.1060) == b, "NearestActive: ближайший активный");
        TestMain.Check(LraRanges.NearestActive(new List<LraRange>(), 1.1) == null, "NearestActive: пусто");
    }
}
