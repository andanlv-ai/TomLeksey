// TickRecorder.Indicator.MC.CS — пишет каждую сделку с графика «1 Tick» в CSV (ТЗ: docs/LraTrader/tz-ticks.md).
// Путь: C:\ProgramData\TS Support\MultiCharts .NET64 Special Edition\StudyServer\Techniques\CS\TickRecorder.Indicator.CS
//
// Файл: <Dir>\<символ>_<Label>.csv. Только дописывается сделками новее последней строки: CQG хранит тики
// около месяца, и перезапись потеряла бы всё старше. Профиль на графике не нужен.
// Size = UpTicks + DownTicks (контракты; одно из двух = 0 — это направление). Bars.Volume здесь = только UpTicks.
// Source: HIST — из загруженной истории, LIVE — пришло в реальном времени.

using System;
using System.Globalization;
using System.IO;

namespace PowerLanguage.Indicator
{
    [SameAsSymbol(true)]
    public class TickRecorder : IndicatorObject
    {
        [Input] public string Label { get; set; }
        [Input] public string Dir   { get; set; }

        private static readonly CultureInfo CI = CultureInfo.InvariantCulture;
        private const string TimeFmt = "yyyy-MM-dd HH:mm:ss.fff";
        private StreamWriter _w;
        private DateTime _last;   // время последней строки файла
        private int _skipAtLast;  // сколько сделок с этим временем уже в файле (в одну миллисекунду их бывает несколько)

        public TickRecorder(object ctx) : base(ctx)
        {
            Label = "TRADES";
            Dir   = @"D:\TradeLog\Bars";
        }

        protected override void StartCalc()
        {
            string sym = Bars.Info.Name;
            foreach (char c in Path.GetInvalidFileNameChars()) sym = sym.Replace(c, '_');
            Directory.CreateDirectory(Dir);
            string path = Path.Combine(Dir, sym + "_" + Label + ".csv");
            _last = DateTime.MinValue; _skipAtLast = 0;
            if (File.Exists(path))
                foreach (string line in File.ReadLines(path))
                {
                    DateTime t;
                    if (!DateTime.TryParseExact(line.Split(',')[0], TimeFmt, CI, DateTimeStyles.None, out t)) continue;
                    if (t > _last) { _last = t; _skipAtLast = 1; } else if (t == _last) _skipAtLast++;
                }
            bool isNew = !File.Exists(path);
            _w = new StreamWriter(path, true);
            if (isNew) _w.WriteLine("Time,Price,Size,Dir,Source");
        }

        protected override void CalcBar()
        {
            if (Bars.Status != EBarState.Close) return;
            DateTime t = Bars.Time[0];
            if (t < _last) return;
            if (t == _last && _skipAtLast > 0) { _skipAtLast--; return; }
            double up = Bars.UpTicks[0], dn = Bars.DownTicks[0];
            _w.WriteLine(string.Format(CI, "{0},{1},{2},{3},{4}", t.ToString(TimeFmt, CI), Bars.Close[0], up + dn,
                up > 0 ? 1 : dn > 0 ? -1 : 0, Environment.IsRealTimeCalc ? "LIVE" : "HIST"));
            if (Bars.LastBarOnChart) _w.Flush();
        }

        protected override void StopCalc()
        {
            if (_w != null) { _w.Dispose(); _w = null; }
        }
    }
}
