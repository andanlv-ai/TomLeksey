#!/usr/bin/env bash
# Сборка как в PowerLanguage Editor (C# 7.3, библиотеки MultiCharts). 0 = собралось.
# Без аргументов — все lra/*.cs. С аргументами — только эти файлы (например собранный LraTrader.Strategy.CS).
# Запуск: bash atas-indicator-v1/multicharts/src/lra/tools/build.sh [файл ...]
source "$(dirname "$0")/common.sh"
files=()
if [ $# -gt 0 ]; then for f in "$@"; do files+=("$(win "$f")"); done
else for f in "$LRA"/*.cs; do files+=("$(win "$f")"); done; fi
csc -t:library -out:"$TEMP\lra_build.dll" -r:"$MC\PLStudiesProxy.dll" -r:"$MC\PLCommon.dll" -r:"$MC\PLTypes.dll" \
  "$(win "$LRA/tools/mc_stubs.cs")" "${files[@]}"
