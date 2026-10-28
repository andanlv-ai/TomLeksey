import csv
for sym,usd in [('BP6Z26',6.25),('EU6Z26',12.5)]:
  for v in 'FG':
    for m in ['FAST_SL','FAST_TP','SWEEP','SWEEP_ALL']:
      rows=[r for r in csv.DictReader(open(rf'D:\TradeLog\Ticks\events_{sym}_{v}_{m}.csv',encoding='utf-8')) if r['Skip']=='']
      p=[float(r['Pips']) for r in rows]; mid='2026-10-12'
      a=sum(float(r['Pips']) for r in rows if r['Time']<mid); b=sum(float(r['Pips']) for r in rows if r['Time']>=mid)
      na=sum(1 for r in rows if r['Time']<mid)
      print(f"{sym} {v} {m:8s} сделок {len(p):2d} плюс {sum(x>0 for x in p):2d} итог {sum(p):+6.0f} п. ${sum(p)*usd:+7.0f} | до 12.10: {na} сд. {a:+.0f} | после: {len(p)-na} сд. {b:+.0f} | лучшая {max(p):+.0f} худшая {min(p):+.0f}")
