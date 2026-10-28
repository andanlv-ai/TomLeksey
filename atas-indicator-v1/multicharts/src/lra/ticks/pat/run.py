import sys; sys.path.insert(0, sys.argv[1])
from pat import *
from datetime import datetime
def paris_h(r): return int(r['ParisTime'][11:13])
def sess(r):
    h=paris_h(r); return 'Азия 0-8' if h<8 else 'Европа 8-14' if h<14 else 'Америка 14-22' if h<22 else 'ночь 22-24'
feats={
 'сторона толпы': lambda r: r['Crowd'],
 'сессия (Париж)': sess,
 'вес': lambda r: float(r['Weight']),
 'цен в зоне': lambda r: int(r['Prices']),
 'ширина зоны, п.': lambda r: (float(r['ZoneHi'])-float(r['ZoneLo']))/PIP,
 'доля мелких': lambda r: float(r['SmallShare']),
 'длит. роста, ч': lambda r: (r['t']-ts(r['GrowthStart']))/3600,
 'вход от H0, п.': lambda r: abs(float(r['H0'])-float(r['Entry']))/PIP,
 'вход от зоны, п.': lambda r: r['side']*(float(r['Entry'])-(float(r['ZoneHi']) if r['side']>0 else float(r['ZoneLo'])))/PIP,
 'сделок с роста': lambda r: int(r['Trades']),
}
for sym in ['BP6Z26','EU6Z26']:
    T,P,D=load_tape(sym)
    # контроль: каждые 5 мин, «толпа» = знак дельты по количеству сделок за прошлый час
    import itertools
    cum=list(itertools.accumulate(D))
    ctrl=[]; t=T[0]+3600
    while t<T[-1]-24*3600:
        i=bisect.bisect_left(T,t); j=bisect.bisect_left(T,t-3600)
        if i<len(T) and T[i]-t<300 and i-j>200:
            d=cum[i-1]-cum[j-1]; side=1 if d>0 else -1
            ctrl.append({'f':path(T,P,t,side,H)})
        t+=1800
    print(f'\n######## {sym}\nКОНТРОЛЬ (каждые 30 мин, толпа = перекос по числу сделок за час): '+stats(ctrl))
    for v in ['F','G']:
        ev,ded=events(sym,v)
        for r in ded: r['f']=path(T,P,r['t'],r['side'],H)
        print(f'\n=== вариант {v}: событий {len(ev)}, после удаления дублей {len(ded)}')
        print('ВСЕ: '+stats(ded))
        mid=sorted(r['t'] for r in ded)[len(ded)//2]
        print('  1-я половина месяца: '+stats([r for r in ded if r['t']<mid]))
        print('  2-я половина месяца: '+stats([r for r in ded if r['t']>=mid]))
        for name,f in feats.items():
            vals=[f(r) for r in ded]
            if isinstance(vals[0],str):
                groups={k:[r for r in ded if f(r)==k] for k in sorted(set(vals))}
            else:
                m=st.median(vals); groups={f'<= {m:.2f}':[r for r in ded if f(r)<=m], f'>  {m:.2f}':[r for r in ded if f(r)>m]}
            print(f'  {name}:')
            for k,g in groups.items(): print(f'     {k:14s} '+stats(g))
