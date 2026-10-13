import requests,pathlib,collections
v={}
for line in pathlib.Path('.env').read_text(encoding='utf-8-sig').splitlines():
 if '=' in line and not line.startswith('#'):
  k,x=line.split('=',1);v[k]=x.strip('"')
s=requests.Session();b='http://127.0.0.1:8080';t=s.get(b+'/api/auth/csrf',timeout=15).json()['token'];s.post(b+'/api/auth/login',json={'email':v['SEED_ADMIN_EMAIL'],'password':v['SEED_ADMIN_PASSWORD']},headers={'X-CSRF-TOKEN':t},timeout=20)
r=s.get(b+'/api/dashboard',timeout=50);print(r.status_code);d=r.json(); print({k:v for k,v in d.items() if k!='stations'})
