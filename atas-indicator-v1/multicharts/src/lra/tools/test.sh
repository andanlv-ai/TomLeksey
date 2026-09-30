#!/usr/bin/env bash
# Тесты ядра: собирает Core*.cs + tests/*.cs в exe без MultiCharts и запускает. 0 = все тесты прошли.
# Запуск: bash atas-indicator-v1/multicharts/src/lra/tools/test.sh
source "$(dirname "$0")/common.sh"
files=(); for f in "$LRA"/Core*.cs "$LRA"/tests/*.cs; do [ -f "$f" ] && files+=("$(win "$f")"); done
exe="$TEMP\lra_tests.exe"
csc -t:exe -out:"$exe" "${files[@]}" || exit 1
"$(cygpath -u "$exe")"
