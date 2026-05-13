# План: Индикатор Divergence Detector (CumDelta vs Price)

## ⚠️ Рабочий процесс (строго)

1. **Сбор информации** — пробник, прогон на реальном чарте, дамп логов.
2. **Анализ и проверка** — изучаем логи, подтверждаем что Data2/Data3 действительно содержат кумдельту, ADX доступна, индексы синхронизированы. Фиксируем факты.
3. **Код** — только после подтверждённых фактов. Никаких догадок и «обычно так работает». Если факт неизвестен → расширяем диагностику, не угадываем.

### Правило перехода между этапами (gating)

- **Каждый этап завершается явным подтверждением** от пользователя («ок, переходим дальше»).
- Если у меня (Claude) возникают вопросы или неоднозначности на текущем этапе — **СТОП**, задаю вопросы, ждём ответ, решаем. Только потом следующий этап.
- Если у пользователя возникают вопросы — отвечаем, фиксируем решение в плане/памяти, и только потом дальше.
- Запрещено «срезать углы»: пропустить этап, начать кодировать «параллельно с диагностикой», предположить ответ за пользователя.

## Контекст

Нужен новый индикатор для MultiCharts .NET, который сравнивает движение цены (Data1) с двумя кумулятивными дельтами (Data2 и Data3) и рисует столбик высотой `1` в подпанели когда обнаружена дивергенция, и `0` когда дивергенции нет.

**Условия дивергенции (любое из):**
1. Цена идёт **вверх**, обе дельты идут **вниз** (красные бары)
2. Цена идёт **вниз**, обе дельты идут **вверх** (зелёные бары)
3. Цена **во флэте** (ADX(FlatPeriod) < FlatAdxLevel), а обе дельты в одну сторону: **обе красные** или **обе зелёные** `DeltaBarsCount` баров подряд

Образец рендеринга столбика (AddPlot Histogram + Set + Colors) — [TradesAlert_v2.Indicator.MC.CS](../../src/TradesAlert_v2.Indicator.MC.CS) строки 37-41, 149-150.

## Параметры (Inputs)

```
[Input] int    DeltaBarsCount  = 2    // сколько одноцветных баров дельты подряд (Data2/Data3)
[Input] int    PriceBarsCount  = 2    // сколько баров цены подряд для определения направления ↑/↓
[Input] int    FlatPeriod      = 4    // период ADX для детекции флэта
[Input] double FlatAdxLevel    = 20.0 // ADX < этого значения = цена во флэте
[Input] bool   LogEveryBar     = false
```

## Фаза 0: Диагностика доступности данных (делаем ПЕРВОЙ)

Прежде чем писать логику дивергенции, убеждаемся что Data1/Data2/Data3 реально читаются и в них то, что мы ожидаем. Создаём отдельный лёгкий индикатор-пробник — без сигналов, только лог.

### Файл: `atas-indicator-v1/multicharts/DivergenceProbe.Indicator.MC.CS`

```csharp
[UpdateOnEveryTick(false)]   // только закрытые бары на этапе диагностики
[SameAsSymbol(false)]
public class DivergenceProbe : IndicatorObject
{
    [Input] public int LogEveryNBars { get; set; }   // default 20
    [Input] public bool LogFirstBars { get; set; }   // default true — лог первых 5 баров
    [Input] public bool DumpLastBar  { get; set; }   // default true — финальный дамп

    private int _barsLogged;

    public DivergenceProbe(object ctx) : base(ctx) {
        LogEveryNBars = 20; LogFirstBars = true; DumpLastBar = true;
    }

    protected override void StartCalc() {
        Output.Clear();
        Output.WriteLine("=== DivergenceProbe START [{0}] ===", DateTime.Now);
        TryLogDataSeriesInfo();
    }

    protected override void CalcBar() {
        bool firstFew = LogFirstBars && Bars.CurrentBar <= 5;
        bool periodic = LogEveryNBars > 0 && Bars.CurrentBar % LogEveryNBars == 0;
        bool last     = DumpLastBar && Bars.LastBarOnChart;
        if (firstFew || periodic || last) LogBar(last ? "LAST" : (firstFew ? "FIRST" : "PERIODIC"));
    }
}
```

### Что проверяет диагностика

**Лог:** все диагностические сообщения пишутся ОДНОВРЕМЕННО в `Output.WriteLine` (для просмотра в PowerLanguage Editor) и в файл `C:\TEMP\DivergenceProbe_<yyyyMMdd_HHmmss>.log` (через `StreamWriter` с авто-flush) — чтобы можно было проанализировать дамп оффлайн без перезапуска чарта. Файл создаётся в `StartCalc()`, закрывается в `StopCalc()`.

**1. Существование Data2 и Data3** — оборачиваем доступы в try/catch:
```csharp
private void TryLogDataSeriesInfo() {
    LogOneSeries("Data1", () => Bars);
    LogOneSeries("Data2", () => BarsOfData(2));
    LogOneSeries("Data3", () => BarsOfData(3));
}

private void LogOneSeries(string tag, Func<IInstrument> getter) {
    try {
        var d = getter();
        if (d == null) { Output.WriteLine("[{0}] NULL", tag); return; }
        Output.WriteLine("[{0}] OK Name='{1}' Resolution='{2}' BarsCount={3}",
            tag, d.Info.Name, d.Info.Resolution, d.CurrentBar);
    } catch (Exception ex) {
        Output.WriteLine("[{0}] FAIL: {1}", tag, ex.Message);
    }
}
```

**2. Чтение OHLC по каждому бару** — выводим Open/High/Low/Close/Volume для всех трёх Data на одном баре. Это покажет:
- реально ли в Data2/Data3 значения кумдельты (а не дублирующая цена);
- синхронизированы ли индексы баров между Data1/Data2/Data3;
- какой знак Close-Open у дельты (понимание «цвета» бара).

```csharp
private void LogBar(string tag) {
    Output.WriteLine("--- BAR#{0} {1} time={2} {3} ---",
        Bars.CurrentBar, tag, Bars.Time[0], Bars.Status);
    DumpRow("D1", Bars);
    SafeDump("D2", () => BarsOfData(2));
    SafeDump("D3", () => BarsOfData(3));
}

private void SafeDump(string tag, Func<IInstrument> g) {
    try { var d = g(); if (d != null) DumpRow(tag, d); else Output.WriteLine("  {0}: null", tag); }
    catch (Exception e) { Output.WriteLine("  {0}: EX {1}", tag, e.Message); }
}

private void DumpRow(string tag, IInstrument d) {
    Output.WriteLine("  {0}: O={1:F5} H={2:F5} L={3:F5} C={4:F5} V={5} bar#={6} time={7}",
        tag, d.Open[0], d.High[0], d.Low[0], d.Close[0], d.Volume[0], d.CurrentBar, d.Time[0]);
}
```

**3. Доступность встроенной ADX** — пробуем создать `Function.ADX` в `Create()` и читать значение в `CalcBar()`:
```csharp
private Function.ADX m_adx;
protected override void Create() { m_adx = new Function.ADX(this); m_adx.Length = 4; }
// в LogBar: try { Output.WriteLine("  ADX(4)={0:F2}", m_adx[0]); } catch { ... }
```

### Verification фазы 0

1. Закинуть `DivergenceProbe` на чарт где Data1=инструмент, Data2 и Data3 = два CumDelta индикатора (или два инструмента, в зависимости от того как пользователь подвесит дельты).
2. Открыть Output и проверить:
   - `[Data2] OK ...` и `[Data3] OK ...` без `NULL`/`FAIL`.
   - Значения O/H/L/C на Data2/Data3 отличаются от Data1 и выглядят как кумдельта (растут/падают, а не цена).
   - Time на Data1/Data2/Data3 совпадает (бары синхронизированы).
   - `ADX(4)` отдаёт число (не NaN/исключение).
3. Если что-то не так — фиксируем ДО того как писать логику дивергенции.

### Точки риска, которые подсветит диагностика

- В MultiCharts CumDelta может быть индикатором поверх той же серии данных, а не отдельной серией — тогда `BarsOfData(2)` вернёт цену, а не дельту. Способ передачи дельт может потребовать переосмысления (Global Variables, прямой расчёт из ленты сделок и т.п.).
- Разные таймфреймы Data1/Data2/Data3 — индексы [0] могут не соответствовать одному и тому же моменту времени.
- Меньшая глубина истории на Data2/Data3 — обращение к `[N]` упадёт пока не накопилось N баров.

---

## Фаза 1: Основной индикатор (СТАБЫ — кодируется ПОСЛЕ анализа логов фазы 0)

> На этапе планирования — **только сигнатуры функций и комментарии «что делает»**. Реализация откладывается до тех пор пока диагностика фазы 0 не подтвердит способ получения дельт и стабильность ADX.

### Файл: `atas-indicator-v1/multicharts/DivergenceDetector.Indicator.MC.CS`

```csharp
[UpdateOnEveryTick(true)]
[SameAsSymbol(false)]
public class DivergenceDetector : IndicatorObject
{
    [Input] public int    DeltaBarsCount { get; set; }   // default 2
    [Input] public int    PriceBarsCount { get; set; }   // default 2
    [Input] public int    FlatPeriod     { get; set; }   // default 4
    [Input] public double FlatAdxLevel   { get; set; }   // default 20.0
    [Input] public bool   LogEveryBar    { get; set; }   // default false

    private IPlotObject  _signal;
    private Function.ADX _adx;

    public DivergenceDetector(object ctx) : base(ctx) { /* defaults */ }

    // === Жизненный цикл ===
    protected override void Create()    { /* AddPlot histogram + создать _adx */ }
    protected override void StartCalc() { /* лог старта версии + параметров */ }
    protected override void CalcBar()   { /* основной цикл — см. ниже */ }

    // === Стабы основной логики (Фаза 1) ===

    // Возвращает 1 если последние N баров серии все зелёные (Close>Open),
    // -1 если все красные (Close<Open), 0 если смешано или данных мало.
    private int  GetDeltaDirection(IInstrument data, int barsCount) { return 0; }

    // То же самое для Data1 (цены).
    private int  GetPriceDirection(int barsCount) { return 0; }

    // true если ADX(FlatPeriod)[0] < FlatAdxLevel.
    private bool IsPriceFlat() { return false; }

    // Главное условие. Возвращает 1 если выполнено любое из трёх правил
    // дивергенции (см. секцию «Условия дивергенции» вверху плана), иначе 0.
    private int  ComputeDivergenceSignal(int priceDir, int d1, int d2, bool flat) { return 0; }

    // Безопасный геттер серии: try/catch + null-check, в случае ошибки логирует и возвращает null.
    private IInstrument SafeGetData(int n) { return null; }

    // Подробный лог одного бара — раз в N тиков или когда LogEveryBar=true.
    // Формат лога повторяет Фазу 0 (D1/D2/D3 OHLC + ADX + d1/d2/priceDir/flat/divergence).
    private void LogBar(int priceDir, int d1, int d2, bool flat, int signal) { }

    // === Скелет CalcBar() ===
    // 1. d2Series = SafeGetData(2); d3Series = SafeGetData(3);
    //    если null → _signal.Set(0,0); return;
    // 2. priceDir = GetPriceDirection(PriceBarsCount);
    //    d1       = GetDeltaDirection(d2Series, DeltaBarsCount);
    //    d2       = GetDeltaDirection(d3Series, DeltaBarsCount);
    //    flat     = IsPriceFlat();
    // 3. signal   = ComputeDivergenceSignal(priceDir, d1, d2, flat);
    //    _signal.Set(0, signal);
    //    _signal.Colors[0] = signal == 1 ? Color.Yellow : Color.Gray;
    // 4. if (LogEveryBar || Bars.LastBarOnChart) LogBar(...);
}
```

### Что НЕ делаем на этапе планирования
- Не пишем тела функций — только сигнатуры с комментарием.
- Не выбираем окончательную форму/цвет столбика (в TODO).
- Не добавляем алерты — отдельная итерация.

## Подключение в MultiCharts

Пользователь должен на чарте подвесить:
- Data1 = инструмент (цена)
- Data2 = первая CumDelta (как самостоятельный data series, сформированный в чарте)
- Data3 = вторая CumDelta
- Затем добавить `DivergenceDetector` в подпанель

## Verification (после реализации Фазы 1)

1. Компиляция: F7 → Build OK без warning.
2. Smoke test: добавить на чарт где уже работает `DivergenceProbe`. В Output — строка старта.
3. Включить `LogEveryBar = true` и пройтись по последним 50 барам, сверить:
   - dir-значения совпадают с цветами свечей в дельтах глазами;
   - ADX < 20 действительно совпадает с визуальным флэтом;
   - сигнал зажигается на ожидаемых сценариях.
4. Edge cases: первые баров < max(DeltaBarsCount, PriceBarsCount, FlatPeriod) → 0; Data2/Data3 отсутствуют → 0 без краша.

## Открытые вопросы / TODO

- Цвет столбика и форма (Histogram vs Bar) — оставлю Histogram жёлтый, можно поменять при ревью.
- Алерт (Alerts.Alert) — пока не делаем; добавим если попросишь.
