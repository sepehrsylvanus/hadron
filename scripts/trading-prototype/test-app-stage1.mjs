// Browser contract tests against the built real app. API responses are explicitly mocked here.
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import http from 'node:http';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {chromium} from 'playwright-core';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
const dist=path.join(root,'src/frontend/dist');
const server=http.createServer(async(req,res)=>{
 try{
  const u=new URL(req.url,'http://test');const pathname=decodeURIComponent(u.pathname);
  const f=path.resolve(dist,'.'+(pathname.startsWith('/assets/')?pathname:'/index.html'));
  if(!f.startsWith(dist+path.sep))throw Error('invalid path');
  res.writeHead(200,{'Content-Type':{'.js':'application/javascript','.css':'text/css','.svg':'image/svg+xml','.webp':'image/webp'}[path.extname(f)]||'text/html'});res.end(await fs.readFile(f));
 }catch{res.writeHead(404);res.end();}
});
await new Promise(r=>server.listen(0,'127.0.0.1',r));const base=`http://127.0.0.1:${server.address().port}`;
const uid=n=>`00000000-0000-4000-8000-${String(n).padStart(12,'0')}`;
let browser;
try{
 browser=await chromium.launch({executablePath:process.env.CHROMIUM_PATH,headless:true,args:['--no-sandbox','--no-zygote','--disable-dev-shm-usage']});
 const ctx=await browser.newContext({viewport:{width:1440,height:1100}});
 const page=await ctx.newPage();const errors=[],unknown=[],posts=[];let failCard=true,failSubmit=true;let userPermissions=['trading.project.view','trading.project.manage'];let lookupCalls=0;
 page.on('pageerror',e=>errors.push(e.message));page.on('dialog',d=>d.accept());
 await ctx.route('**/api/**',async route=>{
  const req=route.request(),u=new URL(req.url()),p=u.pathname;
  const json=(body,status=200)=>route.fulfill({status,contentType:'application/json',body:JSON.stringify(body)});
  if(p==='/api/auth/bootstrap-status')return json({initialized:true});
  if(p==='/api/auth/me')return json({id:uid(1),userName:'test',fullName:'کاربر آزمون',emailAddress:'test@test.invalid',cellphoneNumber:'09000000000',organizationalUnit:{id:uid(99),nameFa:'بازرگانی'},photoUrl:'/api/auth/me/photo',mustChangePassword:false,isSystemAdministrator:false,dataScopes:[],permissions:userPermissions});
  if(p==='/api/auth/me/photo')return route.fulfill({status:200,contentType:'image/gif',body:Buffer.from('R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==','base64')});
  if(p==='/api/auth/csrf')return json({token:'test-token'});
  if(p==='/api/trading/project-setup/resolve-jalali-date'){
   assert.equal(req.postDataJSON().jalaliDate,'14050726');return json({gregorianDate:'2026-10-18'});
  }
  if(p==='/api/trading/lookups')return json({currencies:[],suppliers:[],references:[],referenceTypes:[]});
  if(p==='/api/trading/project-setup/lookups'){
   lookupCalls++;const kind=u.searchParams.get('kind');
   if(kind==='BUSINESS_CARD'&&failCard){failCard=false;return json({detail:'خطای نمونهٔ خواندن کارت؛ دوباره تلاش کنید.'},503);}
   const fixtures=kind==='CUSTOMER'?[{id:uid(50),code:'CUS-000050',nameFa:'مشتری نمونه'}]:kind==='BUSINESS_CARD'?[{id:uid(60),code:'BCR-000060',nameFa:'کارت بازرگانی نمونه'}]:Array.from({length:26},(_,i)=>({id:uid(i+1),code:'',nameFa:`کارشناس ${i+1}`}));
   const search=u.searchParams.get('search')||'',rows=fixtures.filter(x=>x.nameFa.includes(search)||x.code.includes(search));const current=Number(u.searchParams.get('page'));
   return json({items:rows.slice((current-1)*25,current*25),page:current,pageSize:25,totalCount:rows.length});
  }
  if(p==='/api/trading/project-setup'){
   assert.equal(req.method(),'POST');assert.equal(req.headers()['x-csrf-token'],'test-token');const body=req.postDataJSON();posts.push(body);
   if(failSubmit){failSubmit=false;return json({errors:{titleFa:['خطای اعتبارسنجی نمونهٔ سرور']}},400);}
   return json({project:{id:uid(200)}},201);
  }
  if(p===`/api/trading/projects/${uid(200)}`)return json({project:{id:uid(200),projectNumber:'TRD-000200',titleFa:'تأمین قطعات شارژر',priority:'NORMAL',status:'DRAFT',version:1,updatedAtUtc:'2026-10-18T00:00:00Z',startDate:'2026-10-18',businessCardNameFa:'کارت بازرگانی نمونه',responsibleUserName:'کارشناس 26'},notes:'توضیح مرحله اول',orderRegistrations:[],fxAllocations:[],shipments:[],costs:[],audit:[],capabilities:{canViewOrders:false,canViewFx:false,canViewShipments:false,canViewCustoms:false,canViewCosts:false,canViewAudit:false}});
  unknown.push(p);return json({},404);
 });
 await page.goto(base+'/trading');try{await page.locator('form.trade-setup').waitFor({timeout:15000});}catch(e){console.log({errors,unknown,body:await page.locator('body').innerText()});throw e;}
 assert.equal(await page.locator('.trading-flow-step').count(),6);
 assert.equal(await page.locator('.trade-setup-binding').count(),0);
 assert.equal(await page.locator('[name="priority"],[name="supplierId"],[name="targetDate"],[name="eightDigitCode"]').count(),0);
 assert.equal(await page.locator('[name="projectNumber"]').getAttribute('readonly'),'');
 await page.getByRole('button',{name:'تلاش دوباره',exact:true}).click();
 await page.locator('[data-picker="ASSIGNEE"] select option').nth(1).waitFor({state:'attached'});
 await page.locator('[name="titleFa"]').fill('تأمین قطعات شارژر');await page.locator('[name="startDate"]').fill('۱۴۰۵۰۷۲۶');
 await page.locator('.trading-flow-step').nth(1).click();await page.locator('.trading-flow-step').nth(0).click();assert.equal(await page.locator('[name="titleFa"]').inputValue(),'تأمین قطعات شارژر');assert.equal(await page.locator('.is-done').count(),0);
 console.log('PASS: real /trading embeds only first-step fields, no lineage labels, no false completion; tab switches preserve draft');
 await page.locator('[name="customerId"]').selectOption(uid(50));await page.locator('[name="businessCardId"]').selectOption(uid(60));
 await page.locator('[name="responsibleUserId"]').selectOption(uid(1));await page.locator('[data-picker="ASSIGNEE"]').getByRole('button',{name:'بعدی',exact:true}).click();
 await page.locator(`[name="responsibleUserId"] option[value="${uid(26)}"]`).waitFor({state:'attached'});assert.equal(await page.locator('[name="responsibleUserId"]').inputValue(),uid(1));await page.locator('[name="responsibleUserId"]').selectOption(uid(26));
 await page.locator('[name="notes"]').fill('توضیح مرحله اول');
 assert.equal(await page.locator('[name="startDate"]').inputValue(),'14050726');
 await page.locator('.trade-setup button[type="submit"]').click();await page.getByRole('alert').filter({hasText:'خطای اعتبارسنجی نمونهٔ سرور'}).waitFor();assert.equal(await page.locator('[name="titleFa"]').inputValue(),'تأمین قطعات شارژر');
 assert.equal(posts.length,1);assert.deepEqual(posts[0],{titleFa:'تأمین قطعات شارژر',startDate:'2026-10-18',customerId:uid(50),businessCardId:uid(60),responsibleUserId:uid(26),notes:'توضیح مرحله اول'});
 console.log('PASS: lookup retry and paging, preserved FK selection, shared Jalali resolver, allowlisted payload, CSRF and API validation error');
 await page.setViewportSize({width:390,height:844});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
 await page.setViewportSize({width:1440,height:1100});await page.screenshot({path:'/tmp/trading-stage1-app.png',fullPage:true});
 await page.locator('.trade-setup button[type="submit"]').click();await page.waitForURL(`**/trading/projects/${uid(200)}`);try{await page.getByText('کارشناس 26',{exact:true}).waitFor({timeout:12000});}catch(e){console.log({errors,unknown,body:await page.locator('body').innerText()});throw e;}await page.getByText('کارت بازرگانی نمونه',{exact:true}).waitFor();
 assert.equal(posts.length,2);console.log('PASS: successful real-app command navigates to project object and displays saved setup facts');
 userPermissions=['trading.project.view'];const callsBeforeReadOnly=lookupCalls;
 const readOnly=await ctx.newPage();readOnly.on('pageerror',e=>errors.push(e.message));await readOnly.goto(base+'/trading');await readOnly.getByText('برای ایجاد پرونده به مجوز مدیریت پروژهٔ بازرگانی نیاز دارید.',{exact:true}).waitFor();assert.equal(await readOnly.locator('form.trade-setup').count(),0);assert.equal(lookupCalls,callsBeforeReadOnly);
 console.log('PASS: project-view-only users can open the workspace but cannot load assignment pickers or create a project');
 assert.deepEqual(errors,[]);assert.deepEqual(unknown,[]);
 // Both standalone variants are exported from the same React view and carry the same eight facts.
 const expected=['projectNumber','startDate','titleFa','customerId','businessCardId','responsibleUserId','status','notes'];
 for(const file of ['stage-1.html','stage-1-annotated.html']){
  const offline=await ctx.newPage();const offlineErrors=[];offline.on('pageerror',e=>offlineErrors.push(e.message));await offline.goto('file://'+path.join(root,'docs/ui/trading-stage-1',file));
  assert.deepEqual(await offline.locator('[data-setup-field]').evaluateAll(es=>es.map(e=>e.dataset.setupField)),expected);
  assert.equal(await offline.locator('.trade-setup-binding').count(),file.includes('annotated')?8:0);
  await offline.setViewportSize({width:390,height:844});assert.equal(await offline.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
  assert.deepEqual(offlineErrors,[]);
 }
 console.log('PASS: both offline HTML exports share the exact production field set and fit mobile');
 console.log('NOTE: API responses in this browser test are mocked; database integration tests are written separately in .NET and are not executed by this script.');
} finally {await browser?.close();await new Promise(r=>server.close(r));}
