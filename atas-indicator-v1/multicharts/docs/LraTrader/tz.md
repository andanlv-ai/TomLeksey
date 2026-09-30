# ТЗ: LraTrader v0.1 — виртуальный советник по паттернам автора

Составлено 2026-09-30. Первоисточник правил — `docs/DivergenceDetector/PATTERNS.md`
(видео и скриншоты автора). Ссылки вида `P§3.A` ведут на его разделы.
DDAutoTrader и DivergenceDetector — не образец логики. Из них берём только проверенную
«обвязку»: чтение Volume Profile, время Парижа, запись CSV без дублей.

## §1. Назначение
Стратегия MultiCharts .NET на часовом графике валютных фьючерсов CME (6E, 6B, 6J, 6A, 6C, 6N, 6S).
Реальных ордеров нет. Советник находит ситуации из P§3, открывает виртуальные сделки и пишет в CSV
каждый сигнал (и пропущенный тоже), каждую сделку и каждый диапазон ЛРА. По этим файлам
потом подбираются пороги. Все числа по умолчанию — стартовые, это не правила автора.

## §2. Данные бара (на закрытии часового бара)
- Цены: Open, High, Low, Close.
- Объём Ask и Bid по каждой цене бара: `VolumeProfile.ItemForBar(...)`, `ILevel.AskTradedValue` /
  `BidTradedValue` (как `GetDelta` в `DDAutoTrader.Strategy.CS`). Нет профиля — `NoData=true`.
- Число сделок вверх/вниз: `Bars.UpTicks[0]`, `Bars.DownTicks[0]` — дельта по количеству сделок (P§2).
- Время Парижа и сессия: ASIA 00:00–08:00, EUROPE 08:00–14:30, AMERICA 14:30–22:00, иначе OFF.
- Торговый день — календарная дата по Парижу.

## §3. Дисбаланс окна (P§2)
Окно — последние `ImbBars` баров (по умолчанию 2: «последние два часа», В2 00:00).
- `VolDelta = Σ(Ask−Bid)`, `TickDelta = Σ(UpTicks−DownTicks)`.
- Доли от дня: `VolShare = VolDelta / объём дня до текущего бара включительно`,
  `TickShare = TickDelta / (UpTicks+DownTicks дня)`. Знаменатель 0 — доля 0.
- Уровни: цены окна суммируются по всем барам окна; уровень «за покупку», если
  `(Ask−Bid)/(Ask+Bid) ≥ LevelImbPct`, «за продажу» — если `≤ −LevelImbPct`.
- **Направление толпы** `Dir`:
  - `+1`, если `TickShare ≥ MinShare` и уровней за покупку ≥ `MinLevels`;
  - `−1` — зеркально;
  - иначе `0`.
- Причина при `Dir=0`:
  - `WEAK` — доля мала;
  - `ONE_PRICE` — уровней мало;
  - `CONFLICT` — `VolShare` имеет обратный знак и `|VolShare| ≥ MinShare`. Эта проверка идёт раньше остальных: если две дельты противоречат, сделки нет.
- Любой бар окна с `NoData` → `Dir=0`, причина `NO_DATA`.

## §4. Диапазоны ЛРА (P§1)
- **Кандидат.** Идём от текущего бара назад. Кандидат — наибольшее окно подряд идущих баров, где
  `High−Low ≤ MaxRangePips` и число баров ≥ `MinRangeBars`. Среди них не меньше
  `MinActiveBars` баров сессий EUROPE/AMERICA: ночную проторговку не берём.
- **Рождение.** Диапазон рождается, когда закрытие текущего бара вышло за границу кандидата,
  собранного из предыдущих баров. Запоминаем:
  - `High`, `Low` — это уровни стопов, P§1;
  - `Poc` — цену с наибольшим объёмом по уровням всех баров диапазона;
  - `Volume`, `ExitDir` (+1 — вышли вверх).
  Новый диапазон, который пересекается с активным, не создаётся.
- **Отработан** (`Done`) — цена после выхода коснулась `Poc`: High ≥ Poc ≥ Low бара.
- **Устарел** — прошло больше `MaxRangeAgeBars` баров. Устаревший и отработанный диапазоны
  не активны.
- **Ближайший активный ЛРА** — активный диапазон с минимальным `|Close − Poc|`.

## §5. Уровни
- **Свинг.** Бар `i` — верхний свинг, если его High строго выше High соседей `i−1` и `i+1`,
  и бар `i+1` уже закрыт. Нижний — зеркально.
- **Ближайший уровень** по стороне — ближайший по времени свинг за ценой. Ищем назад
  не дальше `SwingLookback` баров. Отступ — `OffsetTicks` тиков за уровень. Нет уровня — NaN.
- **ATR часа** — `this.AverageTrueRange(AtrPeriod)` в стратегии, в ядро передаётся числом.
- **ATR дня** — средний размах `max(High)−min(Low)` торговых дней за последние `AtrPeriod`
  полных дней. Меньше 3 полных дней — NaN.

## §6. Сценарии входа
Проверяются на закрытии бара, в таком порядке. Одна книга — одна позиция.

**A — «против толпы» (книга `AGAINST`, P§3.A).**
- Условия: толпа `Dir≠0` (§3) и цена «стоит или идёт против»:
  `(Close[0] − Close[ImbBars]) × Dir ≤ StallAtr × ATR`, но движение не сильное:
  `|Close[0] − Close[ImbBars]| < StrongAtr × ATR` (сильное — это уже D; A и D не срабатывают вместе).
- Вход против толпы, `dir = −Dir`, по Close.
- `SL` — ближайший уровень против сделки + отступ.
- `TP` — ближайший уровень по ходу сделки + отступ. Если `TP` дальше `MaxTakePips`,
  ставим `TP` на расстоянии `MaxTakePips`.
- Пропуск `NO_LEVEL`: нет SL или TP, либо SL дальше `MaxStopPips`.

**D — «по сильному движению» (книга `MOMENTUM`, P§3.D).**
- Условия: `|Close[0] − Close[ImbBars]| ≥ StrongAtr × ATR`, а толпа `Dir` противоположна
  знаку движения.
- Вход по движению, `dir = −Dir`. SL и TP — как в A.

**Фильтр ЛРА для A и D (P§1 «направление дня», P§3.E).**
- Есть ближайший активный ЛРА, а сделка идёт от его `Poc` → пропуск `AGAINST_LRA`.
- Сделка идёт к `Poc`, расстояние ≥ `LraMinDistPips` и ≤ `DayTargetShare × ATR дня` →
  вторая часть сделки с `TP2 = Poc`.

**Общие пропуски**, проверяются до сценариев:
- `WEEKEND` — суббота или воскресенье по Парижу;
- `NEWS` — окна `NewsWindows` по Парижу;
- `IN_POSITION` — книга уже в позиции.

Пропущенный сигнал всё равно пишется (§8).

## §7. Сопровождение позиции
Проверяется на закрытии каждого бара, по порядку:
1. **Выход по уровню.** Задет SL или TP части сделки (High/Low бара), выход ровно по уровню.
   Задеты оба → считаем SL и ставим `Ambiguous=1`. SL общий для всех частей.
2. **C — выход, когда дисбаланс исчез (P§3.C), книга AGAINST.**
   - Пересчитываем §3 на окне от первого бара сигнального окна до текущего.
   - Если `Dir` стал 0 или сменил знак, а `|Close − Entry| < StallAtr × ATR`, закрываем все части
     по Close с причиной `DISSOLVED`.
3. **B — добор (P§3.B), только книга AGAINST, одна часть.**
   - Условия: цена ушла против на ≥ `AddStepPips` от входа, SL не задет, толпа §3 на окне
     «после входа» не сменила знак.
   - Тогда вторая часть по Close. TP второй части задаёт `AddTargetMode`:
     - `SAME_TP` — обе части с исходным TP (скрин №9);
     - `ENTRY1` — обе части с TP на цене первого входа (В1 01:16).
   - Толпа на окне «после входа» перешла на нашу сторону → закрыть всё по Close, причина
     `ADVERSE_FLOW`.

Встречного переворота нет: пока книга в позиции, новый сигнал в ней только пишется.

## §8. Файлы — папка `LogDir` (по умолчанию `D:\TradeLog\LRA`)
Запись без дублей, как `Write` в `DDAutoTrader.Strategy.CS`. Время — время графика и время Парижа.

- `signals_<SYM>.csv`: `Time,ParisTime,Symbol,SignalId,Book,Dir,Entry,SL,TP,TP2,SL_pips,TP_pips,
  VolDelta,TickDelta,VolShare,TickShare,BuyLevels,SellLevels,CrowdReason,Atr,DayAtr,RangeId,
  RangeDist_pips,Session,Taken,SkipReason,Source,Version`
- `trades.csv`: `CloseTime,ParisTime,Symbol,SignalId,Book,Unit,Dir,OpenTime,OpenPrice,SL,TP,
  ClosePrice,Exit,Pips,Usd,Ambiguous,Source,Version`, где `Exit` ∈ TP/SL/DISSOLVED/ADVERSE_FLOW.
- `ranges_<SYM>.csv`: `Time,ParisTime,Symbol,RangeId,Event(NEW/DONE/EXPIRED),Start,End,High,Low,Poc,
  Volume,Bars,ExitDir`
- `runs.csv`: START/STOP со всеми параметрами.

## §9. Параметры (Input), стартовые значения
`ImbBars=2, MinShare=0.05, LevelImbPct=0.3, MinLevels=3, MaxRangePips=50, MinRangeBars=4,
MinActiveBars=2, MaxRangeAgeBars=120, SwingLookback=48, OffsetTicks=1, AtrPeriod=14,
StallAtr=0.5, StrongAtr=1.5, MaxStopPips=30, MaxTakePips=15, AddStepPips=10,
AddTargetMode="SAME_TP", LraMinDistPips=30, DayTargetShare=0.5, PipSize=0.0001,
CommissionUsd=0, LogDir="D:\TradeLog\LRA", NewsWindows="14:30-15:30;19:00-21:00"`.

## §10. Приёмка v0.1
1. `tools/build.sh` — 0 ошибок; `tools/test.sh` — ALL PASSED.
2. `tools/bundle.sh` собирает `LraTrader.Strategy.CS`, `build.sh` на нём даёт 0 ошибок.
3. В MultiCharts на 6E H1 (после восстановления CQG) появились все файлы §8,
   повторный запуск не задваивает строки. Проверяет владелец.

## Не входит в v0.1
Скальпинг (P§4), индикатор для графика, разрешение «SL и TP в одном баре» по минуткам,
реальные ордера, автоматический подбор порогов.
