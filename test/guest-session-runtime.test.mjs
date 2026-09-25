import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
const code=readFileSync(new URL('../packages/hedge/lib/guest-session.js',import.meta.url),'utf8');
function fixture(fetch,{locks}={}) {
  const store=new Map(),events=new Map();let cleared=0;
  const context={Promise,Math,Date,JSON,encodeURIComponent,fetch,navigator:locks?{locks}:{},
    CustomEvent:class {constructor(type){this.type=type}},
    localStorage:{getItem:k=>store.get(k)||null,setItem:(k,v)=>store.set(k,v)},
    window:{BASE_PATH:'/guide',addEventListener:(k,fn)=>events.set(k,fn),dispatchEvent:()=>{cleared++}}};
  vm.runInNewContext(code,context);
  return {api:context.window.HedgeGuest,store,events,cleared:()=>cleared};
}
const response=()=>({ok:true,json:async()=>({guest:{guestId:'guest',identity:{id:'id',provider:'google',name:'Researcher',picture:''}}})});
const turn=()=>new Promise(resolve=>setImmediate(resolve));
test('bootstrap is single-flight and a failure can be retried',async()=>{
  let calls=0;const f=fixture(async()=>{calls++;return calls===1?{ok:false}:response()});
  assert.equal(f.api.ensureSession(),f.api.ensureSession());
  assert.equal((await f.api.ensureSession()).ready,false);
  assert.equal((await f.api.ensureSession()).ready,true);assert.equal(calls,2);
});
test('logout waits for renewing reads and discards their identity before clearing the cookie',async()=>{
  let release;const calls=[];
  const f=fixture(async(url,options)=>{calls.push([url,options]);if(url.endsWith('/me'))return new Promise(resolve=>{release=resolve});return {ok:true}});
  const read=f.api.refreshSession();await turn();
  const logout=f.api.signOut();assert.equal(f.api.signOut(),logout);await turn();
  assert.equal(calls.length,1);release(response());assert.equal((await read).ready,false);
  assert.equal(await logout,true);assert.ok(f.api.getSession().identity == null);
  assert.equal(calls[1][0],'/guide/api/auth/logout');assert.equal(calls[1][1].method,'POST');assert.ok(f.cleared()>=2);
});
test('invalidation rejects a late read; a subsequent refresh gets current identity',async()=>{
  let release;let count=0;const f=fixture(async()=>++count===1?new Promise(r=>{release=r}):response());
  const old=f.api.refreshSession();await turn();f.api.invalidateSession();release(response());
  assert.equal((await old).ready,false);assert.equal((await f.api.refreshSession()).session.identity.name,'Researcher');
});
test('cross-tab logout clears display state and prevents bootstrap during logout',async()=>{
  let calls=0;const f=fixture(async()=>{calls++;return response()});await f.api.refreshSession();
  f.events.get('storage')({key:'hedge_session_logout',newValue:'{"phase":"begin"}'});
  assert.ok(f.api.getSession().identity == null);assert.equal((await f.api.refreshSession()).ready,false);assert.equal(calls,1);
  f.events.get('storage')({key:'hedge_session_logout',newValue:'{"phase":"end"}'});
  await f.api.refreshSession();assert.equal(calls,2);
});
test('Web Locks coordinate cookie-changing requests using one shared lock name',async()=>{
  const names=[];const f=fixture(async()=>response(),{locks:{request:async(name,task)=>{names.push(name);return task()}}});
  await f.api.refreshSession();await f.api.signOut();assert.deepEqual(names,['hedge-session-cookie','hedge-session-cookie']);
});
test('logout failure is explicit and can be retried',async()=>{
  let calls=0;const f=fixture(async()=>({ok:++calls>1}));
  assert.equal(await f.api.signOut(),false);assert.equal(await f.api.signOut(),true);
});

test('a browser lock failure fails closed and does not strand logout retries',async()=>{
  let blocked=true;
  const f=fixture(async()=>response(),{locks:{request:async(name,task)=>{if(blocked)throw new Error('Lock unavailable');return task()}}});
  assert.equal((await f.api.refreshSession()).ready,false);
  assert.equal(await f.api.signOut(),false);
  blocked=false;assert.equal(await f.api.signOut(),true);
});


test('protected operations finish before logout clears cookies, but their late results are rejected',async()=>{
  let release;const calls=[];const f=fixture(async(url)=>{calls.push(url);return url.endsWith('/me')?response():{ok:true}});
  await f.api.ensureSession();
  const pending=f.api.withSessionRequest(()=>new Promise(resolve=>{release=resolve}));
  const rejected=assert.rejects(pending,/session changed/);await turn();
  const logout=f.api.signOut();await turn();assert.equal(calls.length,1);
  release({private:'data'});await rejected;assert.equal(await logout,true);assert.equal(calls.length,2);
});
test('protected writes queued before logout are cancelled rather than sent with a different session',async()=>{
  let release,writes=0;const f=fixture(async()=>response());await f.api.ensureSession();
  const first=f.api.withSessionRequest(()=>new Promise(resolve=>{release=resolve}));const rejected=assert.rejects(first,/session changed/);await turn();
  const second=f.api.withSessionRequest(()=>{writes++;return 'sent'});const cancelled=assert.rejects(second,/session changed/);await turn();
  const logout=f.api.signOut();release('old response');await Promise.all([rejected,cancelled,logout]);assert.equal(writes,0);
});
