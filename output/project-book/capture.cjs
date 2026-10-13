const fs = require('fs');
const path = require('path');
const {chromium} = require(process.env.USERPROFILE+'/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright');
const base='http://127.0.0.1:8080';
const out=path.resolve('output/project-book');
const env=Object.fromEntries(fs.readFileSync('.env','utf8').split(/\r?\n/).filter(x=>x.includes('=')&&!x.startsWith('#')).map(x=>{const i=x.indexOf('=');return [x.slice(0,i),x.slice(i+1).replace(/^"|"$/g,'')]}));
(async()=>{
 const browser=await chromium.launch({headless:true,channel:'chrome'});
 const context=await browser.newContext({viewport:{width:1440,height:1100},deviceScaleFactor:1});
 const page=await context.newPage(); page.setDefaultTimeout(60000);
 const shots=[]; const created=[];
 async function shot(name,url){
  let r; for(let attempt=0;attempt<3;attempt++){r=await page.goto(base+url,{waitUntil:'networkidle'}); if(r.status()<500)break;} if(r.status()>=500)throw new Error('Screenshot failed '+url+' '+r.status()); await page.screenshot({path:path.join(out,'screenshots',name+'.png')});
  shots.push({name,url,status:r.status(),title:await page.title(),file:path.join(out,'screenshots',name+'.png')});
  fs.writeFileSync(path.join(out,'screenshots.json'),JSON.stringify(shots,null,2)); console.log(name,r.status());
 }
 async function api(method,url,data){
  const csrf=await (await context.request.get(base+'/api/auth/csrf')).json();
  const r=await context.request.fetch(base+url,{method,data,headers:{'X-CSRF-TOKEN':csrf.token}});
  if(!r.ok()) throw new Error(method+' '+url+' returned '+r.status());
  return r.status()===204?null:await r.json();
 }
 try{
  await shot('01-login','/Workspace/Login'); await shot('02-register','/Workspace/Register');
  await api('POST','/api/auth/login',{email:env.SEED_ADMIN_EMAIL,password:env.SEED_ADMIN_PASSWORD});
  const stations=await (await context.request.get(base+'/api/stations')).json();
  if(!Array.isArray(stations)||!stations.length) throw new Error('No live stations');
  const selected=stations.find(x=>!x.stale&&x.station.history.length>1)||stations.find(x=>!x.stale)||stations[0]; const id=selected.station.id;
  for(const [name,url] of [['03-overview','/'],['04-stations','/Workspace/Stations'],['05-station','/Workspace/Station?id='+encodeURIComponent(id)],['06-alerts','/Workspace/Alerts'],['07-rebalancing','/Workspace/Recommendations'],['08-metrics','/Workspace/Metrics'],['09-reports','/Workspace/Reports'],['10-users','/Workspace/Users'],['11-add-user','/Workspace/EditUser'],['12-profile','/Workspace/Profile'],['13-error','/Workspace/Station?id=book-nonexistent']]) await shot(name,url);
  const me=await (await context.request.get(base+'/api/auth/me')).json();
  await shot('14-edit-user','/Workspace/EditUser?id='+me.id);
  const recs=await (await context.request.get(base+'/api/recommendations')).json();
  if(recs.length) await shot('15-confirm','/Workspace/Confirm?kind=recommendation&id='+recs[0].id+'&status=COMPLETED');
  const suffix=Date.now(); const password='Book-'+require('crypto').randomBytes(14).toString('hex');
  const rider=await api('POST','/api/users',{name:'Project Book Rider',email:'book-rider-'+suffix+'@example.invalid',role:'RIDER',enabled:true,password}); created.push(rider.id);
  const operator=await api('POST','/api/users',{name:'Project Book Operator',email:'book-operator-'+suffix+'@example.invalid',role:'OPERATOR',enabled:true,password}); created.push(operator.id);
  await api('POST','/api/auth/logout');
  await api('POST','/api/auth/login',{email:rider.email,password});
  await api('POST','/api/rider/favorites/'+encodeURIComponent(id));
  for(const [name,url] of [['16-rider','/Rider/Index'],['17-favorites','/Rider/Index?favorites=true'],['18-report','/Rider/Report?id='+encodeURIComponent(id)],['19-rider-profile','/Workspace/Profile']]) await shot(name,url);
  await api('DELETE','/api/rider/favorites/'+encodeURIComponent(id));
  await api('POST','/api/auth/logout'); await api('POST','/api/auth/login',{email:operator.email,password});
  await shot('20-operator','/');
  await api('POST','/api/auth/logout'); await api('POST','/api/auth/login',{email:env.SEED_ADMIN_EMAIL,password:env.SEED_ADMIN_PASSWORD});
  fs.writeFileSync(path.join(out,'screenshots.json'),JSON.stringify(shots,null,2));
  fs.writeFileSync(path.join(out,'live-summary.json'),JSON.stringify({capturedAt:new Date().toISOString(),stations:stations.length,current:stations.filter(x=>!x.stale).length,selectedStation:id,healthy:stations.filter(x=>x.status==='HEALTHY').length,createdTestUsers:created},null,2));
 } finally {
  for(const id of created){try{await api('PATCH','/api/users/'+id+'/enabled',{enabled:false}); console.log('Disabled book account',id)}catch(e){console.log('Cleanup needs attention for test user',id)}}
  await browser.close();
 }
})().catch(e=>{console.error(e.message);process.exit(1)});
