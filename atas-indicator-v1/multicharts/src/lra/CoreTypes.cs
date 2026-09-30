using System;
using System.Collections.Generic;

// Типы ядра LraTrader. Контракт между файлами — менять только по согласованию (interfaces.md).
// Ядро (Core*.cs) не использует типы MultiCharts: оно собирается и тестируется без них (tools/test.sh).
// История баров: IList<LraBar> h, h[0] — самый старый, h[h.Count - 1] — текущий закрытый бар.
// «Close[k]» в ТЗ = h[h.Count - 1 - k].Close.
namespace PowerLanguage.Strategy
{
    public class LraBar
    {
        public DateTime Time, Paris;          // время графика и время Парижа
        public string Session = "OFF";        // ASIA / EUROPE / AMERICA / OFF (ТЗ §2)
        public double Open, High, Low, Close;
        public double Volume, Ask, Bid;       // Volume = Ask + Bid по профилю
        public double UpTicks, DownTicks;     // число сделок вверх / вниз
        public double[] LvPrice = new double[0], LvAsk = new double[0], LvBid = new double[0]; // уровни профиля бара
        public bool NoData;                   // у бара нет данных профиля
    }

    // Параметры (ТЗ §9) + размеры тика и пипса. Значения по умолчанию — стартовые.
    public class LraSettings
    {
        public int    ImbBars = 2;
        public double MinShare = 0.05;
        public double LevelImbPct = 0.3;
        public int    MinLevels = 3;
        public double MaxRangePips = 50;
        public int    MinRangeBars = 4;
        public int    MinActiveBars = 2;
        public int    MaxRangeAgeBars = 120;
        public int    SwingLookback = 48;
        public int    OffsetTicks = 1;
        public int    AtrPeriod = 14;
        public double StallAtr = 0.5;
        public double StrongAtr = 1.5;
        public double MaxStopPips = 30;
        public double MaxTakePips = 15;
        public double AddStepPips = 10;
        public string AddTargetMode = "SAME_TP";   // SAME_TP | ENTRY1
        public double LraMinDistPips = 30;
        public double DayTargetShare = 0.5;
        public double PipSize = 0.0001;
        public double Tick = 0.00005;               // цена одного тика инструмента
    }

    // Дисбаланс окна (ТЗ §3)
    public class LraImbalance
    {
        public double VolDelta, TickDelta, VolShare, TickShare;
        public int BuyLevels, SellLevels;
        public int Dir;             // +1 толпа покупает, -1 продаёт, 0 нет
        public string Reason = "";  // при Dir = 0: WEAK / ONE_PRICE / CONFLICT / NO_DATA
    }

    // Диапазон ЛРА (ТЗ §4)
    public class LraRange
    {
        public string Id;                     // Start в формате yyyyMMddHHmm
        public int StartIdx, EndIdx, BornIdx; // индексы в h: первый и последний бар диапазона, бар выхода
        public DateTime Start, End;
        public double High, Low, Poc, Volume;
        public int Bars, ExitDir;             // ExitDir +1 — вышли вверх, -1 — вниз
        public bool Done, Expired;
        public bool Active { get { return !Done && !Expired; } }
    }

    // Сигнал сценария (ТЗ §6)
    public class LraSignal
    {
        public string Book;                   // AGAINST | MOMENTUM
        public int Dir;                       // направление сделки
        public double Entry, SL, TP, TP2 = double.NaN;
        public int WinFrom;                   // индекс первого бара окна дисбаланса
        public LraImbalance Crowd;
        public LraRange Range;                // ближайший активный ЛРА или null
        public double RangeDistPips = double.NaN;
        public string Skip = "";              // "" — входим; NO_LEVEL / AGAINST_LRA / WEEKEND / NEWS / IN_POSITION
    }

    public class LraUnit { public int N; public int OpenIdx; public DateTime OpenTime; public double Entry, TP; }

    // Виртуальная позиция книги (ТЗ §7)
    public class LraPosition
    {
        public string Book, SignalId;
        public int Dir, WinFrom, EntryIdx;
        public double SL;
        public List<LraUnit> Units = new List<LraUnit>();
    }

    public class LraExit { public LraUnit Unit; public double Price; public string Reason; public bool Ambiguous; }
}
