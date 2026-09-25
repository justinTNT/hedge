import {test} from 'node:test';
import assert from 'node:assert/strict';
import {mountAdminAccess} from '../src/Client/admin-access.mjs';
import {write, clear} from '../dist/client/packages/hedge/src/Client/AdminCredential.js';
const tick=()=>new Promise(resolve=>setImmediate(resolve));

test('admin links react to same-tab and cross-tab key changes, reject late results, and dispose without storage polling',async()=>{
  const host=new EventTarget();host.BASE_PATH='/st';
  const timers=[];host.setInterval=(fn,ms)=>{timers.push({fn,ms});return timers.length};host.clearInterval=()=>{};
  host.HedgeGuest={withSessionRequest:fn=>fn()};globalThis.window=host;
  const values=new Map();globalThis.localStorage={getItem:k=>values.get(k),setItem:(k,v)=>values.set(k,v),removeItem:k=>values.delete(k)};
  const root=new EventTarget();root.hidden=false;
  const elements=[{dataset:{plantsAccess:'review'}},{dataset:{plantsAccess:'admin'}}];root.querySelectorAll=()=>elements;
  const pending=[];const oldFetch=globalThis.fetch;
  globalThis.fetch=(url,options)=>new Promise(resolve=>{
    assert.equal(url,'/st/api/plants/v2/access');assert.equal(options.cache,'no-store');
    pending.push({resolve,key:options.headers['X-Admin-Key']||''});
  });
  let stop;
  const reply=async(index,canEditCatalogue,canReview)=>{pending[index].resolve(Response.json({canEditCatalogue,canReview}));await tick()};
  try {
    stop=mountAdminAccess(root,host);await tick();
    assert.deepEqual(elements.map(e=>e.hidden),[true,true]);assert.deepEqual(timers.map(t=>t.ms),[15000]);
    write('owner-a');await tick();assert.equal(pending[1].key,'owner-a');
    await reply(1,true,true);assert.deepEqual(elements.map(e=>e.hidden),[false,false]);
    clear();assert.deepEqual(elements.map(e=>e.hidden),[true,true]);await tick();
    await reply(0,true,true);assert.deepEqual(elements.map(e=>e.hidden),[true,true]);
    await reply(2,false,false);
    values.set('adminKey','owner-b');const changed=new Event('storage');changed.key='adminKey';host.dispatchEvent(changed);await tick();
    assert.equal(pending[3].key,'owner-b');await reply(3,true,true);
    // OAuth logout invalidates capability state but leaves the independent owner credential.
    host.dispatchEvent(new Event('hedge:session-cleared'));assert.deepEqual(elements.map(e=>e.hidden),[true,true]);await tick();
    assert.equal(values.get('adminKey'),'owner-b');assert.equal(pending[4].key,'owner-b');
    await reply(4,true,true);assert.deepEqual(elements.map(e=>e.hidden),[false,false]);
    values.delete('adminKey');const removed=new Event('storage');removed.key=null;host.dispatchEvent(removed);await tick();
    assert.deepEqual(elements.map(e=>e.hidden),[true,true]);
    stop();stop=null;await reply(5,true,true);assert.deepEqual(elements.map(e=>e.hidden),[true,true]);
    write('after-disposal');await tick();assert.equal(pending.length,6);
  } finally {stop?.();globalThis.fetch=oldFetch;}
});
