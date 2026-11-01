import './helpers/browser.mjs';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { init, update, account, Msg, loginUrl } from '../dist/client/Auth.js';
import { IdentityData, GuestSessionData, SessionReadiness } from '../dist/client/packages/hedge/src/Client/GuestSession.js';
globalThis.window={location:{pathname:'/plants/a/name',search:'?view=flowers',hash:'#section-fruit'}};
const identity=new IdentityData('identity-id','google','Researcher','');
const session=new GuestSessionData('guest-id','Generated Anonymous Name','','','',identity);
const ready=new SessionReadiness(true,session);
test('authentication requires a fresh ready result; cached and anonymous display details never suffice',()=>{
  assert.equal(account(ready),identity);
  assert.equal(account(new SessionReadiness(false,session)),undefined);
  const anon=new GuestSessionData('guest-id','Generated Anonymous Name','','','',new IdentityData('anon-id','anonymous','Anonymous',''));
  assert.equal(account(new SessionReadiness(true,anon)),undefined);
  assert.equal(account(undefined),undefined);
});
test('logout and cross-tab clearing invalidate in-flight reads immediately',()=>{
  for(const msg of [new Msg(5,[]),new Msg(7,[])]) {
    let [m]=init();[m]=update(new Msg(1,[1,ready]),m);assert.equal(m.Account,identity);
    [m]=update(msg,m);assert.equal(m.Account,undefined);
    assert.equal(update(new Msg(1,[1,ready]),m)[0].Account,undefined);
  }
});
test('failed refresh fails closed and a failed logout offers recovery',()=>{
  let [m]=init();[m]=update(new Msg(1,[1,ready]),m);
  [m]=update(new Msg(0,[]),m);[m]=update(new Msg(1,[m.Request,undefined]),m);
  assert.equal(m.Account,undefined);
  [m]=update(new Msg(6,[false]),m);assert.equal(m.Open,true);assert.match(m.Error,/Logout did not complete/);
});
test('login links retain the species, search and section anchor',()=>{
  assert.equal(loginUrl('google'),'/api/auth/google/login?returnTo=%2Fplants%2Fa%2Fname%3Fview%3Dflowers%23section-fruit');
});

test('away from the network, the bundled app keeps the identity last confirmed for its stored bearer; the web does not',()=>{
  const stale=new SessionReadiness(false,session);
  let [m]=init();
  [m]=update(new Msg(1,[m.Request,stale]),m);
  assert.equal(m.Account,undefined,'web: a stale read is signed out');
  const previous={API_ORIGIN:window.API_ORIGIN,HedgeGuest:window.HedgeGuest};
  window.API_ORIGIN='https://api.test';window.HedgeGuest={currentBearer:()=>'stored-bearer',isMobile:true};
  try {
    [m]=init();[m]=update(new Msg(1,[m.Request,stale]),m);
    assert.equal(m.Account?.Id,'identity-id','bundled app with a bearer: the last identity stands in');
    window.HedgeGuest={currentBearer:()=>'',isMobile:true};
    [m]=init();[m]=update(new Msg(1,[m.Request,stale]),m);
    assert.equal(m.Account,undefined,'no bearer, no identity');
    window.HedgeGuest={currentBearer:()=>'stored-bearer',isMobile:true};
    const anonymous=new SessionReadiness(false,new GuestSessionData('guest-id','Name','','','',new IdentityData('anon','anonymous','Anonymous','')));
    [m]=init();[m]=update(new Msg(1,[m.Request,anonymous]),m);
    assert.equal(m.Account,undefined,'an anonymous last identity is not a sign-in');
  } finally {window.API_ORIGIN=previous.API_ORIGIN;window.HedgeGuest=previous.HedgeGuest;}
});

