import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import vm from 'node:vm';
const source=readFileSync(new URL('../../../packages/hedge/lib/guest-session.js',import.meta.url),'utf8');
const tick=()=>new Promise(resolve=>setImmediate(resolve));
function fixture() {
  const handlers={},storage=new Map(),requests=[];let opens=0,closed=0,removed=0;
  const response=data=>({ok:true,status:200,json:async()=>data});
  const plugins={
    SecureStoragePlugin:{get:async({key})=>({value:storage.get(key)||''}),set:async({key,value})=>storage.set(key,value),remove:async({key})=>storage.delete(key)},
    App:{addListener(name,fn){handlers[name]=fn;return {remove(){removed++}}}},
    Browser:{addListener(name,fn){handlers[name]=fn;return {remove(){removed++}}},open:async()=>{opens++},close:async()=>{closed++}}
  };
  const context={Promise,Math,Date,JSON,Array,Uint8Array,TextEncoder,URL,encodeURIComponent,crypto:globalThis.crypto,navigator:{},setTimeout,
    CustomEvent:class{},localStorage:{getItem(){return null},setItem(){},removeItem(){}},
    fetch:async(url,init={})=>{requests.push({url,init});if(url.endsWith('/bootstrap'))return response({token:'anonymous'});if(url.endsWith('/exchange'))return response({token:'verified'});return response({guest:null})},
    window:{API_ORIGIN:'https://plants.test',HEDGE_MOBILE_RETURN_URL:'nativeplants://auth',Capacitor:{Plugins:plugins},addEventListener(){},dispatchEvent(){}}
  };
  vm.runInNewContext(source,context);
  return {api:context.window.HedgeGuest,handlers,storage,requests,plugins,get opens(){return opens},get removed(){return removed},get closed(){return closed}};
}
test('shared native OAuth accepts only this app callback, exchanges PKCE and updates secure storage',async()=>{
  const f=fixture();await f.api.ensureSession();const signIn=f.api.signIn('google');
  while(!f.opens)await tick();
  f.handlers.appUrlOpen({url:'wtfail://auth?code=other-app'});await tick();
  assert.equal(f.requests.filter(r=>r.url.endsWith('/exchange')).length,0);
  f.handlers.appUrlOpen({url:'nativeplants://auth?code=one-time'});await signIn;
  const exchange=f.requests.find(r=>r.url.endsWith('/exchange'));
  assert.equal(exchange.init.headers.Authorization,'Bearer anonymous');
  assert.match(JSON.parse(exchange.init.body).verifier,/^[a-f0-9]{64}$/);
  assert.equal(f.storage.get('hedge_mobile_bearer'),'verified');assert.equal(f.closed,1);assert.equal(f.removed,2);
});
test('OAuth error returns and failure to open the system browser dispose listeners and permit retry',async()=>{
  const f=fixture();await f.api.ensureSession();const signIn=f.api.signIn('google');
  while(!f.opens)await tick();f.handlers.appUrlOpen({url:'nativeplants://auth?error=identity'});
  await assert.rejects(signIn,/did not complete/);assert.equal(f.removed,2);
  f.plugins.Browser.open=async()=>{throw new Error('Browser unavailable')};
  await assert.rejects(f.api.signIn('google'),/Browser unavailable/);await tick();assert.equal(f.removed,4);
  assert.equal(f.api.currentBearer(),'anonymous');
});
