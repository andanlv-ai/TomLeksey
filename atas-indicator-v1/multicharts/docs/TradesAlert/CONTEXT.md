# TradesAlert — контекст задачи и хронология решений

## Общая задача

Написать индикатор для **MultiCharts .NET 12 Special Edition**, который:
1. Читает внутренние данные **VolumeProfile** MC (Ask/Bid traded values по уровням цены)
2. Суммирует дельту агрессоров (Buy − Sell) в процентах от общего объёма
3. **Визуализирует** эту дельту на графике
4. **Шлёт алерт по email** при превышении порога 2% (с гистерезисом 1.5% и окном 07–21 UTC, SMTP 192.168.1.211:25)

Рабочие файлы:
- `TradesAlert.Indicator.CS` — исходник
- `TradesAlert.csproj` — для `dotnet build` (только синтакс-чек)
- `DEVELOPMENT.md` — шпаргалка по воркфлоу MC .NET
- `tmp_probe/Probe.Indicator.CS` — reflection-зонд для VolumeProfile API

## Что работает стабильно

| Компонент | Статус | Примечание |
|---|---|---|
| Чтение VolumeProfile API | ✅ | `IProfile.Values` → `ILevel.AskTradedValue` / `BidTradedValue` |
| Суб-панель под графиком | ✅ | `[SameAsSymbol(false)]` — официальный механизм |
| Гистограмма / линии через `AddPlot` | ✅ | стандартный способ рендера в суб-панели |
| Обновление на каждом тике | ✅ | `[UpdateOnEveryTick(true)]` + `Environment.IsRealTimeCalc` |
| SMTP отправка | ✅ | `System.Net.Mail.SmtpClient`, async через `Task.Run` |
| Гистерезис алерта | ✅ | алерт срабатывает повторно только после отката ниже threshold−hysteresis |
| Один алерт на бар (v3.4) | ✅ | флаг `_alertFiredThisBar` сбрасывается при смене бара, взводится при первом тике где `_barTickCount >= LogEveryNTicks` |
| Временнóе окно UTC | ✅ | 07–21 UTC по умолчанию |

## Ядро проблемы: визуализация

Пользователь запросил **горизонтальный progress-bar** в суб-панели:
- Серый фон = общий объём (full-width)
- Цветная полоса (green/red) слева направо, длина = |delta|/total
- Текстовые подписи на/под полосой: total, delta, %

В MC .NET это **архитектурно невозможно** — ниже разобрано почему.

## Хронология гипотез и проверок

### H1 — суб-панель не появлялась (решено раньше)
**Симптом:** индикатор компилился, но рендерился поверх свечей, не в отдельной панели.

**Гипотезы и проверки:**
- Запускали 3 агента параллельно (MC builtins / MC XML docs / web search)
- Все три независимо нашли: нужен атрибут `[SameAsSymbol(false)]`
- Источники: `PLTypes.xml` → `SameAsSymbolAttribute`; tradingcode.net; forum.multicharts.com
- **Результат:** ✅ добавили атрибут, суб-панель появилась, подтверждено скриншотом.

### H2 — `DrwRectangle.Create(start, end, true)` рисует в суб-панели
**Основание:** два агента (reflection по DLL + web search) утверждали что 3-й параметр `onSameSubchart: true` направляет рисование в суб-панель индикатора.

**Проверка:** написали код с `DrwRectangle.Create(..., true)` + `FillColor` + `Pattern=Solid`.

**Результат:** ❌ **Не рендерится.** `dotnet build` чистый, Y-шкала 0..100 отображается (анкерные плоты работают), но **никаких прямоугольников нет**. Подтверждено скриншотом.

### H3 — `DrwText.Create(..., true)` рисует текст в суб-панели
**Симптом:** тот же — компилится, не рендерится. Та же судьба что у DrwRectangle.

### H4 — `IChartCustomDrawer` (кастомное GDI+ рисование) работает в суб-панели
**Гипотеза:** агент нашёл интерфейс `IChartCustomDrawer` с `Draw(DrawContext, EDrawPhases)` — даёт доступ к `context.graphics` (GDI+ `System.Drawing.Graphics`). Теоретически мог бы рисовать что угодно.

**Проверки:**
1. Grep по всем built-in индикаторам MC (`C:\ProgramData\TS Support\...\Techniques\CS\`):
   - Единственный найденный с `IChartCustomDrawer` — `Bollinger_Bands_Area.Indicator.CS`
   - У него `[SameAsSymbol(true)]` — **main chart**, не суб-панель
   - Ни одного примера `IChartCustomDrawer` + `SameAsSymbol(false)` в MC не существует
2. Форум MC (агент C):
   - Тред [multicharts.com/discussion/viewtopic.php?t=10720](https://www.multicharts.com/discussion/viewtopic.php?t=10720): *"Users have inquired about creating custom drawings (e.g., a heat map) in a subchart using IChartCustomDrawer... However, **it is not possible to execute this at the moment**"*
   - Тред [t=46547](https://www.multicharts.com/discussion/viewtopic.php?t=46547): подтверждение от разработчиков
   - Тред [t=51875](https://www.multicharts.com/discussion/viewtopic.php?t=51875): жалобы других разработчиков на этот же баг

**Результат:** ❌ **Подтверждённая архитектурная ограниченность MC**. Custom drawings и `IChartCustomDrawer` работают ТОЛЬКО на main chart.

### Что было опровергнуто / где агенты галлюцинировали

- Агент утверждал что `_Market_Depth_on_Chart_2_.Indicator.CS` и `_TPO_.Indicator.CS` используют `IChartCustomDrawer` → **не подтвердилось grep'ом**, эти файлы либо не существуют, либо не содержат такого кода
- Агент утверждал "DrwRectangle в TradesAlert.Indicator.CS работает нормально" — **это ложь**, пользователь подтвердил скриншотом что не рендерится
- Reflection-агент заявлял что `DrwTrendLine.Create` имеет 3-параметрическую перегрузку с `onSameSubchart` — **технически перегрузка существует (компиляция проходит), но в runtime не рендерит в суб-панели**

## Финальный вывод

**Для суб-панели в MC .NET доступны только `AddPlot` (линии, гистограммы, точки).**

Всё что через `Drw*` (TrendLine, Text, Rectangle, Arrow) и `IChartCustomDrawer` — **либо не рендерится, либо рендерится на main chart поверх свечей**, игнорируя `[SameAsSymbol(false)]`.

## Итоговая визуализация (реализована)

- **Гистограмма** signed delta% в суб-панели:
  - выше нуля → зелёная (BUY агрессия)
  - ниже нуля → красная (SELL)
- **Горизонтальные линии ±`AlertThresholdPct`** — синие (DodgerBlue)
- **Линия 0** — серая
- Точные числа → в status-line MC при наведении + лог раз в 10 тиков

## Альтернативы (если пользователь согласится нарушить "суб-панель" требование)

| Вариант | Плюсы | Минусы |
|---|---|---|
| `[SameAsSymbol(true)]` + `DrwRectangle` + `DrwText` | горизонтальные полосы + текст работают | перекрывает свечи цены |
| `[SameAsSymbol(true)]` + drawings в углу (фикс-координаты) | менее навязчиво | ChartPoint привязан к time/price, "фикс" требует постоянного апдейта при прокрутке |

## Ссылки

- [MultiCharts VolumeProfile API (memory)](../../../../../.claude/projects/c--Users-user-TomLeksey/memory/multicharts_volumeprofile_api.md)
- [DEVELOPMENT.md](../DEVELOPMENT.md) — воркфлоу компиляции и загрузки
- Форум MC: [t=10720](https://www.multicharts.com/discussion/viewtopic.php?t=10720), [t=46547](https://www.multicharts.com/discussion/viewtopic.php?t=46547), [t=51875](https://www.multicharts.com/discussion/viewtopic.php?t=51875)
- Docs: [tradingcode.net/multicharts-net](https://www.tradingcode.net/multicharts-net/)
