#!/usr/bin/env bash
# Прогон ядра LraTrader по CSV записывателя без MultiCharts.
# Запуск: bash src/lra/sweep/run.sh [папка_с_CSV [only|cum]]
source "$(dirname "$0")/../tools/common.sh"
files=(); for f in "$LRA"/Core*.cs "$LRA"/sweep/*.cs; do files+=("$(win "$f")"); done
exe="$TEMP\lra_sweep.exe"
csc -t:exe -out:"$exe" "${files[@]}" || exit 1
"$(cygpath -u "$exe")" "$@"
