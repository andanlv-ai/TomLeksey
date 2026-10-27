// BarRecorder.Indicator.MC.CS — пишет каждый закрытый бар с профилем объёма в CSV для анализа нескольких графиков
// Путь: C:\ProgramData\TS Support\MultiCharts .NET64 Special Edition\StudyServer\Techniques\CS\BarRecorder.Indicator.CS
//
// Файл: <Dir>\<символ>_<Label>.csv. На графике нужен Volume Profile: Every 1 Bar.
// Breakdown = Ask vs Bid Traded Delta → дельта по объёму; Up vs Down Tick Delta → по сделкам (ап/даунтик).
// Файл только дописывается барами новее последней строки: CQG отдаёт тики примерно за месяц,
// и перезапись затёрла бы нулями всё, что старше.
// Levels: "цена:ask:bid;..." по всем ценам бара. Пустой профиль (старая история без тиков) — Ask=Bid=0, Levels пусто.

using System;
using System.Globalization;
using System.IO;
using System.Text;
using PowerLanguage.VolumeProfile;

namespace PowerLanguage.Indicator
{
    [UpdateOnEveryTick(false)]
    [SameAsSymbol(true)]
    public class BarRecorder : IndicatorObject
    {
        [Input] public string Label { get; set; }
        [Input] public string Dir   { get; set; }

        private static readonly CultureInfo CI = CultureInfo.InvariantCulture;
        private const string Header = "Time,Symbol,Label,Open,High,Low,Close,Ask,Bid,Delta,Levels";
        private string _path;
        private DateTime _last;
        private bool _vpWaiting;
        private int _vpPolls;

        public BarRecorder(object ctx) : base(ctx)
        {
            Label = "VOL";
            Dir   = @"D:\TradeLog\Bars";
        }

        protected override void StartCalc()
        {
            string sym = Bars.Info.Name;
            foreach (char c in Path.GetInvalidFileNameChars()) sym = sym.Replace(c, '_');
            Directory.CreateDirectory(Dir);
            _path = Path.Combine(Dir, sym + "_" + Label + ".csv");
            _last = DateTime.MinValue;
            try
            {
                if (!File.Exists(_path)) { File.WriteAllText(_path, Header + "\r\n"); return; }
                string[] lines = File.ReadAllLines(_path);
                if (lines.Length > 1)
                    DateTime.TryParseExact(lines[lines.Length - 1].Split(',')[0], "yyyy-MM-dd HH:mm", CI, DateTimeStyles.None, out _last);
            }
            catch (IOException ex) { Output.WriteLine("[BarRecorder] {0} занят: {1}", _path, ex.Message); }
        }

        protected override void CalcBar()
        {
            if (!VpReady()) return;
            if (Bars.Status != EBarState.Close) return;
            if (Bars.Time[0] <= _last) return;

            decimal ask = 0, bid = 0;
            var levels = new StringBuilder();
            IProfile p = null;
            // ItemForBar(Current) отдаёт профиль следующего бара (сверено по High/Low в CSV), поэтому -1
            try { p = this.VolumeProfile.ItemForBar(Bars.FullSymbolData.Current - 1); } catch { }
            if (p != null && !p.Empty)
            {
                foreach (ILevel lv in p.Values)
                {
                    ask += lv.AskTradedValue;
                    bid += lv.BidTradedValue;
                    if (levels.Length > 0) levels.Append(';');
                    levels.AppendFormat(CI, "{0}:{1}:{2}", (double)lv.Price, lv.AskTradedValue, lv.BidTradedValue);
                }
            }

            string line = string.Format(CI, "{0:yyyy-MM-dd HH:mm},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10}\r\n",
                Bars.Time[0], Bars.Info.Name, Label, Bars.Open[0], Bars.High[0], Bars.Low[0], Bars.Close[0],
                ask, bid, ask - bid, levels);
            // ponytail: файл открыт в Excel — строка теряется; читать копию файла, а не оригинал
            try { File.AppendAllText(_path, line); }
            catch (IOException ex) { Output.WriteLine("[BarRecorder] {0} занят: {1}", _path, ex.Message); }
        }

        // MC строит профиль объёма позже расчёта истории и сигнала о готовности не шлёт.
        // Пока профилей нет — бары пропускаем и опрашиваем последний бар; появились — полный пересчёт.
        // ponytail: опрос раз в 2 с, не больше 60 раз; дальше — ручной Status Off/On
        private bool VpReady()
        {
            var vp = this.VolumeProfile;
            var svc = vp as IProfilesCollectionService;
            if (svc != null) svc.Syncronize();
            bool ready = vp != null && vp.Items != null && vp.Items.Count > 0;
            if (!ready)
            {
                _vpWaiting = true;
                if (Bars.LastBarOnChart && _vpPolls < 60) { _vpPolls++; ExecControl.RecalcLastBarAfter(TimeSpan.FromSeconds(2)); }
                return false;
            }
            if (_vpWaiting) { _vpWaiting = false; _vpPolls = 0; ExecControl.Recalculate(); return false; }
            return true;
        }
    }
}
