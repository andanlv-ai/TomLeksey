# Changelog

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
