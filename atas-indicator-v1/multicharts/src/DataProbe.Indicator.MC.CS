// DataProbe.Indicator.MC.CS — диагностика простых свойств Bars для DivergenceDetector v2
// Шаг 0 по ТЗ: проверяем доступность UpTicks, DownTicks, Volume и направление бара.
// Вывод только в Output (первые 10 баров + каждый 100-й).
// Путь для копирования: C:\ProgramData\TS Support\MultiCharts .NET64 Special Edition\StudyServer\Techniques\CS\DataProbe.Indicator.CS

using System;

namespace PowerLanguage.Indicator
{
    [UpdateOnEveryTick(false)]
    public class DataProbe : IndicatorObject
    {
        public DataProbe(object ctx) : base(ctx) { }

        protected override void StartCalc()
        {
            Output.WriteLine("=== DataProbe START [{0}] {1} ===",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Bars.Info.Name);
            Output.WriteLine("    Header: bar# | time | C-O | UpT[0] DnT[0] | UpT[1] DnT[1] | Vol[0]");
        }

        protected override void CalcBar()
        {
            int n = Bars.CurrentBar;
            bool log = n <= 10 || n % 100 == 0 || Bars.LastBarOnChart;
            if (!log) return;

            double c = Bars.Close[0];
            double o = Bars.Open[0];
            double co = c - o;

            double ut0 = Safe("UpTicks[0]", () => Bars.UpTicks[0]);
            double dt0 = Safe("DownTicks[0]", () => Bars.DownTicks[0]);
            double ut1 = Safe("UpTicks[1]", () => Bars.UpTicks[1]);
            double dt1 = Safe("DownTicks[1]", () => Bars.DownTicks[1]);
            double vol = Safe("Volume[0]", () => Bars.Volume[0]);

            Output.WriteLine("{0,5} | {1:HH:mm:ss} | {2,8:F5} | {3,5:F0} {4,5:F0} | {5,5:F0} {6,5:F0} | {7,7:F0}",
                n, Bars.Time[0], co, ut0, dt0, ut1, dt1, vol);
        }

        private double Safe(string label, Func<double> f)
        {
            try { return f(); }
            catch (Exception ex)
            {
                Output.WriteLine("  !! {0} FAIL: {1}", label, ex.Message);
                return double.NaN;
            }
        }
    }
}
