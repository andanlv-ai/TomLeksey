using System;
using System.Collections.Generic;
using PowerLanguage.Function;
using PowerLanguage.VolumeProfile;

// LraTrader — виртуальный советник по паттернам автора (ТЗ: docs/LraTrader/tz.md). Склейка ядра с MultiCharts.
namespace PowerLanguage.Strategy
{
    public class LraTrader : SignalObject
    {
        private const string Version = "0.1";

        public LraTrader(object ctx) : base(ctx) { }

        protected override void CalcBar()
        {
            // Сбор бара — тикет 08. Логика — тикеты 09, 10.
        }
    }
}
