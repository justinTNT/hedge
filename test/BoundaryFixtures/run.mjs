import { test } from 'node:test';
import assert from 'node:assert/strict';
const events=new Map(),store=new Map();
globalThis.window={BASE_PATH:'/st',addEventListener:(k,f)=>{if(!events.has(k))events.set(k,new Set());events.get(k).add(f)},removeEventListener:(k,f)=>events.get(k)?.delete(f),dispatchEvent:e=>{for(const f of events.get(e.type)||[])f(e)}};
globalThis.localStorage={getItem:k=>store.get(k)||null,setItem:(k,v)=>store.set(k,v),removeItem:k=>store.delete(k)};
const runtime=await import('./dist/Runtime.js');
const credentials=await import('./dist/packages/hedge/src/Client/AdminCredential.js');
const api=await import('./dist/packages/hedge/src/Client/Api.js');
const sessions=await import('./dist/packages/hedge/src/Client/GuestSession.js');
const {createClient}=await import('./dist/generated/ClientGen.js');

test('generated standalone and module GET bindings preserve request, environment, context and parameters',async()=>{
  for(const dispatch of [runtime.standalone,runtime.modular]) {
    for(const [path,value] of [['/public','fixture'],['/private','signed:fixture:ctx'],['/by/plant','plant:signed:fixture:ctx'],['/query?q=red%20flower','red flower:signed:fixture:ctx'],['/both/plant?q=red','plant:red:signed:fixture:ctx']]) {
      const result=await dispatch(new Request('https://test'+path,{headers:{Cookie:'signed'}}),{marker:'ctx'});
      assert.equal((await result.json()).value,value);
    }
  }
});
test('generated client decodes nested records/lists and distinguishes invalid success from rejection',async()=>{
  const client=createClient(async req=>({tag:0,fields:[{Status:200,Headers:[],Body:JSON.stringify({value:'ok',identity:{provider:'google',roles:['curator']}})}]}));
  const good=await client.readPrivate();assert.equal(good.tag,0);assert.equal(good.fields[0].Identity.Provider,'google');assert.deepEqual([...good.fields[0].Identity.Roles],['curator']);
  const bad=createClient(async()=>({tag:0,fields:[{Status:200,Headers:[],Body:'{"value":true}'}]}));
  assert.equal((await bad.readPrivate()).fields[0].tag,3);
  const denied=createClient(async()=>({tag:0,fields:[{Status:403,Headers:[],Body:'{"error":"Denied"}'}]}));
  assert.equal((await denied.readPrivate()).fields[0].tag,1);
});
test('resource operation ceiling bounds owner and subject in discovery and direct CRUD',async()=>{
  let writes=0;
  const statement={bind(){return this},async all(){return {results:[{id:'x'}]}},async first(){return {id:'x'}},async run(){writes++;throw Error('Forbidden write')}};
  const db={prepare(){return statement}};
  const call=(path,access,method='GET')=>runtime.admin(new Request('https://test/api/admin/'+path,{method,headers:{'X-Test-Access':access}}),db);
  for(const access of ['owner','reader']) {
    const response=await call('types',access);const types=(await response.json()).types;
    assert.deepEqual(types.map(t=>t.name),['Protected']);
    assert.deepEqual(types[0].ops,access==='owner'?['list','read']:['list']);
    for(const method of ['POST','PUT','DELETE']) assert.equal((await call(method==='POST'?'Protected':'Protected/x',access,method)).status,403);
  }
  assert.equal((await call('Protected/x','owner')).status,200);
  assert.equal((await call('Protected/x','reader')).status,403);
  assert.equal((await call('Hidden','owner')).status,403);
  assert.equal((await call('Protected','anonymous')).status,401);
  assert.equal((await call('Protected','reader')).headers.get('set-cookie'),'renewed-cookie');assert.equal(writes,0);
});
test('credential changes notify this document and other tabs without exposing the key; disposal and storage failures work',()=>{
  let changed=0;const dispose=runtime.subscribeCredentials(()=>changed++);
  assert.equal(credentials.write('owner').tag,0);assert.equal(credentials.read(),'owner');assert.equal(changed,1);
  window.dispatchEvent({type:'storage',key:'adminKey'});assert.equal(changed,2);
  window.dispatchEvent({type:'storage',key:'else'});assert.equal(changed,2);
  assert.equal(credentials.clear().tag,0);assert.equal(credentials.read(),'');assert.equal(changed,3);
  dispose();credentials.write('other');assert.equal(changed,3);
  const set=localStorage.setItem;localStorage.setItem=()=>{throw Error('blocked')};
  assert.equal(credentials.write('not-stored').tag,1);assert.equal(credentials.read(),'other');localStorage.setItem=set;
});
test('private browser adapter applies base, headers and no-store once and preserves HTTP failure status',async()=>{
  let locked=0,request;
  window.HedgeGuest={withSessionRequest:async f=>{locked++;return f()}};
  const fetch=globalThis.fetch;globalThis.fetch=async(url,options)=>{request={url,options};return new Response('{"error":"revoked"}',{status:403})};
  try {
    const {Request}=await import('./dist/packages/hedge/src/Hedge/Http.js');
    const {ofArray,empty}=await import('./dist/fable_modules/fable-library-js.4.29.0/List.js');
    const result=await sessions.transport(api.uncachedBrowserTransport,new Request('POST','/private',empty(),ofArray([['X-Admin-Key','captured']]),'{}'));
    assert.equal(result.tag,0);assert.equal(result.fields[0].Status,403);assert.equal(locked,1);
    assert.equal(request.url,'/st/private');assert.equal(request.options.cache,'no-store');assert.equal(new Headers(request.options.headers).get('Content-Type'),'application/json');assert.equal(new Headers(request.options.headers).get('X-Admin-Key'),'captured');
  } finally {globalThis.fetch=fetch}
});
