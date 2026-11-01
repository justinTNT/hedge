import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {DatabaseSync} from 'node:sqlite';
import {fixture,jpeg} from './helpers/contributions.mjs';
import {protectPreview} from '../preview-gate.mjs';
import {previewFetch} from '../mobile/transport.mjs';
const digest=async value=>Buffer.from(await crypto.subtle.digest('SHA-256',new TextEncoder().encode(value))).toString('hex');
const json=async response=>{assert.equal(response.status,200,await response.clone().text());return response.json()};
const headers=token=>({Authorization:'Bearer '+token,'Content-Type':'application/json'});
async function login(f,account='botanist') {
  const {token:old}=await json(await f.call('/api/mobile/bootstrap',{method:'POST'}));
  const cookie=await f.login(await f.bootstrap(),account);
  const verifier=crypto.randomUUID().replaceAll('-','')+crypto.randomUUID().replaceAll('-','');
  const challenge=await digest(verifier);
  const back=await f.get('/api/mobile/return?challenge='+challenge,cookie);
  assert.equal(back.status,302);const url=new URL(back.headers.get('location'));
  assert.equal(url.protocol,'nativeplants:');assert.equal(url.host,'auth');
  const code=url.searchParams.get('code');assert.ok(code);
  const exchange=(proof=verifier)=>f.call('/api/mobile/exchange',{method:'POST',headers:headers(old),body:JSON.stringify({code,verifier:proof})});
  assert.equal((await exchange('0'.repeat(64))).status,401);
  const {token}=await json(await exchange());
  assert.equal((await exchange()).status,401);
  assert.equal((await f.call('/api/mobile/me',{headers:headers(old)})).status,401);
  return {token,cookie,code};
}
test('fresh native login: PKCE, one use, hashed storage, private notes/photos, roles and revocation',async()=>{
  const f=fixture();f.env.CONTRIBUTIONS_REQUIRE_LOGIN='true';
  const anon=(await json(await f.call('/api/mobile/bootstrap',{method:'POST'}))).token;
  assert.deepEqual(await json(await f.call('/api/mobile/me',{headers:headers(anon)})),{guest:null});
  assert.equal((await f.call('/api/plants/personal/plant-a',{headers:headers(anon)})).status,401);
  const a=await login(f,'alice'),b=await login(f,'bob');
  assert.notEqual(a.token,b.token);
  const row=f.db.prepare('SELECT * FROM mobile_sessions WHERE id=?').get(await digest(a.token));assert.ok(row);assert.notEqual(row.id,a.token);
  assert.equal((await json(await f.call('/api/mobile/me',{headers:headers(a.token)}))).guest.identity.provider,'google');
  const state=await json(await f.call('/api/plants/personal/plant-a',{headers:headers(a.token)}));
  const h={...headers(a.token),'X-Contribution-Viewer':state.ViewerToken,Origin:'https://localhost'};
  const save=(id,extra={})=>f.call('/api/plants/v2/notes/entry',{method:'POST',headers:{...h,...extra},body:JSON.stringify({plantId:'plant-a',id,revision:0,text:'On Android',purpose:'private',photoIds:[]})});
  await json(await save('native-note'));
  for(let i=1;i<5;i++)await json(await save('native-note-'+i));
  assert.equal((await save('sixth')).status,409);
  assert.equal((await json(await f.call('/api/plants/personal/plant-a',{headers:headers(b.token)}))).Notes.length,0);
  assert.equal((await json(await f.personal(a.cookie))).Notes.length,5);
  assert.equal((await save('forged',{Authorization:'Bearer invalid',Cookie:a.cookie})).status,401);
  const form=new FormData();form.append('image',new Blob([jpeg()],{type:'image/jpeg'}),'image.jpg');form.append('thumbnail',new Blob([jpeg()],{type:'image/jpeg'}),'thumb.jpg');
  const upload=await json(await f.call('/api/plants/personal/plant-a/photos/native-photo',{method:'POST',headers:{Authorization:h.Authorization,'X-Contribution-Viewer':h['X-Contribution-Viewer']},body:form}));
  const image=upload.Photos[0].Image;
  assert.equal((await f.call(image,{headers:headers(a.token)})).status,200);
  assert.equal((await f.call(image,{headers:headers(b.token)})).status,404);
  assert.equal((await f.call('/api/plants/v2/review',{headers:headers(a.token)})).status,403);
  f.db.prepare('INSERT INTO grants(id,provider,provider_user_id,role,enabled,created_at) VALUES (?,?,?,?,?,?)').run('grant','google','alice','curator',1,1);
  assert.equal((await f.call('/api/plants/v2/review',{headers:headers(a.token)})).status,200);
  assert.equal((await f.call('/api/admin/types',{headers:headers(a.token)})).status,401);
  f.db.exec("UPDATE grants SET enabled=0");
  assert.equal((await f.call('/api/plants/v2/review',{headers:headers(a.token)})).status,403);
  await json(await f.call('/api/mobile/signout',{method:'POST',headers:headers(a.token)}));
  assert.equal((await f.call(image,{headers:{...headers(a.token),Cookie:a.cookie}})).status,404);
  assert.equal((await f.call('/api/mobile/me',{headers:headers(a.token)})).status,401);
  assert.equal((await f.personal(a.cookie)).status,200); // another device/browser survives
  f.db.prepare('UPDATE mobile_sessions SET expires_at=0 WHERE id=?').run(await digest(b.token));
  assert.equal((await f.call('/api/mobile/me',{headers:headers(b.token)})).status,401);
});
test('handoff needs a verified cookie and bounded valid JSON; native rows never enter generic admin',async()=>{
  const f=fixture(),cookie=await f.bootstrap();
  const returned=await f.get('/api/mobile/return?challenge='+'a'.repeat(64),cookie);
  assert.equal(new URL(returned.headers.get('location')).searchParams.get('error'),'identity');
  for(const body of ['oops','null','{}',JSON.stringify({code:1,verifier:'x'}),'x'.repeat(25000)]) {
    const response=await f.call('/api/mobile/exchange',{method:'POST',headers:{'Content-Type':'application/json'},body});
    assert.equal(response.status,400);
  }
  const admin=await f.call('/api/admin/types',{headers:{'X-Admin-Key':f.env.ADMIN_KEY}});
  assert.equal(admin.status,200);assert.doesNotMatch(await admin.text(),/MobileSession|MobileAuthCode/);
});
test('preview gate keeps bearer separate and requires its password for native endpoints and media',async()=>{
  const password='test-preview-password-at-least-thirty-two-characters';const credential='Basic '+btoa('preview:'+password);
  let calls=0;const worker=protectPreview({fetch(req){calls++;assert.equal(req.headers.get('Authorization'),'Bearer app-session');assert.equal(req.headers.get('X-Preview-Authorization'),null);return new Response('ok')}});
  const call=(extra={})=>worker.fetch(new Request('https://plants.test/api/mobile/me',{headers:{Authorization:'Bearer app-session',...extra}}),{PREVIEW_PASSWORD:password});
  assert.equal((await call()).status,401);assert.equal((await call({'X-Preview-Authorization':'Basic invalid'})).status,401);
  assert.equal((await call({'X-Preview-Authorization':credential})).status,200);assert.equal(calls,1);
});
test('the Android version code is derived from the mobile package version and rises with every bump',async()=>{
  const {versionCode,appVersion}=await import('../mobile/scripts/version.mjs');
  assert.equal(versionCode('1.1.0'),10100);assert.equal(versionCode('1.1.1'),10101);assert.equal(versionCode('2.0.0'),20000);
  assert.ok(versionCode('2.0.0')>versionCode('1.99.99'));
  assert.throws(()=>versionCode('1.1'));assert.throws(()=>versionCode('1.100.0'));assert.throws(()=>versionCode('1.0.0-beta'));
  assert.equal(versionCode(appVersion()),versionCode(JSON.parse(readFileSync(new URL('../mobile/package.json',import.meta.url),'utf8')).version),'the prepare step and the package agree');
});
test('native preview transport marks only API-origin requests as the app, never external hosts or packaged assets',async()=>{
  globalThis.location={href:'https://localhost/'};const requests=[];
  const fetch=previewFetch(async(input,init)=>{requests.push({input,init});return new Response('ok')},'https://plants.test');
  await fetch('https://plants.test/api/mobile/me',{headers:{Authorization:'Bearer account'}});
  assert.equal(requests[0].init.headers.get('Authorization'),'Bearer account');assert.equal(requests[0].init.headers.get('X-Preview-Client'),'native-plants-app');assert.equal(requests[0].init.headers.get('X-Preview-Authorization'),null);
  await fetch('https://elsewhere.test/image');await fetch('/assets/app.js');
  assert.equal(requests[1].init.headers,undefined);assert.equal(requests[2].init.headers,undefined);
});
test('mobile migration is additive and produces the generated schema',()=>{
  const fresh=new DatabaseSync(':memory:'),old=new DatabaseSync(':memory:');
  const schema=readFileSync(new URL('../schema.sql',import.meta.url),'utf8');fresh.exec(schema);
  old.exec(schema.replace(/CREATE TABLE mobile_(sessions|auth_codes) \([\s\S]*?\);/g,'').replace(/CREATE INDEX idx_mobile_.*?;\n/g,'').replace('    superseded_by TEXT,\n',''));
  old.exec(readFileSync(new URL('../migrations/0004_mobile.sql',import.meta.url),'utf8'));
  for(const table of ['identities','mobile_sessions','mobile_auth_codes']) {
    const columns=db=>db.prepare('PRAGMA table_info('+table+')').all().map(({cid,...row})=>row).sort((a,b)=>a.name.localeCompare(b.name));
    assert.deepEqual(columns(old),columns(fresh));
  }
});
