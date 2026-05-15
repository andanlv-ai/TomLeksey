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
        // === VERSION: 2026-05-13-v0.2 ===
        [Input] public int  LogEveryNBars { get; set; }
        [Input] public bool LogFirstBars  { get; set; }
        [Input] public bool DumpLastBar   { get; set; }

        private Function.ADX m_adx;
        private IInstrument  m_d2;
        private IInstrument  m_d3;
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
            m_adx = new Function.ADX(this);
        }

        protected override void StartCalc()
        {
            m_adx.Length = 4;
            OpenLogFile();
            Output.Clear();
            Log("=== DivergenceProbe v0.2 START [{0}] {1} ===",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Bars.Info.Name);
            Log("    Params: LogEveryNBars={0} LogFirstBars={1} DumpLastBar={2}",
                LogEveryNBars, LogFirstBars, DumpLastBar);
            Log("    LogFile: {0}", m_filePath);

            // --- ПОЭТАПНАЯ ДИАГНОСТИКА доступа к Data2/Data3 ---
            m_d2 = ProbeOneSeries("Data2", 2);
            m_d3 = ProbeOneSeries("Data3", 3);

            // Data1 — простая проверка
            ProbeSimple("Data1", () => Bars);
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

        private void ProbeSimple(string tag, Func<IInstrument> getter)
        {
            IInstrument d = null;
            try { d = getter(); }
            catch (Exception ex) { LogEx("[" + tag + "] step=ctor", ex); return; }
            if (d == null) { Log("[{0}] NULL", tag); return; }

            string name = "?";
            try { name = d.Info.Name; }
            catch (Exception ex) { LogEx("[" + tag + "] step=Info.Name", ex); }

            int curBar = -1;
            try { curBar = d.CurrentBar; }
            catch (Exception ex) { LogEx("[" + tag + "] step=CurrentBar", ex); }

            Log("[{0}] OK name='{1}' currentBar={2}", tag, name, curBar);
        }

        // Расширенный probe — пробует ВСЕ операции по отдельности.
        // Возвращает IInstrument чтобы переиспользовать в CalcBar (если ctor сработал).
        private IInstrument ProbeOneSeries(string tag, int dataNum)
        {
            IInstrument d = null;

            // Step 1: ctor (получение объекта)
            try { d = BarsOfData(dataNum); }
            catch (Exception ex) { LogEx("[" + tag + "] step=BarsOfData(" + dataNum + ")", ex); return null; }
            if (d == null) { Log("[{0}] step=BarsOfData -> NULL", tag); return null; }
            Log("[{0}] step=BarsOfData -> OK (object received)", tag);

            // Step 2: Info.Name
            try { Log("[{0}] step=Info.Name -> '{1}'", tag, d.Info.Name); }
            catch (Exception ex) { LogEx("[" + tag + "] step=Info.Name", ex); }

            // Step 3: CurrentBar
            try { Log("[{0}] step=CurrentBar -> {1}", tag, d.CurrentBar); }
            catch (Exception ex) { LogEx("[" + tag + "] step=CurrentBar", ex); }

            // Step 4: Close[0] (то что делает Spread__Diff)
            try { Log("[{0}] step=Close[0] -> {1:F5}", tag, d.Close[0]); }
            catch (Exception ex) { LogEx("[" + tag + "] step=Close[0]", ex); }

            return d;
        }

        private void LogBar(string tag)
        {
            Log("--- BAR#{0} {1} time={2} status={3} ---",
                Bars.CurrentBar, tag, Bars.Time[0], Bars.Status);
            DumpRow("D1", Bars);
            DumpRowOrError("D2", m_d2);
            DumpRowOrError("D3", m_d3);
            SafeAdx();
        }

        private void DumpRowOrError(string tag, IInstrument d)
        {
            if (d == null) { Log("  {0}: <not available, see StartCalc errors>", tag); return; }
            try
            {
                Log("  {0}: O={1:F5} H={2:F5} L={3:F5} C={4:F5} V={5} bar#={6} time={7}",
                    tag, d.Open[0], d.High[0], d.Low[0], d.Close[0], d.Volume[0], d.CurrentBar, d.Time[0]);
            }
            catch (Exception ex) { LogEx("  " + tag + " DumpRow", ex); }
        }

        private void DumpRow(string tag, IInstrument d)
        {
            Log("  {0}: O={1:F5} H={2:F5} L={3:F5} C={4:F5} V={5} bar#={6} time={7}",
                tag, d.Open[0], d.High[0], d.Low[0], d.Close[0], d.Volume[0], d.CurrentBar, d.Time[0]);
        }

        private void SafeAdx()
        {
            try { Log("  ADX(4)={0:F2}", m_adx[0]); }
            catch (Exception e) { LogEx("  ADX(4)", e); }
        }

        // ---------- logging plumbing ----------

        private void LogEx(string where, Exception ex)
        {
            Log("{0} FAIL: {1} :: {2}", where, ex.GetType().FullName, ex.Message);
            // первая строка стектрейса покажет в каком .dll/функции ELAPI ломается
            string st = ex.StackTrace ?? "<no stack>";
            int nl = st.IndexOf('\n');
            string firstLine = nl > 0 ? st.Substring(0, nl).TrimEnd('\r') : st;
            Log("    stack[0]: {0}", firstLine);
            if (ex.InnerException != null)
                Log("    inner:    {0} :: {1}", ex.InnerException.GetType().FullName, ex.InnerException.Message);
        }

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
