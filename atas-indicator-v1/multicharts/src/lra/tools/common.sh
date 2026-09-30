# Общие пути для build.sh / test.sh / bundle.sh
LRA="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CSC="C:\Program Files\dotnet\sdk\10.0.201\Roslyn\bincore\csc.dll"
FW='C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
MC='C:\Program Files\TS Support\MultiCharts .NET64 Special Edition'
FWREFS=(-r:"$FW\mscorlib.dll" -r:"$FW\System.dll" -r:"$FW\System.Core.dll" -r:"$FW\System.Drawing.dll")
csc() { dotnet "$CSC" -nologo -noconfig -nostdlib -utf8output -langversion:7.3 "${FWREFS[@]}" "$@"; }
win() { cygpath -w "$1"; }
