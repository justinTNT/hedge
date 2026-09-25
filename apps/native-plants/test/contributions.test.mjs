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
  const added=new Set(['plant_notes','personal_plant_photos','plant_view_preferences','contribution_claims','grants']);
  for(const o of objects.filter(o=>!added.has(o.tbl_name)))old.exec(o.sql);
  old.exec(readFileSync(new URL('../migrations/0002_contributions.sql',import.meta.url),'utf8'));
  const shape=db=>db.prepare("SELECT name,sql FROM sqlite_schema WHERE sql IS NOT NULL AND name NOT LIKE 'sqlite_%' ORDER BY name").all();
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
