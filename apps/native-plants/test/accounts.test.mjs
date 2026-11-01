// The bulk warm-up (Client.Search.prime): one accounts response primes the per-plant read cache that the
// pages, the descriptive search and the media packs all read.
import './helpers/browser.mjs';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { prime, load } from '../dist/client/Search.js';
import { store } from '../dist/client/Storage.js';
import { cachedResponses } from '../dist/client/packages/hedge/src/Client/Offline.js';
import { PlantCard, PlantDetail, GetAccounts_Response, Section } from '../dist/client/Models/Api.js';
import { empty, ofArray, toArray } from '../dist/client/fable_modules/fable-library-js.4.29.0/List.js';

const list=empty();
const card=(id,name)=>new PlantCard(id,id,name,'','Fabaceae','Acacia',list,list,'',list,list,list,list,false,'',undefined,list);
const detail=(id,name,sections)=>new PlantDetail(card(id,name),ofArray(sections.map(([l,t])=>new Section(l,t))),list,list);

test('priming from one bulk response makes every account readable, searchable and listable as if fetched singly',async()=>{
  await prime(new GetAccounts_Response(ofArray([
    detail('b','Beta plant',[['Bark','colourful bark'],['References','ignored']]),
    detail('a','Alpha plant',[['Leaves','grey leaves']]),
  ]),'r1'));
  const cached=toArray(await cachedResponses(store,'','/api/plants/plant/'));
  assert.deepEqual(cached.map(([path,response])=>[path,response.Status]).sort(),[['/api/plants/plant/a',200],['/api/plants/plant/b',200]]);
  assert.match(cached[0][1].Body,/"plant":\{"card":/,'stored in the shape the single read returns');
  const accounts=toArray(await load());
  assert.deepEqual(accounts.map(([id])=>id),['a','b'],'scientific-name order');
  assert.deepEqual(toArray(accounts[1][1]).map(s=>s.Label),['Bark'],'the bibliography is left out');
});
