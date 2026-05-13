---
name: MultiCharts VolumeProfile API
description: Как читать VolumeProfile данные (Buy/Sell по ценовым уровням) из индикатора MC .NET через отражённый API
type: reference
originSessionId: b19a3c8d-3b43-4eba-9372-c9363fa6777a
---
## Namespace

```csharp
using PowerLanguage.VolumeProfile;
```

## Доступ к профилю

```csharp
IProfilesCollection vp = this.VolumeProfile;
IProfile profile = vp.ItemForBar(Bars.CurrentBar); // профиль текущего бара
// или
ICollection<IProfile> all = vp.Items; // все профили (обычно 1)
```

## IProfile свойства

| Свойство | Тип | Описание |
|---|---|---|
| `Values` | `ICollection<ILevel>` | Все ценовые уровни |
| `POC` | `ILevel` | Point of Control |
| `MinDelta` | `ILevel` | Уровень с минимальной дельтой |
| `MaxDelta` | `ILevel` | Уровень с максимальной дельтой |
| `Open` | `Price` | Цена открытия периода |
| `Close` | `Price` | Цена закрытия периода |
| `TotalValue` | `decimal` | Суммарный объём |
| `Empty` | `bool` | Профиль пуст |
| `BarsInterval` | `Pair<int>` | Диапазон баров |

## ILevel свойства

| Свойство | Тип | Описание |
|---|---|---|
| `Price` | `Price` | Ценовой уровень |
| `AskTradedValue` | `decimal` | Buy-агрессоры (сделки по Ask) |
| `BidTradedValue` | `decimal` | Sell-агрессоры (сделки по Bid) |
| `TotalValue` | `decimal` | Общий объём на уровне |

## Пример: суммарная дельта за бар

```csharp
IProfile profile = this.VolumeProfile.ItemForBar(Bars.CurrentBar);
if (profile == null || profile.Empty) return;

decimal totalAsk = 0m, totalBid = 0m;
foreach (ILevel level in profile.Values)
{
    totalAsk += level.AskTradedValue;  // Buy
    totalBid += level.BidTradedValue;  // Sell
}
decimal delta = totalAsk - totalBid;
double pct = Math.Abs((double)delta) / (double)(totalAsk + totalBid) * 100.0;
```

## Пример: лог по уровням (как на графике MC)

```csharp
foreach (ILevel level in profile.Values)
{
    decimal delta = level.AskTradedValue - level.BidTradedValue;
    Output.WriteLine("Price={0} Ask={1} Bid={2} Delta={3}{4}",
        level.Price,
        level.AskTradedValue,
        level.BidTradedValue,
        delta >= 0m ? "+" : "", delta);
}
```

## Реальные типы (runtime)

- `VolumeProfile` → `ChartingPlugin.Snapshot.VolumeProfilesSnapshots`
- `IProfile` → `ChartingPlugin.Snapshot.Profile`
- `ILevel` → `ChartingPlugin.Snapshot.Level`
- DLL: `PLTypes.dll` (интерфейсы), `VolumeProfile.dll` (имплементация)

## Важно

- `VolumeProfile` возвращает данные **уже посчитанные MC** — не нужно пересчитывать тики
- `AskTradedValue` = Buy (агрессоры покупки), `BidTradedValue` = Sell (агрессоры продажи)
- Работает только если на графике включён Volume Profile с настройкой "Up vs Down Tick Delta"
- `Items.Count` зависит от настроек VP на графике (сессия, бар, диапазон)
- Reflection-зонд: `c:\Users\user\TomLeksey\atas-indicator-v1\multicharts\tmp_probe\Probe.Indicator.CS`
