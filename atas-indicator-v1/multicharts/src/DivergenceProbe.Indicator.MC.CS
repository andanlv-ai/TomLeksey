// DivergenceProbe.Indicator.MC.CS — диагностический пробник для DivergenceDetector
// Фаза 0 плана (см. ../docs/DivergenceDetector/DivergenceDetector.PLAN.md):
// проверяем доступность Data1/Data2/Data3, читаем OHLC и ADX.
// БЕЗ логики сигналов — только лог в Output и в файл.
// Путь для копирования: C:\ProgramData\TS Support\MultiCharts .NET64 Special Edition\StudyServer\Techniques\CS\DivergenceProbe.Indicator.CS

using System;
using System.IO;
using PowerLanguage.Function;

namespace PowerLanguage.Indicator
{
    [UpdateOnEveryTick(false)]
    [SameAsSymbol(false)]
    public class DivergenceProbe : IndicatorObject
    {
        // === VERSION: 2026-05-13-v0.1 ===
        [Input] public int  LogEveryNBars { get; set; }
        [Input] public bool LogFirstBars  { get; set; }
        [Input] public bool DumpLastBar   { get; set; }

        private ADX          m_adx;
        private StreamWriter m_file;
        private string       m_filePath;

        public DivergenceProbe(object ctx) : base(ctx)
        {
            LogEveryNBars = 20;
            LogFirstBars  = true;
            DumpLastBar   = true;
        }

        protected override void Create()
        {
            m_adx = new ADX(this);
            m_adx.Length = 4;
        }

        protected override void StartCalc()
        {
            OpenLogFile();
            Output.Clear();
            Log("=== DivergenceProbe v0.1 START [{0}] {1} ===",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Bars.Info.Name);
            Log("    Params: LogEveryNBars={0} LogFirstBars={1} DumpLastBar={2}",
                LogEveryNBars, LogFirstBars, DumpLastBar);
            Log("    LogFile: {0}", m_filePath);
            TryLogDataSeriesInfo();
        }

        protected override void StopCalc()
        {
            if (m_file != null)
            {
                try { m_file.Flush(); m_file.Close(); } catch { }
                m_file = null;
            }
        }

        protected override void CalcBar()
        {
            bool firstFew = LogFirstBars && Bars.CurrentBar <= 5;
            bool periodic = LogEveryNBars > 0 && (Bars.CurrentBar % LogEveryNBars == 0);
            bool last     = DumpLastBar && Bars.LastBarOnChart;
            if (firstFew || periodic || last)
                LogBar(last ? "LAST" : (firstFew ? "FIRST" : "PERIODIC"));
        }

        // ---------- diagnostics ----------

        private void TryLogDataSeriesInfo()
        {
            LogOneSeries("Data1", () => Bars);
            LogOneSeries("Data2", () => BarsOfData(2));
            LogOneSeries("Data3", () => BarsOfData(3));
        }

        private void LogOneSeries(string tag, Func<IInstrument> getter)
        {
            try
            {
                var d = getter();
                if (d == null) { Log("[{0}] NULL", tag); return; }
                Log("[{0}] OK Name='{1}' Resolution='{2}' BarsCount={3}",
                    tag, d.Info.Name, d.Info.Resolution, d.CurrentBar);
            }
            catch (Exception ex)
            {
                Log("[{0}] FAIL: {1}", tag, ex.Message);
            }
        }

        private void LogBar(string tag)
        {
            Log("--- BAR#{0} {1} time={2} status={3} ---",
                Bars.CurrentBar, tag, Bars.Time[0], Bars.Status);
            DumpRow("D1", Bars);
            SafeDump("D2", () => BarsOfData(2));
            SafeDump("D3", () => BarsOfData(3));
            SafeAdx();
        }

        private void SafeDump(string tag, Func<IInstrument> g)
        {
            try
            {
                var d = g();
                if (d != null) DumpRow(tag, d);
                else           Log("  {0}: null", tag);
            }
            catch (Exception e) { Log("  {0}: EX {1}", tag, e.Message); }
        }

        private void DumpRow(string tag, IInstrument d)
        {
            Log("  {0}: O={1:F5} H={2:F5} L={3:F5} C={4:F5} V={5} bar#={6} time={7}",
                tag, d.Open[0], d.High[0], d.Low[0], d.Close[0], d.Volume[0], d.CurrentBar, d.Time[0]);
        }

        private void SafeAdx()
        {
            try { Log("  ADX(4)={0:F2}", m_adx[0]); }
            catch (Exception e) { Log("  ADX: EX {0}", e.Message); }
        }

        // ---------- logging plumbing ----------

        private void OpenLogFile()
        {
            try
            {
                Directory.CreateDirectory(@"C:\TEMP");
                m_filePath = string.Format(@"C:\TEMP\DivergenceProbe_{0}.log",
                    DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                m_file = new StreamWriter(m_filePath, false) { AutoFlush = true };
            }
            catch (Exception ex)
            {
                m_file = null;
                Output.WriteLine("[DivergenceProbe] cannot open log file: " + ex.Message);
            }
        }

        private void Log(string fmt, params object[] args)
        {
            string line = (args == null || args.Length == 0) ? fmt : string.Format(fmt, args);
            Output.WriteLine(line);
            if (m_file != null)
            {
                try { m_file.WriteLine(line); } catch { }
            }
        }
    }
}
