import { test } from 'node:test';
import assert from 'node:assert/strict';
import { DatabaseSync } from 'node:sqlite';
import { readFileSync } from 'node:fs';
import worker from '../dist/server/Worker.js';
import { oauth, subject } from '../dist/server/AuthConfig.js';
import { safeReturnPath } from '../dist/server/packages/hedge/src/Hedge/OAuth.js';
import { OAuthDeps, onOAuthComplete } from '../dist/server/packages/modules/identity/src/Server/Handlers.js';
import { createWorker, WorkerConfig, BlobServingPolicy } from '../dist/server/packages/hedge/src/Hedge/Router.js';
import { deps } from '../dist/server/AuthConfig.js';
import { empty } from '../dist/server/fable_modules/fable-library-js.4.29.0/List.js';

const schema=readFileSync(new URL('../schema.sql',import.meta.url),'utf8');
function fixture() {
  const db=new DatabaseSync(':memory:'); db.exec('PRAGMA foreign_keys=ON'); db.exec(schema);
  const statement=(sql,values=[])=>({
    bind(...args){return statement(sql,args)},
    async all(){return {results:db.prepare(sql).all(...values),success:true,meta:{}}},
    async first(){return db.prepare(sql).get(...values)},
    async run(){const s=db.prepare(sql);if(s.columns().length)return {results:s.all(...values),success:true,meta:{}};const r=s.run(...values);return {results:[],success:true,meta:{changes:r.changes}}}
  });
  const env={DB:{prepare:statement,async batch(stmts){db.exec('BEGIN');try{const r=await Promise.all(stmts.map(s=>s.run()));db.exec('COMMIT');return r}catch(e){db.exec('ROLLBACK');throw e}}},
    ADMIN_KEY:'owner-fixture',ENVIRONMENT:'development',GUEST_SECRET:'test-signing-secret-with-more-than-32-characters',OAUTH_SECRET:'independent-oauth-fixture-secret-long-enough',BLOBS:{async get(){return null}},
    ASSETS:{async fetch(){return new Response('Not found',{status:404})}},GOOGLE_CLIENT_ID:'google-fixture-id',GOOGLE_CLIENT_SECRET:'google-fixture-secret',GITHUB_CLIENT_ID:'github-fixture-id',GITHUB_CLIENT_SECRET:'github-fixture-secret'};
  const call=(path,options={})=>worker.fetch(new Request('http://plants.test'+path,options),env,{waitUntil(){}});
  async function bootstrap() {const r=await call('/api/auth/me');return r.headers.get('set-cookie').split(';')[0]}
  async function login(provider='google',returnTo='/plants/example/name#section-references') {
    const start=await call('/api/auth/'+provider+'/login?returnTo='+encodeURIComponent(returnTo));
    assert.equal(start.status,302);
    const cookie=start.headers.get('set-cookie').split(';')[0];
    const state=new URL(start.headers.get('location')).searchParams.get('state');
    const original=globalThis.fetch;
    globalThis.fetch=async (url)=>{
      if(String(url).includes('/token') || String(url).includes('/access_token')) return Response.json({access_token:'verified-fixture-token'});
      if(String(url).includes('userinfo')) return Response.json({id:'google-account',name:'Lynne "Researcher"\nB.',email:'researcher@example.test',picture:''});
      if(String(url)==='https://api.github.com/user') return Response.json({id:12345,name:null,login:'plant-researcher',avatar_url:'',email:null});
      throw new Error('Unexpected provider fetch: '+url);
    };
    try {
      const callback=await call('/api/auth/'+provider+'/callback?code=fixture&state='+encodeURIComponent(state),{headers:{Cookie:cookie}});
      const resultCookie=callback.headers.get('set-cookie')?.split(';')[0] || cookie;
      return {callback,cookie:resultCookie,state,originalCookie:cookie};
    } finally {globalThis.fetch=original}
  }
  return {db,env,call,bootstrap,login};
}

test('signed bootstrap stays invisible, and anonymous guests are not verified identity subjects',async()=>{
  const f=fixture();const response=await f.call('/api/auth/me');
  assert.deepEqual(await response.json(),{guest:null});
  assert.equal(response.headers.get('cache-control'),'private, no-store');
  assert.match(response.headers.get('set-cookie'),/HttpOnly; SameSite=Lax/);
  const cookie=response.headers.get('set-cookie').split(';')[0];
  const request=new Request('http://plants.test/api/personal',{headers:{Cookie:cookie}});
  assert.equal((await subject(f.env,request))[0],undefined);
  assert.equal((await f.call('/api/blobs/guest',{method:'POST',headers:{Cookie:cookie}})).status,404);
  assert.equal((await f.call('/api/blobs/guest',{method:'POST'})).status,404);
  assert.equal((await f.call('/blobs/private/native-plants/secret.webp')).status,404);
  assert.equal((await f.call('/blobs/private%2Fnative-plants%2Fsecret.webp')).status,404);
});

test('only completely configured providers are offered; discovery does not require a session',async()=>{
  const f=fixture();assert.deepEqual((await (await f.call('/api/auth/providers')).json()).providers,['github','google']);
  f.env.GITHUB_CLIENT_SECRET='';assert.deepEqual((await (await f.call('/api/auth/providers')).json()).providers,['google']);
  f.env.OAUTH_SECRET='';assert.deepEqual((await (await f.call('/api/auth/providers')).json()).providers,[]);
  assert.equal((await f.call('/api/auth/google/login')).status,400);
  assert.equal((await f.call('/api/plants/catalogue')).status,200);
});

test('Google OAuth activates directly, handles quoted names, and retains the current species fragment',async()=>{
  const f=fixture();const {callback,cookie}=await f.login();
  assert.equal(callback.status,302);assert.equal(callback.headers.get('location'),'/plants/example/name#section-references');
  const data=await (await f.call('/api/auth/me',{headers:{Cookie:cookie}})).json();
  assert.equal(data.guest.identity.provider,'google');assert.equal(data.guest.identity.name,'Lynne "Researcher"\nB.');
  assert.deepEqual(await subject(f.env,new Request('http://plants.test/api/personal',{headers:{Cookie:cookie}})),[['google','google-account'],undefined]);
  assert.equal((await f.call('/api/admin/Plant',{headers:{Cookie:cookie}})).status,401);
});

test('GitHub uses the same login flow and falls back to its login name',async()=>{
  const f=fixture();const {callback,cookie}=await f.login('github','/taxonomy');
  assert.equal(callback.headers.get('location'),'/taxonomy');
  const data=await (await f.call('/api/auth/me',{headers:{Cookie:cookie}})).json();
  assert.equal(data.guest.identity.name,'plant-researcher');assert.equal(data.guest.identity.provider,'github');
});

test('logging into the same provider on another browser adopts the existing verified subject',async()=>{
  const f=fixture();const first=await f.login();const second=await f.login();
  const get=async cookie=>(await (await f.call('/api/auth/me',{headers:{Cookie:cookie}})).json()).guest;
  assert.deepEqual(await get(first.cookie),await get(second.cookie));
  assert.equal(f.db.prepare('SELECT COUNT(*) n FROM identities').get().n,1);
});

test('logout is same-origin POST, clears only the browser cookie, and does not park provider identities',async()=>{
  const f=fixture();const {cookie}=await f.login();const before=f.db.prepare('SELECT * FROM identities').all();
  for(const origin of [undefined,'https://evil.test','null']) {
    const headers={Cookie:cookie};if(origin)headers.Origin=origin;
    const response=await f.call('/api/auth/logout',{method:'POST',headers});
    assert.equal(response.status,403);assert.equal(response.headers.get('set-cookie'),null);
  }
  assert.equal((await f.call('/api/auth/logout',{headers:{Cookie:cookie}})).status,404);
  const out=await f.call('/api/auth/logout',{method:'POST',headers:{Cookie:cookie,Origin:'http://plants.test'}});
  assert.equal(out.status,200);assert.match(out.headers.get('set-cookie'),/hedge_guest=;.*Max-Age=0/);
  assert.equal(out.headers.get('cache-control'),'no-store');
  assert.deepEqual(f.db.prepare('SELECT * FROM identities').all(),before);
  assert.deepEqual(await (await f.call('/api/auth/me')).json(),{guest:null});
  assert.equal((await (await f.call('/api/auth/me',{headers:{Cookie:cookie}})).json()).guest.identity.provider,'google');
});

test('anonymous/deleted active identities stay hidden; a valid signature alone is not login',async()=>{
  const f=fixture();const {cookie}=await f.login();
  f.db.exec("UPDATE identities SET provider='anonymous'");
  assert.deepEqual(await (await f.call('/api/auth/me',{headers:{Cookie:cookie}})).json(),{guest:null});
  f.db.exec("UPDATE identities SET provider='google'; UPDATE guests SET deleted_at=1");
  assert.deepEqual(await (await f.call('/api/auth/me',{headers:{Cookie:cookie}})).json(),{guest:null});
  assert.equal((await subject(f.env,new Request('http://plants.test',{headers:{Cookie:cookie}})))[0],undefined);
});

test('OAuth callback rejects missing, forged and wrong-browser credentials before provider calls',async()=>{
  const f=fixture();const start=await f.call('/api/auth/google/login');
  const state=new URL(start.headers.get('location')).searchParams.get('state');
  for(const cookie of ['', 'hedge_guest=forged',await f.bootstrap()]) {
    assert.equal((await f.call('/api/auth/google/callback?code=fixture&state='+encodeURIComponent(state),{headers:{Cookie:cookie}})).status,400);
  }
  assert.equal(f.db.prepare('SELECT COUNT(*) n FROM identities').get().n,0);
});

test('return paths reject external/ambiguous navigation while preserving safe paths, query and fragment',async()=>{
  for(const value of ['https://evil.test','//evil.test','/\\evil.test','/\nevil.test',' /plants','javascript:alert(1)','/a/..//evil.test','/a|b'])assert.equal(safeReturnPath(value),'/',value);
  assert.equal(safeReturnPath('/plants/a?name=red%20flower#section-fruit'),'/plants/a?name=red%20flower#section-fruit');
  const f=fixture();assert.equal((await f.login('google','//evil.test')).callback.headers.get('location'),'/');
});

test('shared completion still supports the existing claim flow',async()=>{
  const f=fixture();const policy=new OAuthDeps(empty(),empty(),()=>false);
  const result=await onOAuthComplete(policy,f.env.DB,f.env.BLOBS,'fixture-guest',
    {Name:'A botanist',PictureUrl:'',Provider:'google',ProviderUserId:'claim-account'},'/blog/article');
  assert.match(result.RedirectUrl,/^\/auth\/claim\?identity=.*&returnTo=%2Fblog%2Farticle$/);
  assert.equal(f.db.prepare('SELECT activated_at FROM identities').get().activated_at,null);
});

test('existing guest-upload hosts retain their signed-guest gate when they explicitly enable it',async()=>{
  const f=fixture();const legacy=createWorker(new WorkerConfig(()=>undefined,undefined,undefined,deps,true,empty(),new BlobServingPolicy(empty()),undefined));
  const response=await legacy.fetch(new Request('http://plants.test/api/blobs/guest',{method:'POST'}),f.env,{});
  assert.equal(response.status,401);
  const cookie=await f.bootstrap();
  const accepted=await legacy.fetch(new Request('http://plants.test/api/blobs/guest',{method:'POST',headers:{Cookie:cookie},body:new FormData()}),f.env,{});
  assert.equal(accepted.status,400); // Reaches upload validation instead of the disabled-route 404.
});

test('owner can look up identity accounts but cannot mutate them through admin CRUD',async()=>{
  const f=fixture();const {cookie}=await f.login();const headers={'X-Admin-Key':f.env.ADMIN_KEY};
  const data=await (await f.call('/api/admin/types',{headers})).json();
  assert.deepEqual(data.types.find(t=>t.name==='Identity').ops,['list','read']);
  assert.deepEqual(data.types.find(t=>t.name==='Grant').ops,['list','read','create','update','delete']);
  assert.ok(!data.types.some(t=>['Guest','PlantNote','PersonalPlantPhoto','PlantViewPreference','ContributionClaim'].includes(t.name)));
  const response=await f.call('/api/admin/Identity',{headers});assert.equal(response.status,200);
  const identities=(await response.json()).records;
  const account=identities.find(i=>i.provider==='google');
  assert.equal(account.name,'Lynne "Researcher"\nB.');assert.equal(account.email,'researcher@example.test');
  assert.equal(account.providerUserId,'google-account');
  assert.deepEqual((await (await f.call('/api/admin/Identity/'+account.id,{headers})).json()).record,account);
  const before=f.db.prepare('SELECT * FROM identities ORDER BY id').all();
  for(const [method,path] of [['POST','Identity'],['PUT','Identity/'+account.id],['DELETE','Identity/'+account.id]]) {
    assert.equal((await f.call('/api/admin/'+path,{method,headers:{...headers,'Content-Type':'application/json'},body:JSON.stringify({...account,providerUserId:'forged-account'})})).status,403);
  }
  assert.deepEqual(f.db.prepare('SELECT * FROM identities ORDER BY id').all(),before);
  for(const path of ['types','Identity','Identity/'+account.id,'Grant']) {
    assert.equal((await f.call('/api/admin/'+path)).status,401);
    assert.equal((await f.call('/api/admin/'+path,{headers:{Cookie:cookie}})).status,401);
  }
  assert.equal((await f.call('/api/admin/Guest',{headers})).status,404);
});

test('owner assigns and revokes curator access using an identity lookup and Grant CRUD',async()=>{
  const f=fixture();const {cookie}=await f.login();const headers={'X-Admin-Key':f.env.ADMIN_KEY,'Content-Type':'application/json'};
  const account=(await (await f.call('/api/admin/Identity',{headers})).json()).records.find(i=>i.provider==='google');
  const review=()=>f.call('/api/plants/review',{headers:{Cookie:cookie}});
  assert.equal((await review()).status,403);
  const grant={provider:account.provider,providerUserId:account.providerUserId,role:'curator',enabled:true,grantedBy:null};
  const created=await f.call('/api/admin/Grant',{method:'POST',headers,body:JSON.stringify(grant)});
  assert.equal(created.status,200);const saved=(await created.json()).record;
  assert.equal((await review()).status,200);
  // A curator can review, but cannot browse identities or grant powers to anyone else.
  assert.equal((await f.call('/api/admin/Identity',{headers:{Cookie:cookie}})).status,401);
  assert.equal((await f.call('/api/admin/Grant',{method:'POST',headers:{Cookie:cookie,'Content-Type':'application/json'},body:JSON.stringify(grant)})).status,401);
  assert.equal((await f.call('/api/admin/Grant/'+saved.id,{method:'PUT',headers,body:JSON.stringify({...grant,enabled:false})})).status,200);
  assert.equal((await review()).status,403);
  assert.equal((await f.call('/api/admin/Grant/'+saved.id,{method:'DELETE',headers})).status,200);
});


test('additive migration matches the fresh generated schema and leaves an existing plant intact',()=>{
  const fresh=fixture().db;
  const old=new DatabaseSync(':memory:');
  const objects=fresh.prepare("SELECT type,name,tbl_name,sql FROM sqlite_schema WHERE sql IS NOT NULL AND name NOT LIKE 'sqlite_%' ORDER BY CASE type WHEN 'table' THEN 0 ELSE 1 END,name").all();
  for(const object of objects.filter(o=>!['guests','identities'].includes(o.tbl_name)))old.exec(object.sql);
  const fields=old.prepare('PRAGMA table_info(plants)').all().filter(c=>c.notnull || c.pk);
  const values=fields.map(c=>c.name==='id'?'preserved-plant':c.type==='INTEGER'?1:'Owner value');
  old.prepare('INSERT INTO plants ('+fields.map(c=>c.name).join(',')+') VALUES ('+fields.map(()=>'?').join(',')+')').run(...values);
  const before=old.prepare('SELECT * FROM plants').all();
  old.exec(readFileSync(new URL('../migrations/0001_identity.sql',import.meta.url),'utf8'));
  assert.deepEqual(old.prepare('SELECT * FROM plants').all(),before);
  const describe=db=>db.prepare("SELECT type,name,tbl_name,sql FROM sqlite_schema WHERE sql IS NOT NULL AND name NOT LIKE 'sqlite_%' ORDER BY type,name").all();
  assert.deepEqual(describe(old),describe(fresh));
});


test('capability discovery uses the same owner and curator policy as review and admin',async()=>{
  const f=fixture();
  const access=async headers=>{
    const response=await f.call('/api/plants/access',{headers});
    assert.equal(response.status,200);assert.equal(response.headers.get('cache-control'),'private, no-store');
    assert.match(response.headers.get('vary'),/Cookie/);assert.match(response.headers.get('vary'),/X-Admin-Key/);
    return response.json();
  };
  const none={CanEditCatalogue:false,CanReview:false};
  assert.deepEqual(await access({}),none);
  assert.equal(f.db.prepare('SELECT COUNT(*) AS n FROM guests').get().n,0);
  const anon=await f.bootstrap();
  assert.deepEqual(await access({Cookie:anon}),none);
  assert.deepEqual(await access({'X-Admin-Key':'wrong-key'}),none);
  assert.deepEqual(await access({'X-Admin-Key':f.env.ADMIN_KEY}),{CanEditCatalogue:true,CanReview:true});
  assert.equal((await f.call('/api/plants/review',{headers:{Cookie:anon}})).status,403);
  assert.equal((await f.call('/api/plants/review',{headers:{Cookie:anon,'X-Admin-Key':f.env.ADMIN_KEY}})).status,200);
  const {cookie}=await f.login();
  assert.deepEqual(await access({Cookie:cookie}),none);
  f.db.prepare('INSERT INTO grants (id,provider,provider_user_id,role,enabled,created_at) VALUES (?,?,?,?,?,?)').run('reviewer','google','google-account','curator',1,1);
  assert.deepEqual(await access({Cookie:cookie}),{CanEditCatalogue:false,CanReview:true});
  assert.equal((await f.call('/api/plants/review',{headers:{Cookie:cookie}})).status,200);
  assert.equal((await f.call('/api/admin/types',{headers:{Cookie:cookie}})).status,401);
  f.db.exec('UPDATE grants SET enabled=0');
  assert.deepEqual(await access({Cookie:cookie}),none);
  assert.equal((await f.call('/api/plants/review',{headers:{Cookie:cookie}})).status,403);
  f.env.ADMIN_KEY='';assert.deepEqual(await access({}),none);
});
