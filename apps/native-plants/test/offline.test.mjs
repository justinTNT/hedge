// The framework's offline runtime (Client.Offline), driven through its compiled output exactly as the
// app composes it: a memory store, fake transports, and the generated request shape.
import './helpers/browser.mjs';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { memoryStore, cachedTransport, cacheFirstTransport, warm, createOutbox, defaultDecision, fetchMedia, keepMedia, keptMedia, cacheKey, cachedResponses, keepPrivateMedia, keptPrivateMedia, forgetPrivateMedia, fetchPrivateMedia }
  from '../dist/client/packages/hedge/src/Client/Offline.js';
import { Request, Response, ApiError } from '../dist/client/packages/hedge/src/Hedge/Http.js';
import { FSharpResult$2 } from '../dist/client/fable_modules/fable-library-js.4.29.0/Result.js';
import { ofArray, empty, toArray } from '../dist/client/fable_modules/fable-library-js.4.29.0/List.js';

const get=(path,query=[])=>new Request('GET',path,ofArray(query),empty(),undefined);
const post=(path,body)=>new Request('POST',path,empty(),empty(),body);
const ok=(status,body,headers=[])=>new FSharpResult$2(0,[new Response(status,ofArray(headers),body)]);
const down=()=>new FSharpResult$2(1,[new ApiError(0,['Failed to fetch'])]);
const unwrap=result=>{assert.equal(result.tag,0,'expected Ok');return result.fields[0];};
const servedAs=served=>served.tag===0?'network':'cache';

test('cache keys separate scope, method, path and query',()=>{
  assert.equal(cacheKey('me',get('/api/x',[['q','a'],['b','2']])),'read/me/GET /api/x?q=a&b=2');
  assert.equal(cacheKey('',get('/api/x')),'read//GET /api/x');
});

test('network-first reads store a success, answer from the copy when the network is away, and revalidate by ETag',async()=>{
  const store=memoryStore(),served=[],calls=[];let online=true;
  const inner=async request=>{calls.push(request);return online?ok(200,'{"revision":"a"}',[['ETag','"v1"']]):down();};
  const read=cachedTransport(store,()=>'',(_,s)=>served.push(servedAs(s)),inner);
  let response=unwrap(await read(get('/api/plants/catalogue')));
  assert.equal(response.Body,'{"revision":"a"}');assert.equal(served.at(-1),'network');
  online=false;
  response=unwrap(await read(get('/api/plants/catalogue')));
  assert.equal(response.Status,200);assert.equal(response.Body,'{"revision":"a"}');assert.equal(served.at(-1),'cache');
  const miss=await read(get('/api/plants/plant/x'));
  assert.equal(miss.tag,1,'a miss while away stays a transport failure');assert.equal(miss.fields[0].tag,0);
  online=true;await read(get('/api/plants/catalogue'));
  assert.deepEqual(toArray(calls.at(-1).Headers),[['If-None-Match','"v1"']]);
});

test('a 304 answers from the copy, a 5xx falls back to it, a 4xx is never masked',async()=>{
  const store=memoryStore(),served=[];let reply=()=>ok(200,'first');
  const read=cachedTransport(store,()=>'',(_,s)=>served.push(servedAs(s)),async()=>reply());
  unwrap(await read(get('/api/a')));
  reply=()=>ok(304,'');let response=unwrap(await read(get('/api/a')));
  assert.equal(response.Status,200);assert.equal(response.Body,'first');assert.equal(served.at(-1),'network');
  reply=()=>ok(503,'busy');response=unwrap(await read(get('/api/a')));
  assert.equal(response.Body,'first');assert.equal(served.at(-1),'cache');
  reply=()=>ok(404,'{"error":"gone"}');response=unwrap(await read(get('/api/a')));
  assert.equal(response.Status,404);assert.equal(response.Body,'{"error":"gone"}');
  reply=()=>ok(401,'{"error":"expired"}');response=unwrap(await read(get('/api/a')));
  assert.equal(response.Status,401);
});

test('writes pass straight through and a copy stored under one scope is invisible to another',async()=>{
  const store=memoryStore();let online=true,scope='user-a';
  const read=cachedTransport(store,()=>scope,()=>{},async request=>online?ok(200,request.Path):down());
  unwrap(await read(get('/api/plants/v2/personal/p')));
  online=false;
  assert.equal((await read(post('/api/plants/v2/notes/save','{}'))).tag,1,'a write is never answered from the cache');
  unwrap(await read(get('/api/plants/v2/personal/p')));
  scope='user-b';
  assert.equal((await read(get('/api/plants/v2/personal/p'))).tag,1,'another identity gets no copy');
});

test('cache-first answers from the copy without the network, then falls back to network-first',async()=>{
  const store=memoryStore(),served=[];let calls=0;
  const read=cacheFirstTransport(store,()=>'',(_,s)=>served.push(servedAs(s)),async request=>{calls++;return ok(200,request.Path);});
  unwrap(await read(get('/api/a')));assert.equal(calls,1);assert.equal(served.at(-1),'network');
  unwrap(await read(get('/api/a')));assert.equal(calls,1);assert.equal(served.at(-1),'cache');
  unwrap(await read(get('/api/b')));assert.equal(calls,2);
});

test('warm stores and counts only what the network confirms, a few requests at a time',async()=>{
  const store=memoryStore();let inFlight=0,peak=0;
  const inner=async request=>{inFlight++;peak=Math.max(peak,inFlight);await new Promise(r=>setTimeout(r,2));inFlight--;return request.Path.endsWith('/7')?ok(500,'x'):ok(200,request.Path);};
  const requests=ofArray(Array.from({length:10},(_,i)=>get('/api/plants/plant/'+i)));
  assert.equal(await warm(store,'',inner,requests,3),9);
  assert.ok(peak<=3,'bounded concurrency');
  assert.equal(toArray(await store.Keys('read/')).length,9);
  assert.equal(await warm(store,'',async()=>down(),requests,3),0,'copies already in the cache never count as confirmation for a new revision');
  assert.equal(toArray(await store.Keys('read/')).length,9,'and the old copies stay');
});

test('an outbox pass stays with the identity it started under, and a flush asked for during a pass runs once more',async()=>{
  const store=memoryStore();let scope='alice';const sent=[];
  const send=async item=>{sent.push(item.Scope+':'+item.Request.Path);if(item.Request.Path.endsWith('/switch'))scope='bob';return ok(200,'{}');};
  const outbox=createOutbox(store,()=>scope,send,defaultDecision,async()=>{});
  await outbox.Enqueue(post('/api/a/switch','1'),empty());await outbox.Enqueue(post('/api/a/second','2'),empty());
  await outbox.Flush();
  assert.deepEqual(sent,['alice:/api/a/switch'],'the account switch stopped the pass before the second item');
  scope='alice';assert.equal(toArray(await outbox.Items()).length,1,'the second item is still waiting for alice');
  sent.length=0;
  let release;const gate=new Promise(r=>release=r);
  const slow=createOutbox(store,()=>'carol',async item=>{sent.push(item.Request.Path);await gate;return ok(200,'{}');},defaultDecision,async()=>{});
  await slow.Enqueue(post('/api/c/first','1'),empty());
  const first=slow.Flush();
  await new Promise(r=>setTimeout(r,5));
  await slow.Enqueue(post('/api/c/late','2'),empty());
  const joined=slow.Flush();assert.equal(joined,first,'a flush during a flush joins it');
  release();await first;await new Promise(r=>setTimeout(r,20));
  assert.deepEqual(sent,['/api/c/first','/api/c/late'],'the item queued during the pass went in a second pass');
  assert.equal(toArray(await slow.Items()).length,0);
});

test('the outbox replays in order, stops behind a retry, parks a rejection, isolates scopes and joins concurrent flushes',async()=>{
  const store=memoryStore(),sent=[],delivered=[];let online=false;
  const send=async item=>{sent.push(item.Request.Path);if(!online)return down();return item.Request.Path.endsWith('/quota')?ok(409,'{"error":"full"}'):ok(200,'{}');};
  const outbox=createOutbox(store,()=>'me',send,defaultDecision,async(item,response)=>{delivered.push(item.Request.Path+':'+response.Status);});
  await outbox.Enqueue(post('/api/notes/save','{"a":1}'),empty());
  await outbox.Enqueue(post('/api/notes/quota','{}'),empty());
  await outbox.Enqueue(post('/api/notes/save2','{}'),empty());
  let remaining=toArray(await outbox.Flush());
  assert.equal(sent.length,1,'away: the first item is tried and the pass stops behind it');
  assert.equal(remaining.length,3);assert.equal(remaining[0].Attempts,1);assert.equal(remaining[1].Attempts,0);
  online=true;sent.length=0;
  const [first,second]=await Promise.all([outbox.Flush(),outbox.Flush()]);
  assert.equal(first,second,'a flush during a flush joins it');
  assert.deepEqual(sent,['/api/notes/save','/api/notes/quota','/api/notes/save2']);
  assert.deepEqual(delivered,['/api/notes/save:200','/api/notes/save2:200']);
  remaining=toArray(first);
  assert.equal(remaining.length,1);assert.equal(remaining[0].Parked,'HTTP 409');assert.equal(remaining[0].Request.Path,'/api/notes/quota');
  sent.length=0;await outbox.Flush();assert.equal(sent.length,0,'a parked item waits for the app, not the network');
  const other=createOutbox(store,()=>'you',send,defaultDecision,async()=>{});
  assert.equal(toArray(await other.Items()).length,0,'another identity sees no items');
  await store.Put('media/pending/p1','bytes');
  const withPhoto=await outbox.Enqueue(post('/api/photos','{}'),ofArray(['media/pending/p1']));
  assert.equal(toArray(await outbox.Items()).length,2);
  await outbox.Remove(withPhoto.Id);
  assert.equal(toArray(await outbox.Items()).length,1);
  assert.equal(toArray(await store.Keys('media/')).length,0,'removing an item removes its attachment');
});

test('a parked item can be released, or replaced with the person\'s resolution, and then sends',async()=>{
  const store=memoryStore();
  const send=async item=>item.Request.Path.endsWith('/x')?ok(409,'{"error":"changed"}'):ok(200,'{}');
  const outbox=createOutbox(store,()=>'me',send,defaultDecision,async()=>{});
  const item=await outbox.Enqueue(post('/api/x','1'),empty());
  let remaining=toArray(await outbox.Flush());assert.equal(remaining[0].Parked,'HTTP 409');
  await outbox.Release();remaining=toArray(await outbox.Items());assert.equal(remaining[0].Parked,undefined);
  remaining=toArray(await outbox.Flush());assert.equal(remaining[0].Parked,'HTTP 409','released but still refused');
  await outbox.Replace(item.Id,post('/api/y','2'));remaining=toArray(await outbox.Items());
  assert.equal(remaining[0].Request.Path,'/api/y');assert.equal(remaining[0].Attempts,0);assert.equal(remaining[0].Parked,undefined);
  assert.equal(toArray(await outbox.Flush()).length,0);
});

test('a stopped prefetch takes no more URLs, and stored reads can be listed by path prefix',async()=>{
  const store=memoryStore();let fetched=0;
  const fetchBlob=async url=>{fetched++;return 'blob:'+url;};
  const urls=ofArray(['/media/1.webp','/media/2.webp','/media/3.webp','/media/4.webp']);
  const result=await fetchMedia(store,fetchBlob,urls,1,()=>{},()=>fetched<2);
  assert.equal(result.Kept,2,'two taken before the stop, none after');assert.equal(fetched,2);
  const read=cachedTransport(store,()=>'me',()=>{},async request=>ok(200,'body of '+request.Path));
  await read(get('/api/plants/plant/a'));await read(get('/api/plants/plant/b'));await read(get('/api/plants/catalogue'));
  const accounts=toArray(await cachedResponses(store,'me','/api/plants/plant/'));
  assert.deepEqual(accounts.map(([path,response])=>[path,response.Body]).sort(),[['/api/plants/plant/a','body of /api/plants/plant/a'],['/api/plants/plant/b','body of /api/plants/plant/b']]);
  assert.equal(toArray(await cachedResponses(store,'other','/api/plants/plant/')).length,0,'scoped');
});

test('a delivered item is announced before its attachments go, so the app can keep them',async()=>{
  const store=memoryStore();await store.Put('media/pending/q1','bytes');let seen;
  const outbox=createOutbox(store,()=>'me',async()=>ok(200,'{}'),defaultDecision,async item=>{seen=await store.Get(toArray(item.Attachments)[0]);});
  await outbox.Enqueue(post('/api/photos','{}'),ofArray(['media/pending/q1']));
  assert.equal(toArray(await outbox.Flush()).length,0);
  assert.equal(seen,'bytes','the attachment was still there for the app');
  assert.equal(toArray(await store.Keys('media/')).length,0,'and is removed afterwards');
});

test('identity-bound media is kept and found under one scope only, and prefetches like a pack',async()=>{
  const store=memoryStore();
  await keepPrivateMedia(store,'github:1','/api/plants/personal-media/p1/image','mine');
  assert.deepEqual(toArray(await keptPrivateMedia(store,'github:1')),['/api/plants/personal-media/p1/image']);
  assert.deepEqual(toArray(await keptPrivateMedia(store,'github:2')),[]);
  assert.deepEqual(toArray(await keptMedia(store)),[],'not in the device-wide media');
  let fetched=[];
  const result=await fetchPrivateMedia(store,'github:1',async url=>{fetched.push(url);return 'b';},ofArray(['/api/plants/personal-media/p1/image','/api/plants/personal-media/p1/thumbnail']),2,()=>{},()=>true);
  assert.deepEqual(fetched,['/api/plants/personal-media/p1/thumbnail'],'the kept one is skipped');assert.equal(result.Kept,2);
  await forgetPrivateMedia(store,'github:1');
  assert.deepEqual(toArray(await keptPrivateMedia(store,'github:1')),[]);
});

test('media prefetch skips what is kept, keeps going past a failure, and resumes on the next call',async()=>{
  const store=memoryStore();await keepMedia(store,'/media/a.webp','A');
  let fetched=[];const reports=[];
  const fetchBlob=async url=>{fetched.push(url);if(url.endsWith('bad.webp'))throw new Error('404');return 'blob:'+url;};
  const urls=ofArray(['/media/a.webp','/media/b.webp','/media/bad.webp']);
  let result=await fetchMedia(store,fetchBlob,urls,2,progress=>reports.push(progress.Kept+'/'+progress.Failed+'/'+progress.Total),()=>true);
  assert.deepEqual(fetched.sort(),['/media/b.webp','/media/bad.webp']);
  assert.equal(result.Kept,2);assert.equal(result.Failed,1);assert.equal(reports.length,3);assert.equal(reports.at(-1),'2/1/3');
  assert.deepEqual(toArray(await keptMedia(store)).sort(),['/media/a.webp','/media/b.webp']);
  fetched=[];result=await fetchMedia(store,fetchBlob,urls,2,()=>{},()=>true);
  assert.deepEqual(fetched,['/media/bad.webp'],'only the missing file is tried again');
  assert.equal(result.Kept,2);
});
