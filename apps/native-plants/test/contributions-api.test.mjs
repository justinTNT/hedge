// The contributions client over the offline runtime: a saved notebook answers on a cold start with no
// session, an online save becomes the saved notebook, and an edit to something waiting joins the queue.
import './helpers/browser.mjs';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { personal, changeOrQueue } from '../dist/client/ContributionsApi.js';
import { owner, store } from '../dist/client/Storage.js';
import { rememberSnapshot, pendingFor } from '../dist/client/Sync.js';
import { cachedResponses } from '../dist/client/packages/hedge/src/Client/Offline.js';
import { PersonalSnapshot } from '../dist/client/Models/Api.js';
import { PersonalChange, Capacity } from '../dist/client/Models/Contributions.js';
import { empty, ofArray, toArray } from '../dist/client/fable_modules/fable-library-js.4.29.0/List.js';

const snapshot=token=>new PersonalSnapshot(false,token,empty(),empty(),'',new Capacity(0,5),new Capacity(0,5));
const unavailable=()=>Promise.reject(new Error('Your session is unavailable. Refresh and try again.'));
const saveEntry=(id,revision,text)=>new PersonalChange(5,[id,revision,text,'private',empty()]);
const serverPersonal=token=>JSON.stringify({personal:{anonymous:false,viewerToken:token,notes:[],photos:[],heroPhotoId:'',noteCapacity:{used:1,limit:5},photoCapacity:{used:0,limit:5}}});

test('a cold start with no network opens the saved notebook for its owner before any session check',async()=>{
  owner('github:cold');
  await rememberSnapshot('github:cold','plant-x',snapshot('viewer-cold'));
  globalThis.window.HedgeGuest={withSessionRequest:unavailable,currentBearer:()=>'bearer',isMobile:true};
  globalThis.fetch=()=>Promise.reject(new TypeError('Failed to fetch'));
  const result=await personal('plant-x');
  assert.equal(result.tag,0,'answered from the saved copy');assert.equal(result.fields[0].ViewerToken,'viewer-cold');
  owner('github:other');
  assert.equal((await personal('plant-x')).tag,1,'another identity finds nothing');
});

test('an online save becomes the saved notebook, and an edit to something waiting joins the queue instead of overtaking it',async()=>{
  owner('github:saver');
  globalThis.window.HedgeGuest={withSessionRequest:action=>action(),currentBearer:()=>'bearer',isMobile:true};
  globalThis.fetch=async()=>new Response(serverPersonal('viewer-s'),{status:200});
  const saved=await changeOrQueue('plant-y','viewer-s',saveEntry('n1',0,'first'));
  assert.equal(saved.tag,0);assert.equal(saved.fields[0].ViewerToken,'viewer-s');
  const stored=toArray(await cachedResponses(store,'github:saver','/api/plants/v2/personal/plant-y'));
  assert.equal(stored.length,1,'the acknowledged notebook is the saved one now');
  assert.match(stored[0][1].Body,/"used":1/);
  globalThis.fetch=()=>Promise.reject(new TypeError('Failed to fetch'));
  assert.equal((await changeOrQueue('plant-y','viewer-s',saveEntry('n2',0,'offline text'))).fields[0],undefined,'away: queued');
  let pending=toArray(await pendingFor('plant-y'));assert.equal(pending.length,1);
  globalThis.fetch=async()=>new Response(serverPersonal('viewer-s'),{status:200});
  const again=await changeOrQueue('plant-y','viewer-s',saveEntry('n2',0,'newer text'));
  assert.equal(again.tag,0,'back online: the folded queue was sent and the notebook read');
  pending=toArray(await pendingFor('plant-y'));assert.equal(pending.length,0,'nothing left behind to conflict later');
});
