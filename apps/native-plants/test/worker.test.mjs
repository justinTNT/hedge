import { test } from 'node:test';
import assert from 'node:assert/strict';
import { DatabaseSync } from 'node:sqlite';
import { readFileSync } from 'node:fs';
import worker from '../dist/server/Worker.js';

const schema=readFileSync(new URL('../schema.sql',import.meta.url),'utf8');
const textFields=['scientificName','commonNames','family','genus','aliases','forms','height','sun','water','gardenFeatures','wildlife','habit','bark','leaves','phyllodes','flowers','fruit','flowering','fruiting','features','habitat','cultivation','traditionalUses','notes','distribution','sourceReferences','sourceEvidence'];
const draft={...Object.fromEntries(textFields.map(f=>[f,''])),slug:'test-plant',scientificName:'Acacia example',commonNames:'Example wattle',family:'Fabaceae',genus:'Acacia',forms:'Tree|Shrub',sun:'Full sun',water:'Little water',sourceEvidence:'PRIVATE SOURCE PATH',published:true,endemicNt:false,sortOrder:1};
function fixture() {
  const db=new DatabaseSync(':memory:');db.exec('PRAGMA foreign_keys=ON');db.exec(schema);
  let batches=0;
  const statement=(sql,values=[])=>({
    bind(...args){return statement(sql,args)},
    async all(){return {results:db.prepare(sql).all(...values),success:true,meta:{}}},
    async first(){return db.prepare(sql).get(...values)},
    async run(){const s=db.prepare(sql);if(s.columns().length)return {results:s.all(...values),success:true,meta:{}};const r=s.run(...values);return {results:[],success:true,meta:{changes:r.changes}}}
  });
  const env={DB:{prepare:statement,async batch(stmts){batches++;db.exec('BEGIN');try{const r=await Promise.all(stmts.map(s=>s.run()));db.exec('COMMIT');return r}catch(e){db.exec('ROLLBACK');throw e}}},ADMIN_KEY:'fixture-owner',BLOBS:{},ASSETS:{async fetch(){return new Response('Not found',{status:404})}}};
  async function call(path,{method='GET',body,key}={}){
    const headers=key?{'X-Admin-Key':key}:{};if(body){headers['Content-Type']='application/json';body=JSON.stringify(body)}
    return worker.fetch(new Request('http://plants.test'+path,{method,headers,body}),env,{waitUntil(){}});
  }
  const admin=(path,options={})=>call('/api/admin/'+path,{key:env.ADMIN_KEY,...options});
  const create=async (fields={})=>(await (await admin('Plant',{method:'POST',body:{...draft,...fields}})).json()).record;
  return {db,env,call,admin,create,batches:()=>batches};
}

test('owner admin remains gated, including with an empty configured key',async()=>{
  const f=fixture();
  for(const path of ['/api/admin/types','/api/admin/Plant','/api/admin/Plant/x'])assert.equal((await f.call(path)).status,401);
  assert.equal((await f.call('/api/admin/Plant',{method:'POST',body:draft})).status,401);
  f.env.ADMIN_KEY='';assert.equal((await f.call('/api/admin/Plant')).status,401);
});

test('catalogue uses one bulk load, then searches and details without D1 queries',async()=>{
  const f=fixture();const p=await f.create();assert.ok(p.id);
  const first=await f.call('/api/plants/catalogue');assert.equal(first.status,200);assert.equal(first.headers.get('cache-control'),'no-store');
  const data=await first.json();assert.equal(data.plants.length,1);assert.equal(data.plants[0].scientificName,draft.scientificName);
  assert.equal(f.batches(),1);
  for(let i=0;i<8;i++)assert.equal((await f.call('/api/plants/search?q=wattle')).status,200);
  assert.equal((await f.call('/api/plants/plant/'+p.id)).status,200);assert.equal(f.batches(),1);
  assert.ok(!JSON.stringify(data).includes('PRIVATE SOURCE PATH'));
});

test('public cache immediately reflects owner edits and unpublishing in its isolate',async()=>{
  const f=fixture();const p=await f.create();await f.call('/api/plants/catalogue');
  await f.admin('Plant/'+p.id,{method:'PUT',body:{...p,scientificName:'Acacia renamed'}});
  assert.equal((await (await f.call('/api/plants/plant/'+p.id)).json()).plant.card.scientificName,'Acacia renamed');
  await f.admin('Plant/'+p.id,{method:'PUT',body:{...p,published:false}});
  assert.equal((await f.call('/api/plants/plant/'+p.id)).status,404);
  assert.equal((await (await f.call('/api/plants/catalogue')).json()).plants.length,0);
});

test('filters combine OR within facets, AND across facets; missing attributes are unknown',async()=>{
  const f=fixture();await f.create();await f.create({slug:'other',scientificName:'Other plant',family:'Otheraceae',genus:'Other',forms:'Herb',sun:'',endemicNt:true});
  const query=async q=>(await (await f.call('/api/plants/search?'+q)).json()).plants;
  assert.equal((await query('form=Tree%7CHerb')).length,2);
  assert.equal((await query('form=Tree%7CHerb&sun=Full%20sun')).length,1);
  assert.equal((await query('family=Fabaceae&q=wattle')).length,1);
  assert.equal((await query('endemic=true')).length,1);
  assert.equal((await query('photos=true')).length,0);
});

test('explicit former names are searchable, and exact name outranks broader matches',async()=>{
  const f=fixture();const first=await f.create({aliases:'Former name'});await f.create({slug:'other',scientificName:'Former name tree'});
  const data=await (await f.call('/api/plants/search?q=Former%20name')).json();
  assert.equal(data.plants[0].id,first.id);assert.equal(data.plants.length,2);
});

test('revision is stable across reloads and changes for account-body edits',async()=>{
  const f=fixture();const p=await f.create();const first=await (await f.call('/api/plants/revision')).json();
  const realNow=Date.now;Date.now=()=>realNow()+16000;
  try{assert.deepEqual(await (await f.call('/api/plants/revision')).json(),first)}finally{Date.now=realNow}
  await f.admin('Plant/'+p.id,{method:'PUT',body:{...p,habit:'Updated diagnostic detail'}});
  assert.notEqual((await (await f.call('/api/plants/revision')).json()).revision,first.revision);
});

test('draft/deleted photos and parent records do not enter the public catalogue',async()=>{
  const f=fixture();const p=await f.create();
  const photo={plantId:p.id,image:'/media/public.webp',thumbnail:'/media/thumb.webp',caption:'Whole plant',photographer:'Fixture credit',sortOrder:1,published:true,sourceEvidence:'PRIVATE PHOTO PATH'};
  await f.admin('PlantPhoto',{method:'POST',body:photo});
  await f.admin('PlantPhoto',{method:'POST',body:{...photo,published:false,caption:'Private draft'}});
  const data=await (await f.call('/api/plants/plant/'+p.id)).json();assert.equal(data.plant.photos.length,1);assert.equal(data.plant.photos[0].photographer,'Fixture credit');
  assert.ok(!JSON.stringify(data).includes('PRIVATE'));
  await f.admin('Plant/'+p.id,{method:'DELETE'});assert.equal((await f.call('/api/plants/plant/'+p.id)).status,404);
});

test('independent cache sees outside edits after bounded reload; expired failures fail closed',async()=>{
  const f=fixture();const p=await f.create();await f.call('/api/plants/catalogue');
  f.db.prepare('UPDATE plants SET published=0 WHERE id=?').run(p.id);
  const realNow=Date.now;Date.now=()=>realNow()+16000;
  try {assert.equal((await (await f.call('/api/plants/catalogue')).json()).plants.length,0)}finally{Date.now=realNow}
  const g=fixture();await g.create();await g.call('/api/plants/catalogue');g.env.DB.batch=async()=>{throw new Error('Database unavailable')};
  Date.now=()=>realNow()+16000;
  try {assert.equal((await g.call('/api/plants/catalogue')).status,503)}finally{Date.now=realNow}
});

test('late snapshot cannot republish a record after an admin invalidation',async()=>{
  const f=fixture();const p=await f.create();const original=f.env.DB.batch;
  let release,ready;const entered=new Promise(resolve=>{ready=resolve});let first=true;
  f.env.DB.batch=async stmts=>{const r=await original(stmts);if(first){first=false;ready();await new Promise(resolve=>{release=resolve})}return r};
  const pending=f.call('/api/plants/catalogue');await entered;
  await f.admin('Plant/'+p.id,{method:'PUT',body:{...p,published:false}});release();
  assert.equal((await (await pending).json()).plants.length,0);
});

test('cache is scoped to the database binding; unknown and injection-shaped IDs are not found',async()=>{
  const a=fixture(),b=fixture();await a.create();
  assert.equal((await (await a.call('/api/plants/catalogue')).json()).plants.length,1);
  assert.equal((await (await b.call('/api/plants/catalogue')).json()).plants.length,0);
  assert.equal((await a.call('/api/plants/plant/'+encodeURIComponent("' OR 1=1 --"))).status,404);
  assert.equal((await a.call('/api/unknown')).status,404);
});

test('real seed is idempotent and preserves admin edits and soft deletion',async()=>{
  let seed;try{seed=readFileSync(new URL('../data/import.sql',import.meta.url),'utf8')}catch{return}
  const f=fixture();f.db.exec(seed);const before=f.db.prepare('SELECT count(*) n FROM plants').get().n;assert.equal(before,531);
  const {id}=f.db.prepare('SELECT id FROM plants LIMIT 1').get();f.db.prepare('UPDATE plants SET scientific_name=?,deleted_at=1 WHERE id=?').run('Owner correction',id);f.db.exec(seed);
  assert.equal(f.db.prepare('SELECT count(*) n FROM plants').get().n,before);assert.equal(f.db.prepare('SELECT scientific_name FROM plants WHERE id=?').get(id).scientific_name,'Owner correction');
  const start=performance.now();const resp=await f.call('/api/plants/catalogue');const data=await resp.json();
  assert.equal(data.plants.length,530);console.log(`Full catalogue: ${(performance.now()-start).toFixed(1)} ms, ${JSON.stringify(data).length} serialized characters`);
});
