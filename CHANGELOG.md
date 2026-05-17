# Changelog

## [v4.6] — 2026-05-17 (DivergenceDetector)

### Added
- **Фильтр узкого диапазона для streak-плота**: синий бар теперь рисуется только когда цена в окне `StreakBars` предыдущих баров почти не движется — паттерн absorption/accumulation. Условие: `(HH−LL окна) / ATR(AtrPeriod) ≤ MaxRangeATR`.
- Новые Input-параметры: `AtrPeriod=14`, `MaxRangeATR=0.7`, `UseNRFilter=false`, `NRLookback=7`.
- Метод `IsNarrowRange()` использует встроенный `Function.AverageTrueRange`.
- Лог-строка расширена полями `streak`, `narrow`, `rangeATR` для диагностики.

### Changed
- Streak-плот теперь требует одновременно: `GetStreakDir() != 0` **и** `IsNarrowRange() == true`.
- Дивергентный плот (`_plot`, Red/Lime) и email-алерты — без изменений.

## [v4.5] — 2026-05-17 (DivergenceDetector)

### Added
- **Streak plot**: синий гистограммный бар вниз когда `StreakBars` (по умолчанию 3) предыдущих баров имеют дельту одного знака. Input `StreakBars`.

## [v4.4] — 2026-05-17 (DivergenceDetector)

### Added
- **Фильтр по объёму**: сигнал дивергенции срабатывает только если объём текущего бара больше объёма предыдущего. Объём берётся из VolumeProfile (Ask+Bid traded), fallback — `Bars.Volume[0]`.

### Changed
- `GetCurrentDelta()` → `GetCurrentDeltaAndVolume(out decimal volume)`: один проход по `ILevel` возвращает и дельту, и объём.
- Лог последнего бара теперь включает `vol` и `prevVol` для диагностики.

## [v3.4] — 2026-05-07

### Fixed
- **Алерт ровно 1 раз на бар**: добавлен флаг `_alertFiredThisBar`, который сбрасывается при смене бара и взводится при первом срабатывании. Условие алерта изменено с `_barTickCount == LogEveryNTicks` на `!_alertFiredThisBar && _barTickCount >= LogEveryNTicks` — больше нет пропусков, если тик быстро превысил порог.
- **Лог `logFull`** отвязан от `isAlertTick` — теперь периодический лог не зависит от того, выстрелил ли алерт.

### Added
- Лог-строка `[ALERT] Вызов Alerts.Alert() | <time> | instrument=... BarTick=...` при каждом срабатывании.

## [v3.2] — 2026-05-01

### Changed
- **Алерт по тику N нового бара**: сигнал срабатывает после `LogEveryNTicks` тиков с открытия бара, не ждёт закрытия
- **Лог**: шапка CalcBar теперь показывает `SIGNAL=BUY/SELL/NONE` и номер тика на баре (`BarTick#`) отдельно от общего счётчика
- **Время в логах**: исправлено с UTC на локальное (`DateTime.Now`)
- **Дефолты**: `LevelImbalancePct=35%`, `MinStackHeight=3` (было 10% и 35)

## [0.2.0] — 2026-04-27

### Added
- **Empirical analysis**: Analyzed 20 historical PNG screenshots — 20/20 confirmed stacked imbalance patterns
- **Two-level imbalance detection** in TZ v1:
  - **Level 1 (stacked)**: Scans `IProfile.Values` for 3+ consecutive levels with `|Delta%| ≥ LevelImbalancePct`
  - **Level 2 (aggregated)**: VP Delta% over the full 8h profile period as background filter
- **Stack metrics**: `StackDirection`, `StackHeight`, `StackTotalDelta`, `StackAvgDelta`, `StackWeight`
- **Pure/mixed stack classification**: Pure stack (all same sign) → weight × 1.5
- **New input parameters**: `LevelImbalancePct` (5.0), `MinStackHeight` (3), `MinStackWeight` (15.0), `PureStackMultiplier` (1.5)
- **Updated visualization**: Per-level histogram with stack highlighting (thicker bars), tick delta sub-plot, threshold lines
- **Python analysis script**: `analysis/analyze_imbalance.py` — HSV-based color classification for PNG imbalance detection

### Changed
- TZ v1 (`tz_indicator_imbalance_v1.md`) fully rewritten:
  - Section 2: Empirical analysis with 20/20 results table
  - Section 4: Two-level detection algorithm
  - Section 7: Stack-based entry conditions
  - Section 9: Per-level histogram visualization
  - Section 10: Restructured input parameters (5 groups)

## [0.1.0] — 2026-04-26

### Added
- Initial release: TradesAlert indicator for MultiCharts .NET
- Volume Profile Delta% calculation (Ask − Bid / Ask + Bid)
- Histogram visualization (green/red bars) in sub-panel
- Email alerts with configurable threshold and hysteresis
- Technical Zone (TZ) documents for countertrend imbalance strategy:
  - `tz_indicator_imbalance_v1.md` — stacked imbalance → aggregated VP Delta% over N bars + tick delta
  - `tz_indicator_imbalance_v2.md` — alternative TZ for countertrend strategy
- Project documentation: SESSION_CONTEXT, ATAS_CONNECT_GUIDE, VolumeProfile_API.md
- GitHub repository created: https://github.com/andanlv-ai/TomLeksey
