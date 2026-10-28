import sys; sys.path.insert(0, sys.argv[1])
from pat import *
from datetime import datetime
for sym in ['BP6Z26','EU6Z26']:
    T,P,D=load_tape(sym)
    for v in ['F','G']:
        ev,ded=events(sym,v)
        for r in ded: r['f']=path(T,P,r['t'],r['side'],H)
        out=[]
        for thr in [0.42,0.5,0.58,0.67,0.75]:
            g=[r for r in ded if (r['t']-ts(r['GrowthStart']))/3600<=thr and r['f'][24] is not None]
            b=[r for r in ded if (r['t']-ts(r['GrowthStart']))/3600>thr and r['f'][24] is not None]
            days=len(set(datetime.fromtimestamp(r['t']).date() for r in g))
            m=lambda x,h: f"{st.mean([r['f'][h] for r in x]):+.1f}" if x else '-'
            out.append(f"порог {thr*60:.0f} мин: короткий n={len(g)} (дней {days}) 4ч {m(g,4)} 8ч {m(g,8)} 24ч {m(g,24)} | длинный n={len(b)} 4ч {m(b,4)} 8ч {m(b,8)} 24ч {m(b,24)}")
        print(f'=== {sym} {v}'); print('\n'.join(out))
