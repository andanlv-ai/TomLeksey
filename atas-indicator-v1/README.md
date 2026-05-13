# TradesAlert — MultiCharts .NET Indicator

**Version 0.1 Beta**

Indicator for **MultiCharts .NET 12 Special Edition** that visualizes buy/sell aggressor imbalance and sends email alerts when the threshold is exceeded.

## What it does

- Reads VolumeProfile data (Ask/Bid traded values) from the chart
- Calculates Delta% = (Ask − Bid) / (Ask + Bid) × 100
- Displays a **green bar** (Buy dominance) or **red bar** (Sell dominance) in a sub-panel
- Bar height = Delta% so the Y-axis shows percentages directly
- Sends an **email alert** when Delta% exceeds the configured threshold (with hysteresis)

## Requirements

- MultiCharts .NET 12 Special Edition
- Volume Profile enabled on the chart with "Up vs Down Tick Delta" mode
- .NET Framework 4.8

## Installation

1. Copy `multicharts/src/TradesAlert.Indicator.CS` to:  
   `C:\ProgramData\TS Support\MultiCharts .NET64 Special Edition\StudyServer\Techniques\CS\`
2. Open **PowerLanguage .NET Editor** (Tools → PowerLanguage .NET Editor)
3. Open the file → press **F5** to compile
4. Apply the indicator to a chart via Insert → Study

## Parameters

| Parameter | Default | Description |
|---|---|---|
| `AlertThresholdPct` | 2.0 | Alert fires when Delta% exceeds this value |
| `HysteresisPct` | 1.5 | Alert resets only after Delta% drops below (Threshold − Hysteresis) |
| `AlertFromUtcHour` | 7 | Start of alert window (UTC hour) |
| `AlertToUtcHour` | 21 | End of alert window (UTC hour) |
| `BarWidth` | 6 | Histogram bar thickness in pixels |
| `EmailEnabled` | false | Enable/disable email sending |
| `AlertToEmail` | andan@mateos.lv | Recipient email address |
| `SmtpHost` | 192.168.1.211 | SMTP server host |
| `SmtpPort` | 25 | SMTP server port |
| `SmtpUseSsl` | false | Use SSL for SMTP |
| `SmtpUser` | (empty) | SMTP username |
| `SmtpPassword` | (empty) | SMTP password |

## Notes

- VolumeProfile API returns aggregated session data — all bars show the same value unless the VP on the chart is configured for per-bar profiles
- Alert log is always written to the MC Output Window (View → Output Window) regardless of `EmailEnabled`
- Alert fires with hysteresis: after triggering, it resets only when Delta% drops below `AlertThresholdPct − HysteresisPct`, then can fire again
