#!/usr/bin/env bash
# Шаги 2–3 tz-ticks.md по тикам D:\TradeLog\Bars\<символ>_TRADES.csv. Результат — D:\TradeLog\Ticks.
# Запуск: bash src/lra/ticks/run.sh (из atas-indicator-v1/multicharts)
source "$(dirname "$0")/../tools/common.sh"
files=(); for f in "$LRA"/Core*.cs "$LRA"/ticks/*.cs; do files+=("$(win "$f")"); done
exe="$TEMP\lra_ticks.exe"
csc -t:exe -out:"$exe" "${files[@]}" || exit 1
"$(cygpath -u "$exe")" "$@"
