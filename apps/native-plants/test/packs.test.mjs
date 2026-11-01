// Media packs (Client.Packs): a plan derived from what the read cache holds, the connection rule, the
// size estimates and the settings round trip, through the compiled output with the memory store.
import './helpers/browser.mjs';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { store, plan, urls, estimate, megabytes, allowed, defaults, loadSettings, saveSettings, kept, keptOf, Settings, Connection, Pack }
  from '../dist/client/Packs.js';
import { cachedTransport, keepMedia } from '../dist/client/packages/hedge/src/Client/Offline.js';
import { Request, Response } from '../dist/client/packages/hedge/src/Hedge/Http.js';
import { FSharpResult$2 } from '../dist/client/fable_modules/fable-library-js.4.29.0/Result.js';
import { empty, toArray } from '../dist/client/fable_modules/fable-library-js.4.29.0/List.js';

const ok=body=>new FSharpResult$2(0,[new Response(200,empty(),body)]);
const get=path=>new Request('GET',path,empty(),empty(),undefined);
const card=id=>({id,slug:id,scientificName:'Species '+id,commonNames:'',family:'F',genus:'G',aliases:[],forms:[],height:'',sun:[],water:[],gardenFeatures:[],wildlife:[],endemicNt:false,summary:'',photo:null,photographers:[]});
const plant=(id,photos,maps)=>JSON.stringify({plant:{card:card(id),sections:[],
  photos:photos.map((p,i)=>({id:`${id}-p${i}`,image:p[0],thumbnail:p[1],caption:'',photographer:''})),
  distributionMaps:maps.map((m,i)=>({id:`${id}-m${i}`,image:m,caption:'',sourceLabel:''}))}});
const catalogue=JSON.stringify({plants:[card('a'),card('b'),card('c')],families:[],forms:[],sun:[],water:[],gardenFeatures:[],wildlife:[],revision:'r1',refreshSeconds:15,photographers:[],
  glossary:[{id:'g1',term:'Phyllode',aliases:[],definition:'',illustration:'/media/g1.webp',sourceLabel:''},{id:'g2',term:'Plain',aliases:[],definition:'',illustration:'',sourceLabel:''}],references:[]});
const pack={base:new Pack(0,[]),thumbnails:new Pack(1,[]),fullSize:new Pack(2,[])};
const on={wifi:new Connection(0,[]),mobile:new Connection(1,[]),unknown:new Connection(2,[]),offline:new Connection(3,[])};

test('the plan is exactly the photographs, maps and drawings of the accounts the cache holds',async()=>{
  const bodies={'/api/plants/catalogue':catalogue,
    '/api/plants/plant/a':plant('a',[['/media/a1-large.webp','/media/a1-thumb.webp'],['/media/a2-large.webp','/media/a2-thumb.webp']],['/media/map-a.webp']),
    '/api/plants/plant/b':plant('b',[['/media/a1-large.webp','/media/a1-thumb.webp']],['/media/map-a.webp','/media/map-b.webp'])};
  const read=cachedTransport(store,()=>'',()=>{},async request=>ok(bodies[request.Path]));
  for(const path of Object.keys(bodies))await read(get(path));
  await store.Put('warm/updated',1700000000000);
  const planned=await plan();
  assert.equal(planned.Accounts,2);assert.equal(planned.Total,3);assert.equal(planned.Updated,1700000000000);
  assert.deepEqual(toArray(urls(planned,pack.thumbnails)),['/media/a1-thumb.webp','/media/a2-thumb.webp'],'distinct, in order');
  assert.deepEqual(toArray(urls(planned,pack.fullSize)),['/media/a1-large.webp','/media/a2-large.webp']);
  assert.deepEqual(toArray(urls(planned,pack.base)),['/media/map-a.webp','/media/map-b.webp','/media/g1.webp'],'maps then drawings; an empty illustration is skipped');
  await keepMedia(store,'/media/a1-thumb.webp','bytes');
  assert.equal(keptOf(await kept(),urls(planned,pack.thumbnails)),1);assert.equal(keptOf(await kept(),urls(planned,pack.fullSize)),0);
  assert.equal(megabytes(estimate(planned,pack.thumbnails)),'under 1 MB');
  assert.equal(megabytes(2004*54000),'108 MB');
});

test('packs download by themselves on Wi-Fi, never offline, and on mobile or unknown networks only when allowed',()=>{
  assert.equal(allowed(defaults,on.wifi),true);
  assert.equal(allowed(defaults,on.offline),false);
  assert.equal(allowed(defaults,on.mobile),false);
  assert.equal(allowed(defaults,on.unknown),false);
  const anyNetwork=new Settings(true,false,false);
  assert.equal(allowed(anyNetwork,on.mobile),true);
  assert.equal(allowed(anyNetwork,on.unknown),true);
  assert.equal(allowed(anyNetwork,on.offline),false);
  assert.deepEqual([defaults.Thumbnails,defaults.FullSize,defaults.WifiOnly],[true,false,true]);
});

test('settings round-trip through the store and fall back to the defaults',async()=>{
  assert.deepEqual(await loadSettings(),defaults);
  await saveSettings(new Settings(false,true,false));
  const loaded=await loadSettings();
  assert.deepEqual([loaded.Thumbnails,loaded.FullSize,loaded.WifiOnly],[false,true,false]);
  await store.Put('settings/offline',{thumbnails:true});
  const partial=await loadSettings();
  assert.deepEqual([partial.Thumbnails,partial.FullSize,partial.WifiOnly],[true,false,true],'missing fields take the defaults');
});
