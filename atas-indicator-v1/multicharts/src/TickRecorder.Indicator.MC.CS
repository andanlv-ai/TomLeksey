// TickRecorder.Indicator.MC.CS — пишет каждый бар графика «1 тик» в CSV: проверка, отдаёт ли CQG каждую сделку отдельно.
// Путь: C:\ProgramData\TS Support\MultiCharts .NET64 Special Edition\StudyServer\Techniques\CS\TickRecorder.Indicator.CS
//
// Файл: <Dir>\<символ>_TICK.csv, перезаписывается при каждом пересчёте.
// Source: HIST — из загруженной истории, LIVE — пришло в реальном времени (сравнить с историей после перезагрузки).

using System;
using System.Globalization;
using System.IO;

namespace PowerLanguage.Indicator
{
    [SameAsSymbol(true)]
    public class TickRecorder : IndicatorObject
    {
        [Input] public string Dir { get; set; }

        private static readonly CultureInfo CI = CultureInfo.InvariantCulture;
        private StreamWriter _w;

        public TickRecorder(object ctx) : base(ctx) { Dir = @"D:\TradeLog\Bars"; }

        protected override void StartCalc()
        {
            string sym = Bars.Info.Name;
            foreach (char c in Path.GetInvalidFileNameChars()) sym = sym.Replace(c, '_');
            Directory.CreateDirectory(Dir);
            _w = new StreamWriter(Path.Combine(Dir, sym + "_TICK.csv"), false);
            _w.WriteLine("Time,Price,Volume,UpTicks,DownTicks,Source");
        }

        protected override void CalcBar()
        {
            if (Bars.Status != EBarState.Close) return;
            _w.WriteLine(string.Format(CI, "{0:yyyy-MM-dd HH:mm:ss.fff},{1},{2},{3},{4},{5}",
                Bars.Time[0], Bars.Close[0], Bars.Volume[0], Bars.UpTicks[0], Bars.DownTicks[0],
                Environment.IsRealTimeCalc ? "LIVE" : "HIST"));
            if (Bars.LastBarOnChart) _w.Flush();
        }

        protected override void StopCalc()
        {
            if (_w != null) { _w.Dispose(); _w = null; }
        }
    }
}
