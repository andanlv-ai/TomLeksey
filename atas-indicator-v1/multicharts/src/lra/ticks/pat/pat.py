import csv, bisect, sys, statistics as st
from datetime import datetime, timedelta
PIP=0.0001
def load_tape(sym):
    T=[];P=[];D=[]
    with open(rf'D:\TradeLog\Bars\{sym}_TRADES.csv', newline='') as f:
        r=csv.reader(f); next(r)
        for row in r:
            if len(row)<4: continue
            T.append(datetime.strptime(row[0],'%Y-%m-%d %H:%M:%S.%f').timestamp()); P.append(float(row[1])); D.append(int(row[3]))
    return T,P,D
def price_at(T,P,t):
    i=bisect.bisect_right(T,t)-1
    return P[i] if i>=0 else None
def path(T,P,t,side,hours,lvl=10):
    """доходность в сторону толпы через h часов; кто первым: +lvl (по толпе) или -lvl (против)"""
    i=bisect.bisect_left(T,t); 
    if i>=len(T): return None
    e=P[i]; out={}
    for h in hours:
        p=price_at(T,P,t+h*3600); out[h]=side*(p-e)/PIP if T[-1]>=t+h*3600 else None
    first=None; j=i; end=t+24*3600
    while j<len(T) and T[j]<=end:
        m=side*(P[j]-e)/PIP
        if m>=lvl: first='WITH'; break
        if m<=-lvl: first='AGAINST'; break
        j+=1
    out['first']=first; return out
def ts(s): return datetime.strptime(s,'%Y-%m-%d %H:%M').timestamp()
H=[1,4,8,24]
def events(sym,v):
    ev=[]
    for r in csv.DictReader(open(rf'D:\TradeLog\Ticks\events_{sym}_{v}_H0.csv')):
        r['t']=ts(r['Time']); r['side']=1 if r['Crowd']=='BUY' else -1; ev.append(r)
    # одно движение = одно событие: та же сторона в пределах 2 ч от предыдущего — дубль
    ded=[]; last={}
    for r in sorted(ev,key=lambda x:x['t']):
        if r['side'] in last and r['t']-last[r['side']]<2*3600: last[r['side']]=r['t']; continue
        last[r['side']]=r['t']; ded.append(r)
    return ev,ded
def stats(rows,key='f'):
    n=len(rows)
    if n==0: return 'n=0'
    a=sum(1 for r in rows if r[key]['first']=='AGAINST'); w=sum(1 for r in rows if r[key]['first']=='WITH')
    s=[]
    for h in H:
        x=[r[key][h] for r in rows if r[key][h] is not None]
        s.append(f'{h}ч {st.mean(x):+.1f}' if x else f'{h}ч -')
    return f'n={n:2d} против толпы первым {a}/{a+w} ({100*a/max(1,a+w):.0f}%) | ход в сторону толпы, п.: '+', '.join(s)
