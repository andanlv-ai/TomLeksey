# Индикатор: Divergence Detector (CumDelta vs Price)

## 1. Назначение и описание

Индикатор выявляет расхождения (дивергенции) между двумя кумулятивными дельтами на графике и движением цены. Сигнал генерируется когда:
- Цена находится в консолидации (стоит на месте), а кумулятивные дельты разнонаправлены
- Кумулятивные дельты растут, а цена падает (отрицательная дивергенция)
- Кумулятивные дельты падают, а цена растет (отрицательная дивергенция)

## 2. Входные параметры

```
[Input] double  PriceRangeThreshold      = 0.5   // Порог для определения консолидации цены (в %)
[Input] int     CumDeltaWindow           = 20    // Окно для анализа направления кумдельт (бары)
[Input] double  DivergenceMinPercent     = 2.0   // Минимальное расхождение между дельтами и ценой (%)
[Input] bool    LogEveryBar              = false // Логировать каждый бар (для отладки)
```

## 3. Логика работы

### Шаг 1: Получение данных
- Прочитать два источника кумулятивной дельты с графика (из двух отдельных окон/индикаторов)
- Получить текущую цену (Close)
- Получить уровень открытия бара (Open)

### Шаг 2: Анализ цены
```
PriceRange = (High - Low) / Close * 100
IsConsolidation = (PriceRange < PriceRangeThreshold)
PriceDirection = Close > Open ? 1 : (Close < Open ? -1 : 0)
```

### Шаг 3: Анализ кумулятивных дельт
- Определить направление CumDelta1 за последние N бар (восходящий = 1, нисходящий = -1)
- Определить направление CumDelta2 за последние N бар (восходящий = 1, нисходящий = -1)
- Вычислить разницу между CumDelta1 и CumDelta2 в процентах от максимума

### Шаг 4: Детектирование дивергенции

**Сценарий 1: Консолидация + дельты расходятся**
```
if (IsConsolidation && CumDelta1Direction != CumDelta2Direction)
  Signal = 1  // BUY (если хотя бы одна дельта растет)
  Signal = -1 // SELL (если обе падают в разные стороны)
```

**Сценарий 2: Дельта растет, цена падает**
```
if ((CumDelta1Direction == 1 || CumDelta2Direction == 1) && PriceDirection == -1)
  if (DeltaDivergence >= DivergenceMinPercent)
    Signal = 1  // Потенциальный BUY
```

**Сценарий 3: Дельта падает, цена растет**
```
if ((CumDelta1Direction == -1 || CumDelta2Direction == -1) && PriceDirection == 1)
  if (DeltaDivergence >= DivergenceMinPercent)
    Signal = -1 // Потенциальный SELL
```

## 4. Визуализация

- **Столбик в подпанели индикатора:**
  - `1` (зеленый) → сигнал BUY / бычья дивергенция
  - `-1` (красный) → сигнал SELL / медвежья дивергенция
  - `0` (серый) → нет сигнала
  
- **Логирование (в Output):**
  - Направление обеих дельт
  - Направление цены и ее диапазон
  - Выявленная дивергенция (если есть)
  - Тип сигнала (консолидация + разброс / дивергенция цены)

## 5. Примеры сценариев

### Пример 1: Консолидация + разные дельты
```
Open=1.0950, Close=1.0952 → PriceRange=0.3% (консолидация)
CumDelta1: +100 (растет)
CumDelta2: -50  (падает)
→ Signal = BUY (зеленый столбик)
```

### Пример 2: Цена падает, дельты растут
```
Open=1.0960, Close=1.0950 → PriceDirection = -1 (вниз)
CumDelta1: +200 (растет)
CumDelta2: +150 (растет)
Divergence = 5% (достаточно для сигнала)
→ Signal = BUY (зеленый столбик)
```

### Пример 3: Цена растет, дельты падают
```
Open=1.0950, Close=1.0960 → PriceDirection = 1 (вверх)
CumDelta1: -100 (падает)
CumDelta2: -80  (падает)
Divergence = 4%
→ Signal = SELL (красный столбик)
```

## 6. Архитектура кода

```csharp
namespace PowerLanguage.Indicator
{
  [UpdateOnEveryTick(true)]
  [SameAsSymbol(false)]
  public class DivergenceDetector : IndicatorObject
  {
    [Input] public double PriceRangeThreshold { get; set; }
    [Input] public int    CumDeltaWindow      { get; set; }
    [Input] public double DivergenceMinPercent { get; set; }
    [Input] public bool   LogEveryBar         { get; set; }

    private IPlotObject _signal;
    private int _lastCalcBar = -1;

    protected override void Create() 
    {
      _signal = AddPlot(...); // Как в TradesAlert_v2
    }

    protected override void CalcBar()
    {
      // 1. GetCumDeltas() - получить две дельты
      // 2. AnalyzePrice() - определить направление/консолидацию
      // 3. AnalyzeCumDeltas() - определить направления обеих дельт
      // 4. DetectDivergence() - логика из шага 4
      // 5. SetSignal() - вывести сигнал на график
      // 6. Log() - вывести в Output
    }

    private int GetSignal(...) { ... }
    private double GetCumDeltaDirection(...) { ... }
    private bool IsConsolidation(...) { ... }
  }
}
```

## 7. Особенности реализации

- **Доступ к кумулятивным дельтам:** нужно решить, как читать две дельты (возможно, через VolumeProfile API или отдельные индикаторы на графике)
- **Синхронизация:** убедиться, что анализ происходит на текущем баре (как в TradesAlert_v2 с флагом `_alertFiredThisBar`)
- **Отладка:** добавить опцию `LogEveryBar` для вывода всех значений в Output
