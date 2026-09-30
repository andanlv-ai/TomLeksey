using System;
using System.Linq;

// Точка входа тестов. Сама находит все статические классы с именем Tests* и вызывает их Run().
// Новый файл тестов: tests/TestsИмя.cs с public static class TestsИмя { public static void Run() {...} }.
public static class TestMain
{
    public static int Failed;

    public static void Check(bool ok, string name)
    {
        if (ok) Console.WriteLine("ok   " + name);
        else { Console.WriteLine("FAIL " + name); Failed++; }
    }

    public static bool Near(double a, double b) { return Math.Abs(a - b) < 1e-9; }

    public static int Main()
    {
        foreach (var t in typeof(TestMain).Assembly.GetTypes()
                     .Where(t => t.Name.StartsWith("Tests") && t.GetMethod("Run") != null).OrderBy(t => t.Name))
        {
            try { t.GetMethod("Run").Invoke(null, null); }
            catch (Exception e) { Console.WriteLine("FAIL " + t.Name + ": " + (e.InnerException ?? e).Message); Failed++; }
        }
        Console.WriteLine(Failed == 0 ? "ALL PASSED" : Failed + " FAILED");
        return Failed == 0 ? 0 : 1;
    }
}
