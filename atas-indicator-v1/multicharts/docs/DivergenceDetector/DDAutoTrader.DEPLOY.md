# DDAutoTrader — деплой и проверка

Версия: **v1.0** (2026-09-26). ТЗ: `DDAutoTrader.TZ.md`.
Тип: **Strategy** (советник) для MultiCharts .NET64 Special Edition. Торговля только виртуальная: ордеров нет, всё пишется в CSV.

## 1. Файл
```
ИЗ: C:\Users\user\TomLeksey\atas-indicator-v1\multicharts\src\DDAutoTrader.Strategy.CS
В:  C:\ProgramData\TS Support\MultiCharts .NET64 Special Edition\StudyServer\Techniques\CS\DDAutoTrader.Strategy.CS
```
PowerLanguage Editor → открыть `DDAutoTrader` → **Build → Compile** (F7). Ожидается 0 ошибок.

## 2. График (на каждый инструмент: евро, фунт, иена)
1. Новый график, символ — текущий контракт CQG. Старые (`BP6M26` и т.п.) истекли; нужны декабрьские `…Z26`
   (например `EU6Z26`, `BP6Z26`, `JY6Z26` — точное имя проверить в поиске символов).
2. Период — **60 минут**; история — не меньше 3 месяцев.
3. **Format Instrument → Volume Profile**: Show = ON, Type = Total Volume,
   Breakdown by = Ask vs Bid Traded Delta, Time → Every: 1 Bar.
4. **Insert Symbol** — тот же контракт, период **1 минута** (это Data 2, для порядка стопа и тейка). Можно скрыть.
5. **Insert Study → Strategies → DDAutoTrader**.
6. Properties стратегии → **Maximum number of bars study will reference** = 200 (или Auto-detect).
7. Для 6J задать `PipSize` (для 6E и 6B — 0.0001 по умолчанию).

Индикатор DivergenceDetector на график ставить не обязательно: советник считает сигналы сам.

## 3. Параметры
| Параметр | По умолчанию | Смысл |
|---|---|---|
| `NBars`, `StreakBars`, `UseNarrowFilter`, `AtrPeriod`, `MaxRangeATR` | 2, 3, false, 14, 0.7 | как в DivergenceDetector |
| `LogDir` | `D:\TradeLog` | папка файлов |
| `PipSize` | 0.0001 | размер пипса |
| `CommissionUsd` | 0 | комиссия за контракт туда и обратно |
| `FractalOffsetTicks` | 1 | отступ стопа и тейка за фрактал |
| `FractalLookback` | 100 | глубина поиска фрактала, баров |
| `NewsWindows` | `14:30-15:30;19:00-21:00` | окна без входов, время Парижа |

## 4. Проверка
| # | Что | Ожидается |
|---|---|---|
| 1 | `D:\TradeLog\` после запуска | `runs.csv` (START), `signals_<SYM>.csv`, `moves_<SYM>.csv`, `trades.csv` |
| 2 | В `runs.csv` | `Data2=True` — минутки подключены |
| 3 | Два перезапуска подряд (Status Off/On) | число строк в signals/moves/trades не растёт |
| 4 | Три сделки из `trades.csv` сверить с графиком | вход, фракталы стопа и тейка, выход совпадают |
| 5 | Сигналы с `SkipReason=NEWS` | время Парижа в окнах новостей |
| 6 | Файл открыт в Excel во время работы | строки не теряются: дописываются при следующей записи |

Файлы и колонки — ТЗ, раздел 6. Правила отбора — `D:\TradeLog\rules.csv` (ТЗ, раздел 7); после правки перезапустить стратегию.
