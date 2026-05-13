# MultiCharts .NET — Developer Notes

## Структура файлов

| Путь | Назначение |
|---|---|
| `C:\Program Files\TS Support\MultiCharts .NET64 Special Edition\` | Бинарники MC, все DLL для компиляции |
| `C:\ProgramData\TS Support\MultiCharts .NET64 Special Edition\StudyServer\Techniques\CS\` | Исходники индикаторов (`.Indicator.CS`, `.Strategy.CS`) |
| `C:\ProgramData\TS Support\MultiCharts .NET64 Special Edition\StudyServer\Techniques\CompAssms\` | Скомпилированные DLL (имена — случайные хэши, MC управляет сам) |
| `C:\ProgramData\TS Support\MultiCharts .NET64 Special Edition\StudyServer\Techniques\tech_storage.bin` | Бинарный реестр всех техник (MC читает отсюда) |

## Какие DLL нужны для компиляции

Три .NET сборки из папки MC:

```
PLStudiesProxy.dll  — IndicatorObject, базовые типы индикатора
PLCommon.dll        — Input, UpdateOnEveryTick атрибуты
PLTypes.dll         — EResolution, EBarState и прочие enums
```

Все остальные DLL в папке MC — либо нативные (не .NET), либо не нужны для базового индикатора.

## Как проверить что DLL является .NET сборкой

```powershell
$bytes = [IO.File]::ReadAllBytes("path\to.dll")
$text  = [Text.Encoding]::ASCII.GetString($bytes)
$text.Contains('BSJB')  # True = .NET managed assembly
```

## Шаблон .csproj для компиляции из командной строки

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <TargetFramework>net48</TargetFramework>
    <Nullable>disable</Nullable>
    <LangVersion>7.3</LangVersion>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="PLStudiesProxy">
      <HintPath>C:\Program Files\TS Support\MultiCharts .NET64 Special Edition\PLStudiesProxy.dll</HintPath>
    </Reference>
    <Reference Include="PLCommon">
      <HintPath>C:\Program Files\TS Support\MultiCharts .NET64 Special Edition\PLCommon.dll</HintPath>
    </Reference>
    <Reference Include="PLTypes">
      <HintPath>C:\Program Files\TS Support\MultiCharts .NET64 Special Edition\PLTypes.dll</HintPath>
    </Reference>
  </ItemGroup>
  <Compile Include="MyIndicator.Indicator.CS" />
</Project>
```

Компиляция:
```bash
dotnet build MyIndicator.csproj
```

## Как загрузить индикатор в MC (единственный способ)

MC не читает внешние DLL напрямую. Компиляция только через **PLEditor.NET.exe**.

1. Открыть: `C:\Program Files\TS Support\MultiCharts .NET64 Special Edition\PLEditor.NET.exe`
   (или через меню MC: **Tools → PowerLanguage .NET Editor**)
2. **New → Indicator** → ввести имя (например `TradesAlert`)
3. Вставить код из `.Indicator.CS` файла
4. Нажать **F5** (Build)
5. Индикатор появится в MC в списке индикаторов

> PLEditor не поддерживает CLI — только GUI. Нет способа импортировать обход.

## Workflow разработки

```
Редактировать .Indicator.CS  →  dotnet build (проверка ошибок)
                                       ↓
                              Вставить в PLEditor  →  F5 (финальная сборка в MC)
```

`dotnet build` используется **только для проверки синтаксиса и типов** — компилирует в `bin/` но MC эту DLL не использует. Финальная сборка всегда через PLEditor.

## API шпаргалка

### Структура индикатора

```csharp
[UpdateOnEveryTick(true)]          // CalcBar вызывается на каждый тик
public class MyIndicator : IndicatorObject {
    public MyIndicator(object ctx) : base(ctx) {
        MyParam = 2.0;             // дефолты параметров — в конструкторе
    }

    [Input] public double MyParam { get; set; }

    protected override void Create()    { /* создать объекты */ }
    protected override void StartCalc() { /* инициализация при каждом пересчёте */ }
    protected override void CalcBar()   { /* логика на каждый бар/тик */ }
    protected override void Destroy()   { /* очистка */ }
}
```

### Важные свойства Bars

```csharp
Environment.IsRealTimeCalc    // true только в реалтайме
Bars.Close[0]                 // цена текущего тика
Bars.Time[0]                  // время текущего бара
Bars.UpTicks[0]               // кол-во up-тиков в баре (Buy с CQG)
Bars.DownTicks[0]             // кол-во down-тиков в баре (Sell с CQG)
Bars.Volume[0]                // объём текущего бара
Bars.StatusLine.Bid           // текущий Bid (только реалтайм)
Bars.StatusLine.Ask           // текущий Ask (только реалтайм)
Bars.Info.Name                // название символа
Bars.Info.Resolution.Type     // EResolution (тип бара)
```

### Buy/Sell с CQG фидом

С CQG `Bars.UpTicks[0]` / `Bars.DownTicks[0]` = реальные Buy/Sell агрессоры **если** включено:

> **QuoteManager → правая кнопка на символ → Edit → Data → "Build Volume On" → "Ask ticks & Bid ticks"**

Без этой настройки — tick rule (цена выше/ниже предыдущей), приближение.

### Детектировать новый тик в реалтайме

CalcBar с `[UpdateOnEveryTick(true)]` вызывается на каждый тик. Чтобы получить дельту тика:

```csharp
private int _lastUp, _lastDn;
private DateTime _lastBarTime;

protected override void CalcBar() {
    if (!Environment.IsRealTimeCalc) return;

    int up = (int)Bars.UpTicks[0];
    int dn = (int)Bars.DownTicks[0];
    int dUp, dDn;

    if (Bars.Time[0] != _lastBarTime) {
        dUp = up; dDn = dn;          // начало нового бара
        _lastBarTime = Bars.Time[0];
    } else {
        dUp = up - _lastUp;
        dDn = dn - _lastDn;
    }
    _lastUp = up; _lastDn = dn;
    // dUp/dDn — Buy/Sell этого конкретного тика
}
```

### Примеры встроенных индикаторов для справки

```
C:\ProgramData\TS Support\...\Techniques\CS\Bid_And_Ask.Indicator.CS      — Bid/Ask реалтайм
C:\ProgramData\TS Support\...\Techniques\CS\Volume_Up.Indicator.CS        — UpTicks по барам
C:\ProgramData\TS Support\...\Techniques\CS\Day_OpenHiLo_Lines.Indicator.CS — TrendLine рисование
```

## Файлы этого проекта

| Файл | Описание |
|---|---|
| `TradesAlert.Indicator.CS` | Исходник: алерт по % дельты, скользящее окно 1ч, SMTP |
| `TradesAlert.csproj` | Проект для `dotnet build` (проверка синтаксиса) |
| `DEVELOPMENT.md` | Этот файл |
