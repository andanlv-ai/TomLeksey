using System;
using System.IO;
using PowerLanguage.Strategy;

public static class TestsCsv
{
    private const string H = "time,x,sym";

    private static string Line(DateTime t, string sym) { return t.ToString("yyyy-MM-dd HH:mm") + ",v," + sym; }

    public static void Run()
    {
        string dir = Path.Combine(Path.GetTempPath(), "lracsv_" + Guid.NewGuid().ToString("N"));
        try
        {
            var t0 = new DateTime(2026, 1, 5, 10, 0, 0);

            // рестарт: те же 3 строки + 1 новая -> заголовок + 4 строки
            var a = new LraCsv(dir, "EUR");
            for (int i = 0; i < 3; i++) a.Write("a.csv", H, t0.AddHours(i), Line(t0.AddHours(i), "EUR"));
            var b = new LraCsv(dir, "EUR");
            for (int i = 0; i < 4; i++) b.Write("a.csv", H, t0.AddHours(i), Line(t0.AddHours(i), "EUR"));
            string[] lines = File.ReadAllLines(Path.Combine(dir, "a.csv"));
            TestMain.Check(lines.Length == 5, "csv: заголовок + 4 строки, без дублей");
            TestMain.Check(lines[0] == H && lines[4] == Line(t0.AddHours(3), "EUR"), "csv: заголовок первым, новая строка последней");

            // MaxValue пишется всегда
            var c = new LraCsv(dir, "EUR");
            c.Write("runs.csv", H, DateTime.MaxValue, "r1,v,EUR");
            c.Write("runs.csv", H, DateTime.MaxValue, "r1,v,EUR");
            TestMain.Check(File.ReadAllLines(Path.Combine(dir, "runs.csv")).Length == 3, "csv: key=MaxValue пишется всегда");

            // строки другого символа метку не двигают
            var d = new LraCsv(dir, "EUR");
            d.Write("m.csv", H, t0.AddHours(5), Line(t0.AddHours(5), "GBP"));
            var e = new LraCsv(dir, "EUR");
            e.Write("m.csv", H, t0.AddHours(1), Line(t0.AddHours(1), "EUR"));
            TestMain.Check(File.ReadAllLines(Path.Combine(dir, "m.csv")).Length == 3, "csv: чужой символ не влияет на метку");
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
