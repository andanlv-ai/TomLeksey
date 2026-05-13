# MultiCharts .NET Indicators — Documentation

Документация к индикаторам для MultiCharts .NET 12 Special Edition.
Исходный код — в [../src/](../src/), документация — в этой папке.

## Структура

```
atas-indicator-v1/multicharts/
  src/      — C#-исходники индикаторов и .csproj для синтакс-чека
  docs/     — документация (этот каталог)
```

## Общие документы

- [DEVELOPMENT.md](DEVELOPMENT.md) — воркфлоу компиляции, загрузки в MC, отладка
- [VolumeProfile_API.md](VolumeProfile_API.md) — справка по `IProfile`/`ILevel` и Ask/Bid TradedValue

## Индикаторы

### TradesAlert ✅ реализован

- [TradesAlert/CONTEXT.md](TradesAlert/CONTEXT.md) — хронология решений, ограничения суб-панели MC, итоговая визуализация
- Исходники:
  - [../src/TradesAlert.Indicator.CS](../src/TradesAlert.Indicator.CS) — v1
  - [../src/TradesAlert_v2.Indicator.CS](../src/TradesAlert_v2.Indicator.CS) — v2 (рабочая версия)
  - [../src/TradesAlert_v2.Indicator.MC.CS](../src/TradesAlert_v2.Indicator.MC.CS) — копия для MC StudyServer
  - [../src/TradesAlert.csproj](../src/TradesAlert.csproj) — для `dotnet build` (синтакс-чек)

### DivergenceDetector 🚧 Фаза 0 (диагностика)

- [DivergenceDetector/DivergenceDetector.PLAN.md](DivergenceDetector/DivergenceDetector.PLAN.md) — план разработки по фазам с правилом gating
- [DivergenceDetector/DIVERGENCE_DETECTOR.md](DivergenceDetector/DIVERGENCE_DETECTOR.md) — описание функциональности и сценариев
- Исходники:
  - [../src/DivergenceProbe.Indicator.MC.CS](../src/DivergenceProbe.Indicator.MC.CS) — диагностический пробник (Фаза 0)

## Соглашения

- `*.Indicator.CS` — версия для `dotnet build` (синтакс-чек, не для деплоя)
- `*.Indicator.MC.CS` — копия которая руками кладётся в `C:\ProgramData\TS Support\MultiCharts .NET64 Special Edition\StudyServer\Techniques\CS\` под именем `*.Indicator.CS`
