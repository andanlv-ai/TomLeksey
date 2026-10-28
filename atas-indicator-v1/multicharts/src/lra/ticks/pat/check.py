import sys; sys.path.insert(0, sys.argv[1])
from pat import *
from math import comb, sqrt
def binp(k,n):  # двусторонний биномиальный p при 50%
    if n==0: return 1
    m=max(k,n-k); return min(1,2*sum(comb(n,i) for i in range(m,n+1))/2**n)
def line(rows):
    a=sum(1 for r in rows if r['f']['first']=='AGAINST'); n=sum(1 for r in rows if r['f']['first'])
    x=[r['f'][24] for r in rows if r['f'][24] is not None]
    se=st.stdev(x)/sqrt(len(x)) if len(x)>1 else 0
    pos=sum(1 for v in x if v>0)
    return (f'n={len(rows):2d} | против толпы первым {a}/{n} ({100*a/max(1,n):.0f}%, p={binp(a,n):.2f}) | '
            f'24ч: среднее {st.mean(x) if x else 0:+.1f}±{se:.1f}, медиана {st.median(x) if x else 0:+.1f}, по толпе {pos}/{len(x)} (p={binp(pos,len(x)):.2f})')
cands={
 'набор дольше 35 мин': lambda r: (r['t']-ts(r['GrowthStart']))/3600>0.58,
 'набор до 35 мин':     lambda r: (r['t']-ts(r['GrowthStart']))/3600<=0.58,
 'мелких > 50%':        lambda r: float(r['SmallShare'])>0.5,
 'мелких <= 50%':       lambda r: float(r['SmallShare'])<=0.5,
 'вход от H0 > 6 п.':   lambda r: abs(float(r['H0'])-float(r['Entry']))/PIP>6,
 'вход от H0 <= 6 п.':  lambda r: abs(float(r['H0'])-float(r['Entry']))/PIP<=6,
}
for sym in ['BP6Z26','EU6Z26']:
    T,P,D=load_tape(sym)
    for v in ['F','G']:
        ev,ded=events(sym,v)
        for r in ded: r['f']=path(T,P,r['t'],r['side'],H)
        mid=sorted(r['t'] for r in ded)[len(ded)//2]
        print(f'\n=== {sym} {v}\n  ВСЕ        {line(ded)}')
        for name,f in cands.items():
            g=[r for r in ded if f(r)]
            print(f'  {name}:\n     весь месяц {line(g)}\n     1-я пол.   {line([r for r in g if r["t"]<mid])}\n     2-я пол.   {line([r for r in g if r["t"]>=mid])}')
