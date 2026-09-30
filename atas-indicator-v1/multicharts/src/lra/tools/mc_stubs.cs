// Только для проверки сборки вне MultiCharts: функции MC компилируются внутри MC и снаружи недоступны.
namespace PowerLanguage.Function
{
    public static class McStubs
    {
        public static double AverageTrueRange(this object study, int length) { return 0; }
    }
}
