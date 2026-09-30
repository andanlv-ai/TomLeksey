#!/usr/bin/env bash
# Склеивает lra/*.cs в один файл для MultiCharts: atas-indicator-v1/multicharts/src/LraTrader.Strategy.CS
# using-строки берутся один раз, из всех файлов. Запуск: bash atas-indicator-v1/multicharts/src/lra/tools/bundle.sh
source "$(dirname "$0")/common.sh"
out="$LRA/../LraTrader.Strategy.CS"
{
  echo "// LraTrader.Strategy.CS — СОБРАНО tools/bundle.sh из src/lra/*.cs. Не править руками."
  cat "$LRA"/*.cs | grep -E '^using [A-Za-z.]+;' | sort -u
  for f in "$LRA"/*.cs; do echo; echo "// ---- $(basename "$f") ----"; grep -vE '^using [A-Za-z.]+;' "$f"; done
} | sed 's/\r$//' > "$out"
echo "$out"
