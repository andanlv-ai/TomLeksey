# DivergenceDetector — Деплой и проверка

Версия индикатора: `DivergenceDetector.Indicator.MC.pla` (v2.0, PowerLanguage)

## Деплой

**1. Открыть PowerLanguage Editor**
В MultiCharts: меню **View → PowerLanguage Editor** (или F3 на чарте).

**2. Импортировать файл**
**File → Import Study...**
```
C:\Users\user\TomLeksey\atas-indicator-v1\multicharts\src\DivergenceDetector.Indicator.MC.pla
```

**3. Собрать**
**Build → Build Study** (F3). Ожидаемый результат: `0 errors, 0 warnings`.

---

## Проверка на чарте

**4. Подвесить индикатор**
Чарт CA6M26 30sec → правая кнопка → **Insert Study → Indicators → DivergenceDetector**.
Параметры: `NBars = 2`, `LogEveryBar = true`.

**5. Открыть Output**
**View → Output Bar** (Ctrl+Alt+O).

---

## Чек-лист

| # | Что проверить | Ожидаемый результат |
|---|---------------|---------------------|
| 1 | `upT` и `dnT` в строках Output | Ненулевые числа (не `upT=0 dnT=0`) |
| 2 | Строки с `sig=1` | Хотя бы несколько на 700+ барах истории |
| 3 | Цвет на чарте | `sig=1` + `delta=-1` → красный, `delta=1` → зелёный, `sig=0` → серый |
| 4 | `NBars=1` → сигналов больше, `NBars=5` → меньше | Проверка что NBars влияет |
| 5 | Output не пуст | Хотя бы одна строка (последний бар) даже при `LogEveryBar=false` |

### Пример нормального вывода в Output
```
BAR# 5  price= 1  delta=-1  sig= 1  upT= 142  dnT= 87
BAR# 6  price= 0  delta=-1  sig= 1  upT= 55   dnT= 120
BAR# 7  price=-1  delta= 1  sig= 0  upT= 200  dnT= 44
```

### Признаки проблем
- `upT=0 dnT=0` на всех барах → PowerLanguage не даёт тиковые данные на этом тайм-фрейме
- `sig=0` везде при ненулевых тиках → проблема в логике условий
- Output пуст → индикатор не запустился (проверить Build)
