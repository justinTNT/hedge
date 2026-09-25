import { test } from 'node:test';
import assert from 'node:assert/strict';
import { DatabaseSync } from 'node:sqlite';
import { readFileSync } from 'node:fs';
import worker from '../dist/server/Worker.js';
import { cleanJpeg } from '../src/Server/contribution-media.mjs';
import { claim } from '../dist/server/ContributionOwnership.js';

const schema=readFileSync(new URL('../schema.sql',import.meta.url),'utf8');
// Small JPEG marker fixture: parser tests deliberately exercise dimensions/metadata without a decoder.
const jpeg=(w=300,h=220)=>new Uint8Array([255,216,255,225,0,12,...new TextEncoder().encode('GPS secret'),255,192,0,11,8,h>>8,h&255,w>>8,w&255,1,1,17,0,255,218,0,8,1,1,0,0,63,0,17,255,0,23,255,217]);

function fixture() {
  const db=new DatabaseSync(':memory:');db.exec('PRAGMA foreign_keys=ON');db.exec(schema);
  let beforeRun=null, beforeFirst=null, beforePut=null, failPut=0, puts=0, failDelete=0, deletes=0;
  const objects=new Map();
  const statement=(sql,values=[])=>({
    bind(...args){return statement(sql,args)},
    async first(){if(beforeFirst)await beforeFirst(sql,values);return db.prepare(sql).get(...values)},
    async all(){return {results:db.prepare(sql).all(...values),meta:{},success:true}},
    async run(){if(beforeRun)await beforeRun(sql,values);const s=db.prepare(sql);if(s.columns().length)return {results:s.all(...values),meta:{},success:true};const result=s.run(...values);return {results:[],meta:{changes:Number(result.changes)},success:true}}
  });
  const env={DB:{prepare:statement,async batch(stmts){db.exec('BEGIN');try{const results=[];for(const s of stmts)results.push(await s.run());db.exec('COMMIT');return results}catch(e){db.exec('ROLLBACK');throw e}}},
    ENVIRONMENT:'development',CONTRIBUTIONS_REQUIRE_LOGIN:'false',ADMIN_KEY:'owner-fixture',GUEST_SECRET:'signed-session-secret-more-than-thirty-two-chars',OAUTH_SECRET:'separate-oauth-secret-more-than-thirty-two-chars',GOOGLE_CLIENT_ID:'fixture',GOOGLE_CLIENT_SECRET:'fixture',
    BLOBS:{async put(key,value){if(beforePut)await beforePut(key,value);puts++;if(puts===failPut)throw new Error('R2 unavailable');objects.set(key,new Uint8Array(await new Response(value).arrayBuffer()));return {key}},async get(key){const value=objects.get(key);return value?{body:value,size:value.length,httpMetadata:{contentType:'image/jpeg'}}:null},async delete(key){deletes++;if(deletes===failDelete)throw new Error('R2 delete unavailable');objects.delete(key)}},
    ASSETS:{async fetch(){return new Response('Missing',{status:404})}}};
  for(const id of ['plant-a','plant-b']) {
    const cols=db.prepare('PRAGMA table_info(plants)').all().filter(c=>c.notnull||c.pk);
    const values=cols.map(c=>c.name==='id'?id:c.name==='slug'?id:c.type==='INTEGER'?1:c.name==='scientific_name'?id:'');
    db.prepare('INSERT INTO plants ('+cols.map(c=>c.name).join(',')+') VALUES ('+cols.map(()=>'?').join(',')+')').run(...values);
  }
  const call=(path,options={})=>worker.fetch(new Request('http://plants.test'+path,options),env,{waitUntil(){}});
  const get=(path,cookie,extra={})=>call(path,{headers:{Cookie:cookie,...extra}});
  const personalPath=plant=>'/api/plants/personal/'+plant;
  const personal=async(cookie,plant='plant-a')=>get(personalPath(plant),cookie);
  const viewer=async(cookie,plant='plant-a')=>(await (await personal(cookie,plant)).json()).ViewerToken;
  async function bootstrap(){return (await call('/api/auth/me')).headers.get('set-cookie').split(';')[0]}
  async function post(cookie,data,plant='plant-a',headers={}) {
    return call(personalPath(plant),{method:'POST',headers:{Cookie:cookie,Origin:'http://plants.test','Content-Type':'application/json','X-Contribution-Viewer':await viewer(cookie,plant),...headers},body:JSON.stringify(data)});
  }
  async function upload(cookie,id='photo-a',plant='plant-a',image=jpeg(),thumb=jpeg()) {
    const form=new FormData();form.append('image',new Blob([image],{type:'image/jpeg'}),'image.jpg');form.append('thumbnail',new Blob([thumb],{type:'image/jpeg'}),'thumbnail.jpg');
    return call(personalPath(plant)+'/photos/'+id,{method:'POST',headers:{Cookie:cookie,Origin:'http://plants.test','X-Contribution-Viewer':await viewer(cookie,plant)},body:form});
  }
  const review=(data,cookie='',key=env.ADMIN_KEY)=>call('/api/plants/review',data?{method:'POST',headers:{Origin:'http://plants.test','Content-Type':'application/json',Cookie:cookie,'X-Admin-Key':key},body:JSON.stringify(data)}:{headers:{Cookie:cookie,'X-Admin-Key':key}});
  async function login(cookie,account='botanist') {
    const start=await get('/api/auth/google/login?returnTo=%2Fplants%2Fplant-a',cookie);
    const state=new URL(start.headers.get('location')).searchParams.get('state');
    const original=globalThis.fetch;
    globalThis.fetch=async(url)=>String(url).includes('/token')?Response.json({access_token:'verified'}):Response.json({id:account,name:'Botanist',picture:'',email:''});
    try {
      const response=await get('/api/auth/google/callback?code=fixture&state='+encodeURIComponent(state),cookie);
      assert.equal(response.status,302,await response.clone().text());
      return response.headers.get('set-cookie')?.split(';')[0]||cookie;
    } finally {globalThis.fetch=original}
  }
  return {db,env,call,get,personal,post,upload,review,bootstrap,login,objects,viewer,setBeforeRun:fn=>beforeRun=fn,setBeforeFirst:fn=>beforeFirst=fn,setBeforePut:fn=>beforePut=fn,failNextPut:n=>failPut=puts+n,failNextDelete:n=>failDelete=deletes+n};
}
const note=(Id,Text='A private observation',Correction=false,Revision=0)=>({Action:'saveNote',Id,Revision,Text,Correction});
const photo=(Id,Revision=1,Offered=true)=>({Action:'savePhoto',Id,Revision,Offered,Caption:'Flower detail',Photographer:'A contributor'});
async function body200(response) {assert.equal(response.status,200,await response.clone().text());return response.json()}

test('anonymous signed sessions have isolated private notebooks, with no public identity or catalogue leak',async()=>{
  const f=fixture(),a=await f.bootstrap(),b=await f.bootstrap();
  assert.equal((await f.personal('')).status,401);assert.equal((await f.personal('hedge_guest=forged')).status,401);
  const saved=await body200(await f.post(a,note('note-a')));assert.equal(saved.Anonymous,true);assert.equal(saved.Notes[0].Text,'A private observation');
  assert.equal((await body200(await f.personal(b))).Notes.length,0);
  assert.equal((await f.post(b,note('note-a','Changed',false,1))).status,409);
  assert.equal((await f.post(a,note('note-a','Wrong plant',false,1),'plant-b')).status,409);
  assert.equal((await f.post(a,note('note-b'),'plant-a',{Origin:'https://elsewhere.test'})).status,403);
  assert.equal((await f.post(a,note('note-b'),'plant-a',{'X-Contribution-Viewer':'stale'})).status,409);
  assert.equal((await body200(await f.get('/api/plants/catalogue',b))).plants.length,2);
  assert.ok(!(await (await f.get('/api/plants/plant/plant-a',b)).text()).includes('A private observation'));
  const r=await f.personal(a);assert.equal(r.headers.get('cache-control'),'private, no-store');assert.match(r.headers.get('vary'),/Cookie/);
});

test('notes are revision checked; only flagged corrections enter review, and edits become unread',async()=>{
  const f=fixture(),cookie=await f.bootstrap();
  await body200(await f.post(cookie,note('private')));await body200(await f.post(cookie,note('correction','Please check the leaf description',true)));
  let queue=await body200(await f.review());assert.equal(queue.Notes.length,1);assert.equal(queue.Notes[0].Note.Read,false);
  await body200(await f.review({Action:'read',Id:'correction',Revision:1}));
  assert.equal((await body200(await f.personal(cookie))).Notes.find(n=>n.Id==='correction').Read,true);
  await body200(await f.post(cookie,note('correction','Updated correction',true,1)));
  assert.equal((await f.review({Action:'read',Id:'correction',Revision:1})).status,409);
  assert.equal((await body200(await f.review())).Notes[0].Note.Read,false);
  await body200(await f.review({Action:'read',Id:'correction',Revision:2}));await body200(await f.review({Action:'unread',Id:'correction',Revision:2}));
  await body200(await f.post(cookie,note('correction','Keep private',false,2)));assert.equal((await body200(await f.review())).Notes.length,0);
  await body200(await f.post(cookie,{Action:'deleteNote',Id:'private',Revision:1}));
  assert.equal((await body200(await f.personal(cookie))).Notes.length,1);
});

test('uploads have private media gates, strip GPS metadata, and hero choices belong to one species and owner',async()=>{
  const f=fixture(),a=await f.bootstrap(),b=await f.bootstrap();
  const state=await body200(await f.upload(a));assert.equal(state.Photos.length,1);assert.equal(state.Photos[0].Width,300);
  const image=state.Photos[0].Image;
  assert.equal((await f.get(image,b)).status,404);assert.equal((await f.get(image,a)).status,200);assert.equal((await f.get(image,a)).headers.get('set-cookie'),null);
  assert.equal((await f.review(null,a,'')).status,403);
  assert.equal((await f.get(image,'',{'X-Admin-Key':f.env.ADMIN_KEY})).status,404); // private even from reviewers
  const stored=f.db.prepare('SELECT * FROM personal_plant_photos').get();
  assert.ok(!Buffer.from(f.objects.get(stored.image_key)).includes('GPS secret'));
  assert.equal((await f.get('/blobs/'+stored.image_key,a)).status,404);
  assert.equal((await f.post(b,{Action:'hero',Id:'photo-a',Revision:0})).status,400);
  assert.equal((await f.post(a,{Action:'hero',Id:'photo-a',Revision:0},'plant-b')).status,400);
  assert.equal((await body200(await f.post(a,{Action:'hero',Id:'photo-a',Revision:0}))).HeroPhotoId,'photo-a');
  assert.equal((await body200(await f.personal(b))).HeroPhotoId,'');
  assert.equal((await body200(await f.upload(a))).Photos.length,1);assert.equal(f.objects.size,2);
  await body200(await f.post(a,{Action:'hero',Id:'',Revision:0}));
  await body200(await f.post(a,{Action:'deletePhoto',Id:'photo-a',Revision:1}));assert.equal(f.objects.size,0);
});

test('photo offers need explicit bounded promotion; public copies survive removal of private copies',async()=>{
  const f=fixture(),a=await f.bootstrap();await body200(await f.upload(a));
  assert.equal((await f.review({Action:'promote',Id:'photo-a',Revision:1})).status,409);
  await body200(await f.post(a,photo('photo-a')));
  assert.equal((await body200(await f.review())).Photos.length,1);
  assert.equal((await f.get('/api/plants/personal-media/photo-a/image','',{'X-Admin-Key':f.env.ADMIN_KEY})).status,200);
  await body200(await f.post(a,photo('photo-a',2,false)));
  assert.equal((await f.review({Action:'promote',Id:'photo-a',Revision:2})).status,409);
  await body200(await f.post(a,photo('photo-a',3,true)));
  assert.equal((await f.review({Action:'promote',Id:'photo-a',Revision:2})).status,409);
  await body200(await f.review({Action:'promote',Id:'photo-a',Revision:4}));
  await body200(await f.review({Action:'promote',Id:'photo-a',Revision:4}));
  assert.equal(f.db.prepare('SELECT COUNT(*) n FROM plant_photos').get().n,1);assert.equal(f.objects.size,4);
  const state=await body200(await f.personal(a));assert.equal(state.Photos[0].PublicPhotoId,'contributed-photo-a');
  const published=f.db.prepare('SELECT * FROM plant_photos').get();assert.equal(published.caption,'Flower detail');
  assert.equal((await f.get(published.image,'')).status,200);
  assert.equal((await body200(await f.get('/api/plants/plant/plant-a',''))).plant.photos.length,1);
  await body200(await f.post(a,{Action:'deletePhoto',Id:'photo-a',Revision:4}));assert.equal(f.objects.size,2);
  assert.equal((await f.get(published.image,'')).status,200);
});

test('OAuth adopts anonymous content and preferences; claimed sessions cannot author abandoned guest rows',async()=>{
  const f=fixture(),anon=await f.bootstrap();
  await body200(await f.post(anon,note('note-a')));await body200(await f.upload(anon));await body200(await f.post(anon,{Action:'hero',Id:'photo-a',Revision:0}));
  const cookie=await f.login(anon),state=await body200(await f.personal(cookie));
  assert.equal(state.Anonymous,false);assert.equal(state.Notes.length,1);assert.equal(state.Photos.length,1);assert.equal(state.HeroPhotoId,'photo-a');
  const second=await f.bootstrap();await body200(await f.post(second,note('note-b')));await body200(await f.upload(second,'photo-b'));await body200(await f.post(second,{Action:'hero',Id:'photo-b',Revision:0}));
  const adopted=await f.login(second);const combined=await body200(await f.personal(adopted));
  assert.equal(combined.Notes.length,2);assert.equal(combined.Photos.length,2);assert.equal(combined.HeroPhotoId,'photo-a');
  assert.equal((await f.personal(second)).status,401);assert.equal(f.db.prepare("SELECT COUNT(*) n FROM plant_notes WHERE owner_provider='guest'").get().n,0);
});

test('requiring sign-in blocks legacy anonymous notebooks, writes and media, while login still claims their content',async()=>{
  const f=fixture(),anon=await f.bootstrap();await body200(await f.post(anon,note('note-a')));
  const uploaded=await body200(await f.upload(anon));
  const image=uploaded.Photos[0].Image;
  const token=await f.viewer(anon);
  for(const setting of ['true', undefined, '', 'mistyped']) {
    f.env.CONTRIBUTIONS_REQUIRE_LOGIN=setting;
    assert.equal((await f.personal(anon)).status,401);
    const headers={Cookie:anon,Origin:'http://plants.test','X-Contribution-Viewer':token};
    assert.equal((await f.call('/api/plants/personal/plant-a',{method:'POST',headers:{...headers,'Content-Type':'application/json'},body:JSON.stringify(note('blocked'))})).status,401);
    assert.equal((await f.call('/api/plants/personal/plant-a/photos/blocked',{method:'POST',headers})).status,401);
    assert.equal((await f.get(image,anon)).status,404);
    assert.equal((await body200(await f.get('/api/plants/catalogue',anon))).plants.length,2);
  }
  f.env.CONTRIBUTIONS_REQUIRE_LOGIN='true';
  const cookie=await f.login(anon),claimed=await body200(await f.personal(cookie));
  assert.equal(claimed.Anonymous,false);assert.equal(claimed.Notes.length,1);assert.equal(claimed.Photos.length,1);
  assert.equal((await f.get(image,cookie)).status,200);
  await body200(await f.post(cookie,note('verified-note')));
  await body200(await f.upload(cookie,'verified-photo'));
});

test('role grants are fresh, anonymous sessions never review, and generic CRUD cannot bypass private handlers',async()=>{
  const f=fixture(),anon=await f.bootstrap();assert.equal((await f.review(null,anon,'')).status,403);
  const cookie=await f.login(anon);assert.equal((await f.review(null,cookie,'')).status,403);
  f.db.prepare('INSERT INTO grants (id,provider,provider_user_id,role,enabled,created_at) VALUES (?,?,?,?,?,?)').run('curator','google','botanist','curator',1,1);
  await body200(await f.review(null,cookie,''));f.db.exec('UPDATE grants SET enabled=0');assert.equal((await f.review(null,cookie,'')).status,403);
  for(const type of ['PlantNote','PersonalPlantPhoto','PlantViewPreference','ContributionClaim','Guest']) assert.equal((await f.get('/api/admin/'+type,cookie,{'X-Admin-Key':f.env.ADMIN_KEY})).status,404,type);
  assert.equal((await f.get('/api/admin/Plant',cookie)).status,401);
  assert.equal((await f.get('/api/admin/Grant','',{'X-Admin-Key':f.env.ADMIN_KEY})).status,200);
});

test('bounded ingestion rejects malformed/oversized media and cleans up failed R2 uploads',async()=>{
  const f=fixture(),a=await f.bootstrap();
  assert.equal((await f.upload(a,'bad','plant-a',new TextEncoder().encode('<svg/>'))).status,400);
  assert.equal((await f.upload(a,'wide','plant-a',jpeg(3300,220))).status,400);
  assert.equal((await f.upload(a,'huge','plant-a',new Uint8Array(7*1024*1024))).status,400);
  assert.equal((await f.upload(a,'thumb','plant-a',jpeg(),jpeg(200,400))).status,400);
  f.failNextPut(2);assert.equal((await f.upload(a,'failed')).status,503);assert.equal(f.objects.size,0);assert.equal(f.db.prepare('SELECT COUNT(*) n FROM personal_plant_photos').get().n,0);
  await body200(await f.upload(a,'retry'));f.db.exec('UPDATE personal_plant_photos SET stored_bytes=262144000');assert.equal((await f.upload(a,'over-quota')).status,409);assert.equal(f.objects.size,2);
});

test('a claim racing a note insert is checked inside the write statement',async()=>{
  const f=fixture(),a=await f.bootstrap();await body200(await f.post(a,note('initial')));
  const guest=f.db.prepare('SELECT owner_id FROM plant_notes').get().owner_id;
  let raced=false;
  f.setBeforeRun(async(sql)=>{if(!raced&&sql.startsWith('INSERT OR IGNORE INTO plant_notes')){raced=true;await claim(f.env.DB,guest,'google','claimed')}});
  assert.equal((await f.post(a,note('late'))).status,409);
  assert.equal(f.db.prepare("SELECT COUNT(*) n FROM plant_notes WHERE owner_provider='guest'").get().n,0);
});

test('a withdrawn offer racing publication cannot create a public row or keep unused public objects',async()=>{
  const f=fixture(),a=await f.bootstrap();await body200(await f.upload(a));await body200(await f.post(a,photo('photo-a')));
  f.setBeforeRun(async(sql)=>{if(sql.startsWith('INSERT OR IGNORE INTO plant_photos'))f.db.exec('UPDATE personal_plant_photos SET offered=0,revision=revision+1')});
  assert.equal((await f.review({Action:'promote',Id:'photo-a',Revision:2})).status,409);
  assert.equal(f.db.prepare('SELECT COUNT(*) n FROM plant_photos').get().n,0);assert.equal(f.objects.size,2);
});

test('JPEG metadata stripping leaves scan data and rejects trailing payloads',()=>{
  const source=jpeg(),clean=cleanJpeg(source);assert.equal(clean.width,300);assert.equal(clean.height,220);assert.ok(clean.bytes.length<source.length);
  assert.throws(()=>cleanJpeg(new Uint8Array([...source,60,115,118,103,62])));
});

test('contribution migration matches generated schema without modifying existing catalogue records',()=>{
  const fresh=fixture().db,old=new DatabaseSync(':memory:');
  const objects=fresh.prepare("SELECT name,tbl_name,sql FROM sqlite_schema WHERE sql IS NOT NULL AND name NOT LIKE 'sqlite_%' ORDER BY CASE type WHEN 'table' THEN 0 ELSE 1 END,name").all();
  const added=new Set(['plant_notes','personal_plant_photos','plant_view_preferences','contribution_claims','grants','identification_responses']);
  for(const o of objects.filter(o=>!added.has(o.tbl_name)))old.exec(o.sql);
  old.exec(readFileSync(new URL('../migrations/0002_contributions.sql',import.meta.url),'utf8'));
  old.exec(readFileSync(new URL('../migrations/0003_field_notes.sql',import.meta.url),'utf8'));
  const shape=db=>db.prepare("SELECT name,sql,type FROM sqlite_schema WHERE sql IS NOT NULL AND name NOT LIKE 'sqlite_%' ORDER BY name").all().map(o=>o.type==='table'?{name:o.name,columns:db.prepare('PRAGMA table_info('+o.name+')').all(),keys:db.prepare('PRAGMA foreign_key_list('+o.name+')').all()} : o);
  assert.deepEqual(shape(old),shape(fresh));
});


test('five active notes per species and verified owner: edits and reviewed corrections retain slots; deletion frees one',async()=>{
  const f=fixture();f.env.CONTRIBUTIONS_REQUIRE_LOGIN='true';
  const a=await f.login(await f.bootstrap(),'botanist-a'),b=await f.login(await f.bootstrap(),'botanist-b');
  for(let i=0;i<5;i++)await body200(await f.post(a,note('limited-note-'+i,'Note '+i,i===0)));
  let own=await body200(await f.personal(a));assert.deepEqual(own.NoteCapacity,{Used:5,Limit:5});
  let denied=await f.post(a,note('sixth-note'));assert.equal(denied.status,409);assert.match((await denied.json()).error,/5 notes per species.*Delete/);
  await body200(await f.post(a,note('limited-note-1','Edited at the limit',false,1)));
  await body200(await f.review({Action:'read',Id:'limited-note-0',Revision:1}));
  assert.deepEqual((await body200(await f.personal(a))).NoteCapacity,{Used:5,Limit:5});
  await body200(await f.post(a,note('different-species'),'plant-b'));
  await body200(await f.post(b,note('different-owner')));
  assert.equal((await body200(await f.personal(b))).NoteCapacity.Used,1);
  await body200(await f.post(a,{Action:'deleteNote',Id:'limited-note-1',Revision:2}));
  assert.equal((await body200(await f.personal(a))).NoteCapacity.Used,4);
  await body200(await f.post(a,note('replacement-note')));
  // Later admin lifecycle actions can use the same deletion state; counts are not cached in the client.
  f.db.exec("UPDATE plant_notes SET deleted_at=42 WHERE id='limited-note-2'");
  assert.equal((await body200(await f.personal(a))).NoteCapacity.Used,4);
});

test('five personal photos per species: edits, offers, promotion and retries retain slots; deleting the private copy frees one',async()=>{
  const f=fixture();f.env.CONTRIBUTIONS_REQUIRE_LOGIN='true';
  const a=await f.login(await f.bootstrap(),'botanist-a'),b=await f.login(await f.bootstrap(),'botanist-b');
  for(let i=0;i<5;i++)await body200(await f.upload(a,'limited-photo-'+i));
  const denied=await f.upload(a,'sixth-photo');assert.equal(denied.status,409);assert.match((await denied.json()).error,/5 photos per species.*Delete/);
  assert.equal(f.objects.size,10);
  await body200(await f.upload(a,'limited-photo-0'));assert.equal(f.objects.size,10);
  await body200(await f.post(a,photo('limited-photo-0')));
  await body200(await f.review({Action:'promote',Id:'limited-photo-0',Revision:2}));
  assert.deepEqual((await body200(await f.personal(a))).PhotoCapacity,{Used:5,Limit:5});
  assert.equal((await f.upload(a,'still-full')).status,409);
  await body200(await f.upload(a,'different-plant','plant-b'));
  await body200(await f.upload(b,'different-owner'));
  await body200(await f.post(a,{Action:'deletePhoto',Id:'limited-photo-0',Revision:2}));
  const state=await body200(await f.personal(a));assert.equal(state.PhotoCapacity.Used,4);
  await body200(await f.upload(a,'replacement-photo'));
  assert.equal((await body200(await f.personal(a))).PhotoCapacity.Used,5);
  assert.equal(f.db.prepare("SELECT COUNT(*) n FROM plant_photos WHERE deleted_at IS NULL").get().n,1,'Deleting the private photo preserves its public copy');
});

test('competing note inserts cannot both take the fifth slot',async()=>{
  const f=fixture(),a=await f.bootstrap();
  for(let i=0;i<4;i++)await body200(await f.post(a,note('racing-note-'+i)));
  const responses=await Promise.all([f.post(a,note('race-a')),f.post(a,note('race-b'))]);
  assert.deepEqual(responses.map(r=>r.status).sort(),[200,409]);
  assert.equal((await body200(await f.personal(a))).NoteCapacity.Used,5);
});

test('an in-flight photo reserves the fifth slot before R2 writes complete',async()=>{
  const f=fixture(),a=await f.bootstrap();
  for(let i=0;i<4;i++)await body200(await f.upload(a,'ready-'+i));
  let release,started;
  const hold=new Promise(resolve=>release=resolve),reserved=new Promise(resolve=>started=resolve);
  let first=true;
  f.setBeforePut(async()=>{if(first){first=false;started();await hold}});
  const upload=f.upload(a,'in-flight');
  await reserved;
  try {
    const state=await body200(await f.personal(a));
    assert.equal(state.Photos.length,4);assert.deepEqual(state.PhotoCapacity,{Used:5,Limit:5});
    assert.equal((await f.upload(a,'too-many')).status,409);
    assert.equal(f.objects.size,8,'Rejected upload writes no R2 objects');
  } finally {release()}
  await body200(await upload);
  assert.equal((await body200(await f.personal(a))).Photos.length,5);
});

test('a failed upload releases its species slot even when cleanup fails; retained bytes still count against storage',async()=>{
  const f=fixture(),a=await f.bootstrap();
  for(let i=0;i<4;i++)await body200(await f.upload(a,'existing-'+i));
  f.failNextPut(2);f.failNextDelete(1);
  assert.equal((await f.upload(a,'failed-cleanup')).status,503);
  const failed=f.db.prepare("SELECT * FROM personal_plant_photos WHERE id='failed-cleanup'").get();
  assert.notEqual(failed.deleted_at,null);assert.ok(failed.stored_bytes>0);assert.equal(failed.ready,0);
  assert.equal((await body200(await f.personal(a))).PhotoCapacity.Used,4);
  await body200(await f.upload(a,'replacement'));
  assert.equal((await body200(await f.personal(a))).PhotoCapacity.Used,5);
  f.db.exec("UPDATE personal_plant_photos SET stored_bytes=262144000 WHERE id='failed-cleanup'");
  assert.equal((await f.upload(a,'storage-full','plant-b')).status,409);
});

test('legacy account adoption preserves over-limit items but prevents additions until the owner makes room',async()=>{
  const f=fixture();
  const first=await f.bootstrap();
  for(let i=0;i<3;i++){await body200(await f.post(first,note('first-'+i)));await body200(await f.upload(first,'first-photo-'+i))}
  await f.login(first);
  const second=await f.bootstrap();
  for(let i=0;i<3;i++){await body200(await f.post(second,note('second-'+i)));await body200(await f.upload(second,'second-photo-'+i))}
  const cookie=await f.login(second);f.env.CONTRIBUTIONS_REQUIRE_LOGIN='true';
  const state=await body200(await f.personal(cookie));assert.equal(state.Notes.length,6);assert.equal(state.Photos.length,6);
  assert.deepEqual(state.NoteCapacity,{Used:6,Limit:5});assert.deepEqual(state.PhotoCapacity,{Used:6,Limit:5});
  assert.equal((await f.post(cookie,note('over-limit'))).status,409);assert.equal((await f.upload(cookie,'over-limit')).status,409);
  await body200(await f.post(cookie,note('first-0','Editable at six',false,1)));
  for(const id of ['first-1','first-2'])await body200(await f.post(cookie,{Action:'deleteNote',Id:id,Revision:1}));
  for(const id of ['first-photo-1','first-photo-2'])await body200(await f.post(cookie,{Action:'deletePhoto',Id:id,Revision:1}));
  await body200(await f.post(cookie,note('new-note')));await body200(await f.upload(cookie,'new-photo'));
  const final=await body200(await f.personal(cookie));assert.equal(final.NoteCapacity.Used,5);assert.equal(final.PhotoCapacity.Used,5);
});


test('a failed capacity response cannot remove a completed upload; retrying its ID returns the same photo',async()=>{
  const f=fixture(),a=await f.bootstrap();let fail=true;
  f.setBeforeFirst(async sql=>{
    if(fail && sql.includes('AS notes') && f.db.prepare("SELECT 1 FROM personal_plant_photos WHERE id='completed' AND ready=1").get()){
      fail=false;throw Error('Temporary read failure');
    }
  });
  assert.equal((await f.upload(a,'completed')).status,503);
  assert.equal(f.objects.size,2);
  const row=f.db.prepare("SELECT * FROM personal_plant_photos WHERE id='completed'").get();
  assert.equal(row.ready,1);assert.equal(row.deleted_at,null);
  const retry=await body200(await f.upload(a,'completed'));
  assert.equal(retry.Photos.length,1);assert.deepEqual(retry.PhotoCapacity,{Used:1,Limit:5});assert.equal(f.objects.size,2);
});


const v2='/api/plants/v2';
async function v2post(f,cookie,path,data,extra={}) {
  return f.call(v2+path,{method:'POST',headers:{Cookie:cookie,Origin:'http://plants.test','Content-Type':'application/json','X-Contribution-Viewer':await f.viewer(cookie),...extra},body:JSON.stringify(data)});
}
test('v2 codecs and v1 compatibility adapter share revisions, quotas, privacy and review state',async()=>{
  const f=fixture(),cookie=await f.bootstrap(),other=await f.bootstrap();
  const saved=await body200(await v2post(f,cookie,'/notes/save',{plantId:'plant-a',id:'typed-note',revision:0,text:'A typed correction',correction:true}));
  assert.equal(saved.personal.notes[0].text,'A typed correction');assert.equal(saved.personal.noteCapacity.used,1);
  assert.equal((await body200(await f.personal(cookie))).Notes[0].Text,'A typed correction');
  assert.equal((await body200(await f.get(v2+'/personal/plant-a',other))).personal.notes.length,0);
  assert.equal((await v2post(f,other,'/notes/delete',{plantId:'plant-a',id:'typed-note',revision:1})).status,409);
  assert.equal((await v2post(f,cookie,'/notes/delete',{plantId:'plant-b',id:'typed-note',revision:1})).status,409);
  assert.equal((await v2post(f,cookie,'/notes/save',{plantId:'plant-a',id:'typed-note',revision:0,text:'stale',correction:false})).status,409);
  const queue=await body200(await f.get(v2+'/review?page=0','',{'X-Admin-Key':f.env.ADMIN_KEY}));
  assert.equal(queue.review.notes[0].note.text,'A typed correction');
  await body200(await v2post(f,cookie,'/review/correction',{id:'typed-note',revision:1,read:true,page:0},{'X-Admin-Key':f.env.ADMIN_KEY}));
  assert.equal((await body200(await f.personal(cookie))).Notes[0].Read,true);
  await body200(await f.post(cookie,note('typed-note','Legacy edit',true,1)));
  assert.equal((await body200(await f.get(v2+'/personal/plant-a',cookie))).personal.notes[0].read,false);
  for(let i=0;i<4;i++)await body200(await v2post(f,cookie,'/notes/save',{plantId:'plant-a',id:'extra-'+i,revision:0,text:'A note',correction:false}));
  assert.equal((await v2post(f,cookie,'/notes/save',{plantId:'plant-a',id:'too-many',revision:0,text:'Extra',correction:false})).status,409);
  await body200(await v2post(f,cookie,'/notes/delete',{plantId:'plant-a',id:'typed-note',revision:2}));
  assert.equal((await body200(await f.review())).Notes.length,0);
});

test('v2 photo edits, personal hero, offers, promotion and deletion preserve private/public copies',async()=>{
  const f=fixture(),cookie=await f.bootstrap();await body200(await f.upload(cookie));
  await body200(await v2post(f,cookie,'/photos/update',{plantId:'plant-a',id:'photo-a',revision:1,caption:'Flowers',photographer:'Botanist',offered:true}));
  const selected=await body200(await v2post(f,cookie,'/hero',{plantId:'plant-a',id:'photo-a'}));
  assert.equal(selected.personal.heroPhotoId,'photo-a');
  await body200(await v2post(f,cookie,'/review/promote',{id:'photo-a',revision:2,page:0},{'X-Admin-Key':f.env.ADMIN_KEY}));
  const pub=f.db.prepare('SELECT * FROM plant_photos WHERE id=?').get('contributed-photo-a');assert.equal(pub.caption,'Flowers');
  await body200(await v2post(f,cookie,'/photos/delete',{plantId:'plant-a',id:'photo-a',revision:2}));
  assert.ok(f.objects.has(pub.image.slice('/blobs/'.length)));
  const data=await body200(await f.get(v2+'/personal/plant-a',cookie));
  assert.equal(data.personal.photos.length,0);assert.equal(data.personal.heroPhotoId,'');
  assert.equal(f.db.prepare('SELECT COUNT(*) AS n FROM plant_photos').get().n,1);
});

test('v2 auth, origin, viewer, decoder and byte bounds reject writes without changing data',async()=>{
  const f=fixture(),cookie=await f.bootstrap();
  const payload={plantId:'plant-a',id:'note-a',revision:0,text:'Valid',correction:false};
  assert.equal((await v2post(f,cookie,'/notes/save',payload,{Origin:'https://attacker.test'})).status,403);
  assert.equal((await v2post(f,cookie,'/notes/save',payload,{'X-Contribution-Viewer':'old'})).status,409);
  for(const value of [{...payload,correction:'false'},{...payload,revision:1.5},{...payload,revision:-1},{...payload,text:'x'.repeat(6001)},{...payload,text:'x'.repeat(24001)}])
    assert.equal((await v2post(f,cookie,'/notes/save',value)).status,400);
  assert.equal((await v2post(f,cookie,'/notes/save',payload,{'Content-Type':'text/plain'})).status,400);
  assert.equal((await f.get(v2+'/personal/plant-a','')).status,401);
  assert.equal((await f.get(v2+'/review',cookie)).status,403);
  assert.equal((await v2post(f,cookie,'/review/promote',{id:'photo-a',revision:1,page:0})).status,403);
  assert.equal((await body200(await f.personal(cookie))).Notes.length,0);
  const capabilities=await body200(await f.get(v2+'/access',cookie));
  assert.deepEqual(capabilities,{canEditCatalogue:false,canReview:false,canIdentify:false});
  const invalidPage=await body200(await f.get(v2+'/review?page=oops','',{'X-Admin-Key':f.env.ADMIN_KEY}));
  assert.equal(invalidPage.review.page,0);
});

test('v2 denies untrusted callers before consuming a streamed body; authorized bodies are capped without Content-Length',async()=>{
  const f=fixture(),cookie=await f.bootstrap();
  let reads=0;
  const body=new ReadableStream({pull(controller){reads++;controller.enqueue(new Uint8Array(30000));}}, {highWaterMark:0});
  const denied=await f.call(v2+'/notes/save',{method:'POST',headers:{Origin:'http://plants.test','Content-Type':'application/json'},body,duplex:'half'});
  assert.equal(denied.status,401);assert.equal(reads,0);await body.cancel();
  let cancelled=false;
  const large=new ReadableStream({pull(c){c.enqueue(new Uint8Array(24001));},cancel(){cancelled=true}}, {highWaterMark:0});
  const rejected=await f.call(v2+'/notes/save',{method:'POST',headers:{Cookie:cookie,Origin:'http://plants.test','Content-Type':'application/json'},body:large,duplex:'half'});
  assert.equal(rejected.status,400);assert.equal(cancelled,true);
});

test('generated v2 client roundtrips actual worker data and preserves HTTP versus decode failures',async()=>{
  const {createClient}=await import('../dist/client/generated/ClientGen.js');
  const {FSharpResult$2:Result}=await import('../dist/client/fable_modules/fable-library-js.4.29.0/Result.js');
  const f=fixture(),cookie=await f.bootstrap(),viewer=await f.viewer(cookie);
  const transport=async req=>{
    const headers={Cookie:cookie,Origin:'http://plants.test','Content-Type':'application/json','X-Contribution-Viewer':viewer};
    const qs=new URLSearchParams([...req.Query]);
    const response=await f.call(req.Path+(qs.size?'?'+qs:''),{method:req.Method,headers,body:req.Body});
    return new Result(0,[{Status:response.status,Body:await response.text(),Headers:[]}]);
  };
  const client=createClient(transport);
  const saved=await client.saveNote({PlantId:'plant-a',Id:'typed',Revision:0,Text:'Decoded',Correction:true});
  assert.equal(saved.tag,0);assert.equal([...saved.fields[0].Personal.Notes][0].Text,'Decoded');
  const read=await client.getPersonal('plant-a');assert.equal(read.tag,0);assert.equal([...read.fields[0].Personal.Notes].length,1);
  const denied=await client.getReview({Page:'0'});assert.equal(denied.tag,1);assert.equal(denied.fields[0].tag,1);assert.equal(denied.fields[0].fields[0],403);
  const broken=createClient(async()=>new Result(0,[{Status:200,Body:'{"personal":{"notes":"bad"}}',Headers:[]}]))
  const malformed=await broken.getPersonal('plant-a');assert.equal(malformed.tag,1);assert.equal(malformed.fields[0].tag,3);
});


test('v2 enforces credential-backed contributors and current grants independently from owner keys',async()=>{
  const f=fixture();f.env.CONTRIBUTIONS_REQUIRE_LOGIN='true';
  const anon=await f.bootstrap();assert.equal((await f.get(v2+'/personal/plant-a',anon)).status,401);
  const cookie=await f.login(anon);
  await body200(await v2post(f,cookie,'/notes/save',{plantId:'plant-a',id:'verified',revision:0,text:'Check this',correction:true}));
  assert.equal((await f.get(v2+'/review',cookie)).status,403);
  f.db.prepare('INSERT INTO grants (id,provider,provider_user_id,role,enabled,created_at) VALUES (?,?,?,?,?,?)').run('curator-v2','google','botanist','curator',1,1);
  const capabilities=await body200(await f.get(v2+'/access',cookie));assert.deepEqual(capabilities,{canEditCatalogue:false,canReview:true,canIdentify:false});
  assert.equal((await body200(await f.get(v2+'/review',cookie))).review.notes.length,1);
  assert.equal((await f.get('/api/admin/types',cookie)).status,401);
  f.db.prepare('UPDATE grants SET enabled=0 WHERE id=?').run('curator-v2');
  assert.equal((await f.get(v2+'/review',cookie)).status,403);
  assert.equal((await v2post(f,cookie,'/review/correction',{id:'verified',revision:1,read:true,page:0})).status,403);
  const owner=await body200(await f.get(v2+'/access','',{'X-Admin-Key':f.env.ADMIN_KEY}));assert.deepEqual(owner,{canEditCatalogue:true,canReview:true,canIdentify:true});
});

test('Native Plants explicitly registers only catalogue, grants and read-only identity descriptors',async()=>{
  const f=fixture(),headers={'X-Admin-Key':f.env.ADMIN_KEY};
  const {tables}=await import('../dist/server/generated/AdminGen.js');
  const {adminConfig}=await import('../dist/server/AdminConfig.js');
  // Changing the generated aggregate cannot register a table in the authored host list.
  const before=[...adminConfig.Tables].map(t=>t.Name);tables.head={...tables.head,Name:'FuturePrivateTable'};
  const data=await body200(await f.get('/api/admin/types','',headers));
  assert.deepEqual(data.types.map(t=>t.name).sort(),['GlossaryTerm','Grant','Identity','Plant','PlantMap','PlantPhoto','SourceReference'].sort());
  assert.deepEqual([...adminConfig.Tables].map(t=>t.Name),before);
  for(const name of ['Guest','PlantNote','PersonalPlantPhoto','PlantViewPreference','ContributionClaim','FuturePrivateTable'])
    assert.equal((await f.get('/api/admin/'+name,'',headers)).status,404);
});


test('v2 success and malformed requests preserve session renewal and private cache headers',async()=>{
  const {deps}=await import('../dist/server/AuthConfig.js');
  const {issue,verify}=await import('../dist/server/packages/hedge/src/Hedge/GuestCookie.js');
  const f=fixture(),initial=await f.bootstrap();
  const config=deps(f.env,new Request('http://plants.test/')).Config;
  const verified=await verify(config,Math.floor(Date.now()/1000),initial.slice('hedge_guest='.length));
  assert.equal(verified.tag,0);const guest=verified.fields[0].GuestId;
  const token=await issue(config,Math.floor(Date.now()/1000)-86400,86460,guest);
  const cookie='hedge_guest='+token;
  for(const response of [await f.get(v2+'/personal/plant-a',cookie),await f.get(v2+'/access',cookie),
    await v2post(f,cookie,'/notes/save',{bad:'body'})]) {
    assert.ok([200,400].includes(response.status),await response.clone().text());
    assert.match(response.headers.get('set-cookie'),/^hedge_guest=/);
    assert.equal(response.headers.get('cache-control'),'private, no-store');
    assert.match(response.headers.get('vary'),/Cookie/);
  }
  f.setBeforeRun(()=>{throw new Error('database fixture unavailable')});
  const failed=await v2post(f,cookie,'/notes/save',{plantId:'plant-a',id:'failed',revision:0,text:'Failure',correction:false});
  assert.equal(failed.status,503);assert.equal(failed.headers.get('cache-control'),'private, no-store');
  assert.doesNotMatch(await failed.text(),/database fixture/);
});


// Field-note purposes share the same owner quotas but have separate review audiences.
const entry=(id,purpose='private',photoIds=[],revision=0,text='Field observation')=>({plantId:'plant-a',id,revision,text,purpose,photoIds});
const grant=(f,account,role)=>f.db.prepare('INSERT INTO grants (id,provider,provider_user_id,role,enabled,created_at) VALUES (?,?,?,?,1,1)').run(account+'-'+role,'google',account,role);
const fieldSave=(f,cookie,data)=>v2post(f,cookie,'/notes/entry',data);
const fieldQueue=(f,cookie='',key='')=>f.get(v2+'/review',cookie,{'X-Admin-Key':key});
const identify=(f,cookie,id,revision=1,outcome='confirmed',extra={})=>v2post(f,cookie,'/review/identify',{id,revision,outcome,text:'Reviewed by me',alternativePlantId:'',page:0,...extra});

test('field-note photo requirements, bounded associations and shared quota are enforced by the worker',async()=>{
  const f=fixture(),a=await f.login(await f.bootstrap(),'author'),b=await f.login(await f.bootstrap(),'other');
  await body200(await f.upload(a,'own'));await body200(await f.upload(a,'other-plant','plant-b'));await body200(await f.upload(b,'other-owner'));
  for(const data of [
    entry('private-photo','private',['own']),entry('empty-id','identification'),entry('bad-purpose','oops'),
    entry('duplicate','correction',['own','own']),entry('too-many','correction',['1','2','3','4','5','6'])
  ])assert.equal((await fieldSave(f,a,data)).status,400,data.id);
  for(const id of ['missing','other-owner','other-plant'])
    assert.equal((await fieldSave(f,a,entry('invalid-link','identification',[id]))).status,409,id);
  const saved=await body200(await fieldSave(f,a,entry('request','identification',['own'])));
  assert.equal(saved.personal.notes[0].photos[0].id,'own');assert.equal(saved.personal.notes[0].photos[0].offered,false);
  assert.deepEqual(saved.personal.photoCapacity,{used:1,limit:5});
  await body200(await fieldSave(f,a,entry('private')));
  await body200(await fieldSave(f,a,entry('correction','correction')));
  await body200(await fieldSave(f,a,entry('shared-photo','correction',['own'])));
  await body200(await fieldSave(f,a,entry('fifth')));
  assert.equal((await fieldSave(f,a,entry('sixth','correction'))).status,409);
  // Linking the same uploaded image twice consumes one photo slot.
  const data=await body200(await f.get(v2+'/personal/plant-a',a));
  assert.equal(data.personal.noteCapacity.used,5);assert.equal(data.personal.photoCapacity.used,1);
  // Old clients may read entries but cannot erase new-purpose data.
  assert.equal((await f.post(a,note('request','Old client',false,1))).status,409);
  assert.equal((await v2post(f,a,'/notes/save',{plantId:'plant-a',id:'shared-photo',revision:1,text:'Old client',correction:false})).status,409);
});

test('curator and identifier queues/media are isolated; withdrawal and grant revocation remove access',async()=>{
  const f=fixture();f.env.CONTRIBUTIONS_REQUIRE_LOGIN='true';
  const a=await f.login(await f.bootstrap(),'author'),c=await f.login(await f.bootstrap(),'curator'),i=await f.login(await f.bootstrap(),'identifier');
  grant(f,'curator','curator');grant(f,'identifier','identifier');
  for(const id of ['correction-photo','id-photo','private-photo','offered-photo'])await body200(await f.upload(a,id));
  await body200(await f.post(a,photo('offered-photo')));
  await body200(await fieldSave(f,a,entry('correction','correction',['correction-photo'])));
  await body200(await fieldSave(f,a,entry('id','identification',['id-photo'])));
  await body200(await fieldSave(f,a,entry('private')));
  const cq=await body200(await fieldQueue(f,c)),iq=await body200(await fieldQueue(f,i));
  assert.deepEqual(cq.review.notes.map(n=>n.note.id),['correction']);assert.equal(cq.review.photos.length,1);
  assert.deepEqual(iq.review.notes.map(n=>n.note.id),['id']);assert.equal(iq.review.photos.length,0);
  assert.deepEqual(await body200(await f.get(v2+'/access',i)),{canEditCatalogue:false,canReview:false,canIdentify:true});
  for(const [cookie,allowed] of [[c,['correction-photo','offered-photo']],[i,['id-photo']]]) {
    for(const id of ['correction-photo','id-photo','private-photo','offered-photo']){
      const r=await f.get('/api/plants/personal-media/'+id+'/image',cookie);
      assert.equal(r.status,allowed.includes(id)?200:404,id);
      assert.equal(r.headers.get('set-cookie'),null);
    }
  }
  assert.equal((await identify(f,c,'id')).status,403);
  assert.equal((await v2post(f,i,'/review/correction',{id:'correction',revision:1,read:true,page:0})).status,403);
  assert.equal((await v2post(f,i,'/review/promote',{id:'offered-photo',revision:2,page:0})).status,403);
  // Generic admin remains unavailable to identifiers and hides private tables from owners.
  assert.equal((await f.get('/api/admin/types',i)).status,401);
  assert.equal((await f.get('/api/admin/IdentificationResponse','',{'X-Admin-Key':f.env.ADMIN_KEY})).status,404);
  await body200(await fieldSave(f,a,entry('id','private',[],1)));
  assert.equal((await body200(await fieldQueue(f,i))).review.notes.length,0);
  assert.equal((await f.get('/api/plants/personal-media/id-photo/image',i)).status,404);
  f.db.exec("UPDATE grants SET enabled=0 WHERE role='curator'");
  assert.equal((await fieldQueue(f,c)).status,403);
  assert.equal((await f.get('/api/plants/personal-media/correction-photo/image',c)).status,404);
  assert.equal((await f.get('/api/plants/personal-media/private-photo/image','',{'X-Admin-Key':f.env.ADMIN_KEY})).status,404);
});

test('ID responses preserve author text, attribution and revision history without publishing or moving images',async()=>{
  const f=fixture(),a=await f.login(await f.bootstrap(),'author'),i=await f.login(await f.bootstrap(),'identifier');
  grant(f,'identifier','identifier');
  await body200(await f.upload(a,'evidence'));
  await body200(await fieldSave(f,a,entry('id','identification',['evidence'],0,'Original question')));
  assert.equal((await identify(f,i,'id',1,'alternative',{alternativePlantId:'plant-a'})).status,409);
  assert.equal((await identify(f,i,'id',1,'alternative')).status,400);
  await body200(await identify(f,i,'id',1,'alternative',{alternativePlantId:'plant-b',text:'The leaves suggest this species'}));
  const own=await body200(await f.get(v2+'/personal/plant-a',a));const n=own.personal.notes[0],r=n.responses[0];
  assert.equal(n.text,'Original question');assert.equal(r.submittedText,n.text);assert.equal(r.revision,1);
  assert.equal(r.outcome,'alternative');assert.equal(r.alternativePlantId,'plant-b');assert.equal(r.reviewerName,'Botanist');
  assert.ok(!JSON.stringify(own).includes('reviewerProvider'));assert.ok(!JSON.stringify(own).includes('reviewerId'));
  assert.equal(f.db.prepare('SELECT reviewer_provider,reviewer_id FROM identification_responses').get().reviewer_id,'identifier');
  assert.equal((await identify(f,i,'id')).status,409);
  assert.equal(f.db.prepare('SELECT COUNT(*) n FROM plant_photos').get().n,0);
  assert.equal(f.db.prepare("SELECT plant_id,offered FROM personal_plant_photos WHERE id='evidence'").get().plant_id,'plant-a');
  assert.equal(f.db.prepare("SELECT offered FROM personal_plant_photos WHERE id='evidence'").get().offered,0);
  await body200(await fieldSave(f,a,entry('id','identification',['evidence'],1,'Updated question')));
  let q=await body200(await fieldQueue(f,i));assert.equal(q.review.notes[0].note.revision,2);assert.equal(q.review.notes[0].note.responses[0].revision,1);
  assert.equal((await identify(f,i,'id',1,'rejected')).status,409);
  await body200(await identify(f,i,'id',2,'unknown'));
  await body200(await fieldSave(f,a,entry('id','identification',['evidence'],2)));
  await body200(await identify(f,i,'id',3,'rejected'));
  await body200(await fieldSave(f,a,entry('id','identification',['evidence'],3)));
  await body200(await identify(f,i,'id',4,'confirmed'));
  const revised=await body200(await f.get(v2+'/personal/plant-a',a));
  assert.deepEqual(revised.personal.notes[0].responses.map(r=>r.outcome),['confirmed','rejected','unknown','alternative']);
  assert.equal(revised.personal.noteCapacity.used,1);
  // Converting to a correction shares only that revision's text/photos, not the ID conversation.
  await body200(await fieldSave(f,a,entry('id','correction',['evidence'],4)));
  const curator=await f.login(await f.bootstrap(),'curator');grant(f,'curator','curator');
  assert.equal((await body200(await fieldQueue(f,curator))).review.notes[0].note.responses.length,0);
  await body200(await fieldSave(f,a,entry('id','private',[],5)));
  assert.equal((await identify(f,i,'id',6)).status,409);
  assert.equal((await body200(await f.get(v2+'/personal/plant-a',a))).personal.notes[0].responses.length,4);
});

test('linking, deleting and responding recheck invariants inside their write statements',async()=>{
  const f=fixture(),a=await f.login(await f.bootstrap(),'author'),i=await f.login(await f.bootstrap(),'identifier');grant(f,'identifier','identifier');
  await body200(await f.upload(a,'evidence'));
  f.setBeforeRun(sql=>{if(sql.startsWith('INSERT OR IGNORE INTO plant_notes')){f.setBeforeRun(null);f.db.exec("UPDATE personal_plant_photos SET deleted_at=42 WHERE id='evidence'")}});
  assert.equal((await fieldSave(f,a,entry('id','identification',['evidence']))).status,409);
  assert.equal(f.db.prepare('SELECT COUNT(*) n FROM plant_notes').get().n,0);
  f.db.exec("UPDATE personal_plant_photos SET deleted_at=NULL WHERE id='evidence'");
  // A competing link wins before the photo deletion statement, which must now fail.
  f.setBeforeRun(sql=>{if(sql.startsWith('UPDATE personal_plant_photos SET deleted_at=')){f.setBeforeRun(null);f.db.exec("INSERT INTO plant_notes (id,plant_id,owner_provider,owner_id,text,is_correction,revision,created_at,purpose,photo_ids) VALUES ('id','plant-a','google','author','Question',0,1,1,'identification','[\"evidence\"]')")}});
  assert.equal((await f.post(a,{Action:'deletePhoto',Id:'evidence',Revision:1})).status,409);
  assert.equal(f.db.prepare("SELECT deleted_at FROM personal_plant_photos WHERE id='evidence'").get().deleted_at,null);
  const blocked=await f.post(a,{Action:'deletePhoto',Id:'evidence',Revision:1});
  assert.equal(blocked.status,400);assert.match(await blocked.text(),/Unlink/);
  f.setBeforeRun(sql=>{if(sql.startsWith('INSERT OR IGNORE INTO identification_responses')){f.setBeforeRun(null);f.db.exec("UPDATE plant_notes SET purpose='private',photo_ids='[]',revision=2 WHERE id='id'")}});
  assert.equal((await identify(f,i,'id')).status,409);
  assert.equal(f.db.prepare('SELECT COUNT(*) n FROM identification_responses').get().n,0);
  await body200(await f.post(a,{Action:'deletePhoto',Id:'evidence',Revision:1}));
  assert.equal((await body200(await f.personal(a))).PhotoCapacity.Used,0);
});

test('0003 preserves legacy notes, correction flags, photographs and catalogue records',async()=>{
  const f=fixture(),a=await f.bootstrap();
  await body200(await f.post(a,note('private','Legacy private text')));
  await body200(await f.post(a,note('correction','Legacy correction text',true)));
  await body200(await f.upload(a,'legacy-photo'));
  f.db.exec('DROP TABLE identification_responses; ALTER TABLE plant_notes DROP COLUMN purpose; ALTER TABLE plant_notes DROP COLUMN photo_ids;');
  const before=f.db.prepare('SELECT * FROM plant_notes ORDER BY id').all();
  const plants=f.db.prepare('SELECT * FROM plants ORDER BY id').all();
  const photos=f.db.prepare('SELECT * FROM personal_plant_photos').all();
  f.db.exec(readFileSync(new URL('../migrations/0003_field_notes.sql',import.meta.url),'utf8'));
  assert.deepEqual(f.db.prepare('SELECT * FROM plant_notes ORDER BY id').all().map(({purpose,photo_ids,...old})=>old),before.map(r=>({...r})));
  assert.deepEqual(f.db.prepare('SELECT * FROM plants ORDER BY id').all(),plants);
  assert.deepEqual(f.db.prepare('SELECT * FROM personal_plant_photos').all(),photos);
  const data=await body200(await f.get(v2+'/personal/plant-a',a));
  assert.deepEqual(data.personal.notes.map(n=>[n.id,n.purpose]).sort(),[['correction','correction'],['private','private']]);
  assert.ok(data.personal.notes.every(n=>n.photos.length===0 && n.responses.length===0));
});
