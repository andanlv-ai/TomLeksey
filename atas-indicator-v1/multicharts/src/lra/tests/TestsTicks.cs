using System.Collections.Generic;
using PowerLanguage.Strategy;

public static class TestsTicks
{
    public static void Run()
    {
        // 28.10.2026: вторая сделка вошла на последнем тике ленты, пока первая открыта (TExit первой = последний тик)
        var a = new LraTicks.Ev { Skip = "", Reason = "OPEN", TEntry = 100, TExit = 200 };
        var b = new LraTicks.Ev { Skip = "", Reason = "OPEN", TEntry = 200, TExit = 200 };
        LraTicks.Simulate(new List<LraTicks.Ev> { a, b });
        TestMain.Check(a.Skip == "" && b.Skip == "IN_POSITION", "Simulate: открытая сделка не пускает следующую");

        var c = new LraTicks.Ev { Skip = "", Reason = "TP", TEntry = 100, TExit = 150 };
        var d = new LraTicks.Ev { Skip = "", Reason = "SL", TEntry = 160, TExit = 170 };
        LraTicks.Simulate(new List<LraTicks.Ev> { c, d });
        TestMain.Check(d.Skip == "", "Simulate: после выхода следующая сделка входит");

    }
}
