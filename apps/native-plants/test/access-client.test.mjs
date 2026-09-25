import { empty as emptyList } from '../dist/client/fable_modules/fable-library-js.4.29.0/List.js';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { renderToStaticMarkup } from 'react-dom/server';
import { empty, update, canEdit, canReview, Msg as AccessMsg } from '../dist/client/Access.js';
import { init, update as appUpdate, footer, view, Page, Msg as AppMsg, changeRoute } from '../dist/client/App.js';
import { Msg as AuthMsg } from '../dist/client/Auth.js';
import { Msg as ReviewMsg } from '../dist/client/Review.js';
import { Capabilities, Review, Note } from '../dist/client/Models/Contributions.js';
import { FSharpResult$2 as Result } from '../dist/client/fable_modules/fable-library-js.4.29.0/Result.js';
import { storedAdminKey, readCapabilities } from '../src/Client/access-client.mjs';
import { showCapabilities } from '../src/Client/admin-access.mjs';

globalThis.window={location:{pathname:'/review',search:''}};
globalThis.document={title:''};
let key='';globalThis.localStorage={getItem:()=>key};
const ok=value=>new Result(0,[value]);
const denied=new Capabilities(false,false,false),curator=new Capabilities(false,true,false),owner=new Capabilities(true,true,true);
const loaded=(epoch,data)=>new AccessMsg(1,[epoch,ok(data)]);
const privateQueue=new Review([{PlantId:'plant-a',PlantName:'A plant',Note:new Note('note-a','Private correction for review',true,1,false,1,'correction',emptyList(),emptyList())}],[],0,false);
function permitted(capabilities) {
  let [m]=init();m.Access={...empty(1),Key:key,Data:capabilities};return m;
}

test('links require confirmed capabilities, not a saved key or a signed-in display identity',()=>{
  key='unverified-key';let [m]=init();
  let html=renderToStaticMarkup(footer(m,()=>{}));assert.doesNotMatch(html,/Edit catalogue|Review contributions/);
  m=permitted(denied);assert.doesNotMatch(renderToStaticMarkup(footer(m,()=>{})),/Edit catalogue|Review contributions/);
  m=permitted(curator);html=renderToStaticMarkup(footer(m,()=>{}));assert.match(html,/Review contributions/);assert.doesNotMatch(html,/Edit catalogue/);
  m=permitted(owner);html=renderToStaticMarkup(footer(m,()=>{}));assert.match(html,/Edit catalogue/);assert.match(html,/Review contributions/);
  key='';
});

test('direct review navigation checks access first and never renders the queue for a denied visitor',()=>{
  const m=permitted(denied);m.Review.Data=privateQueue;
  const html=renderToStaticMarkup(view(m,()=>{}));assert.match(html,/Review access required/);
  assert.doesNotMatch(html,/Contributions to the guide|Private correction for review|Refresh review queue|Mark read/);
  const [entered,commands]=changeRoute(permitted(curator));
  assert.equal(canReview(entered.Access),false);assert.equal(entered.Review.Data,undefined);assert.equal(entered.Review.Busy,false);
  const messages=[];for(const cmd of commands)cmd(m=>messages.push(m));
  assert.ok(messages.some(m=>m.tag===19));assert.ok(!messages.some(m=>m.tag===18));
});

test('a grant revocation removes review data and ignores late queue responses',()=>{
  let m=permitted(curator);m.Review.Data=privateQueue;const reviewEpoch=m.Review.Epoch;
  [m]=appUpdate(new AppMsg(19,[loaded(m.Access.Epoch,denied)]),m);
  assert.equal(m.Review.Data,undefined);assert.equal(canReview(m.Access),false);
  const [late]=appUpdate(new AppMsg(18,[new ReviewMsg(3,[reviewEpoch,ok(privateQueue)])]),m);
  assert.equal(late.Review.Data,undefined);
  assert.doesNotMatch(renderToStaticMarkup(view(late,()=>{})),/Private correction for review/);
});

test('logout and credential changes invalidate outstanding permission decisions',()=>{
  key='';let m=permitted(curator);const epoch=m.Access.Epoch;
  [m]=appUpdate(new AppMsg(16,[new AuthMsg(5,[])]),m);
  [m]=appUpdate(new AppMsg(19,[loaded(epoch,curator)]),m);assert.equal(canReview(m.Access),false);
  key='first-key';let [a]=update(new AccessMsg(0,[]),empty(0));
  key='changed-key';[a]=update(loaded(a.Epoch,owner),a);assert.equal(canEdit(a),false);assert.equal(canReview(a),false);
  key='';
});

test('failed checks hide capabilities and review errors do not cause an automatic retry loop',()=>{
  key='';let a={...empty(1),Data:owner};
  [a]=update(new AccessMsg(1,[1,new Result(1,['Unavailable'])]),a);assert.equal(a.Failed,true);assert.equal(canReview(a),false);
  let m=permitted(curator);m.Review.Error='Queue unavailable';
  const [next,commands]=appUpdate(new AppMsg(19,[loaded(m.Access.Epoch,curator)]),m);
  assert.equal(next.Review.Busy,false);assert.equal([...commands].length,0);
});

test('admin-page navigation and role instructions start hidden and follow permissions',()=>{
  const elements=[{dataset:{plantsAccess:'review'},hidden:false},{dataset:{plantsAccess:'admin'},hidden:false}];
  const root={querySelectorAll:()=>elements};
  showCapabilities(root,null);assert.deepEqual(elements.map(e=>e.hidden),[true,true]);
  showCapabilities(root,curator);assert.deepEqual(elements.map(e=>e.hidden),[false,true]);
  showCapabilities(root,owner);assert.deepEqual(elements.map(e=>e.hidden),[false,false]);
  showCapabilities(root,denied);assert.deepEqual(elements.map(e=>e.hidden),[true,true]);
});

test('capability fetch uses the session lock, sends only the supplied key and rejects malformed responses',async()=>{
  const oldFetch=globalThis.fetch;let calls=0;
  window.HedgeGuest={withSessionRequest:async fn=>{calls++;return fn()}};
  globalThis.fetch=async(url,options)=>{
    assert.equal(url,'/api/plants/v2/access');assert.equal(options.cache,'no-store');assert.ok(options.credentials===undefined || options.credentials==='same-origin');
    assert.equal(options.headers['X-Admin-Key'],'test-owner-key');return Response.json({canEditCatalogue:true,canReview:true,canIdentify:true});
  };
  try {
    const result=await readCapabilities('test-owner-key');assert.equal(result.CanEditCatalogue,true);assert.equal(result.CanReview,true);assert.equal(calls,1);
    globalThis.fetch=async()=>Response.json({CanReview:'true',CanEditCatalogue:'true'});
    await assert.rejects(readCapabilities(''),/canEditCatalogue|canReview/);
    const storage=globalThis.localStorage;globalThis.localStorage={getItem(){throw new Error('Blocked')}};
    try{assert.equal(storedAdminKey(),'')}finally{globalThis.localStorage=storage}
  } finally {globalThis.fetch=oldFetch;delete window.HedgeGuest}
});


test('a review completion arriving before the credential event cannot restore the old queue',()=>{
  key='first-owner';let m=permitted(owner);m.Review.Key=key;
  const epoch=m.Review.Epoch;key='different-owner';
  const [next]=appUpdate(new AppMsg(18,[new ReviewMsg(3,[epoch,ok(privateQueue)])]),m);
  assert.equal(next.Review.Data,undefined);assert.equal(canReview(next.Access),false);key='';
});


test('losing one role clears the old queue even while another review capability remains',()=>{
  key='';let m=permitted(owner);m.Review.Data=privateQueue;
  const epoch=m.Review.Epoch;
  const identifier=new Capabilities(false,false,true);
  [m]=appUpdate(new AppMsg(19,[loaded(m.Access.Epoch,identifier)]),m);
  assert.equal(canReview(m.Access),true);assert.equal(m.Review.Data,undefined);assert.ok(m.Review.Epoch>epoch);
  [m]=appUpdate(new AppMsg(18,[new ReviewMsg(3,[epoch,ok(privateQueue)])]),m);
  assert.equal(m.Review.Data,undefined);
  assert.match(renderToStaticMarkup(footer(m,()=>{})),/Review contributions/);
  assert.doesNotMatch(renderToStaticMarkup(footer(m,()=>{})),/Edit catalogue/);
});
