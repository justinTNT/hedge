// The app's offline sync policy (Client.Sync) over the framework outbox, through the compiled output:
// typed queueing and folding, the overlay the notebook shows, replay decisions, retry and reconcile.
import './helpers/browser.mjs';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { store, queueChange, queueUpload, pendingFor, pendingState, overlay, decide, retry, reconcile, fold, keepDelivered, rememberDelivered, rememberSnapshot, prepare, supersedes, Change, Pending, Fold }
  from '../dist/client/Sync.js';
import { owner } from '../dist/client/Storage.js';
import { keptPrivateMedia, cachedResponses } from '../dist/client/packages/hedge/src/Client/Offline.js';
import { PersonalSnapshot } from '../dist/client/Models/Api.js';
import { OutboxItem } from '../dist/client/packages/hedge/src/Client/Offline.js';
import { Request, Response, ApiError } from '../dist/client/packages/hedge/src/Hedge/Http.js';
import { sessionChangedMessage } from '../dist/client/packages/hedge/src/Client/GuestSession.js';
import { PersonalChange, Personal, Photo, Note, Capacity } from '../dist/client/Models/Contributions.js';
import { SaveFieldNote_Request, UpdatePhoto_Request, SelectHero_Request } from '../dist/client/Models/Api.js';
import { Image } from '../dist/client/packages/hedge/src/Hedge/Interface.js';
import { FSharpResult$2 } from '../dist/client/fable_modules/fable-library-js.4.29.0/Result.js';
import { ofArray, empty, toArray } from '../dist/client/fable_modules/fable-library-js.4.29.0/List.js';

const ok=(status,body)=>new FSharpResult$2(0,[new Response(status,empty(),body)]);
const failed=message=>new FSharpResult$2(1,[new ApiError(0,[message])]);
const saveEntry=(id,revision,text,purpose='private',photos=[])=>new PersonalChange(5,[id,revision,text,purpose,ofArray(photos)]);
const deleteNote=(id,revision)=>new PersonalChange(1,[id,revision]);
const deletePhoto=(id,revision)=>new PersonalChange(3,[id,revision]);
const selectHero=id=>new PersonalChange(4,[id]);
const item=(id,parked)=>new OutboxItem(id,'test',new Request('POST','/x',empty(),empty(),undefined),empty(),0,Date.now(),parked);
const entry=(id,revision,text,purpose='private',photos=[])=>new Change(0,[new SaveFieldNote_Request('plant-c',id,revision,text,purpose,ofArray(photos))]);
const photo=(id,width=300,height=220)=>new Photo(id,new Image('/api/image/'+id),new Image('/api/thumb/'+id),'Mine','',width,height,false,2,'');
const note=(id,text,revision=2)=>new Note(id,text,false,revision,false,1,'private',empty(),empty());
const snapshot=(notes,photos)=>new Personal(false,'viewer-a',notes,photos,'',new Capacity(notes.length,5),new Capacity(photos.length,5));
const storedKeyOf=async id=>{for(const key of toArray(await store.Keys('outbox/'))){const value=await store.Get(key);if(value?.id===id)return key;}};
const park=async(id,reason)=>{const key=await storedKeyOf(id);const value=await store.Get(key);value.parked=reason;await store.Put(key,value);};

test('queued commands describe themselves by endpoint, fold into earlier ones, and stay with their identity',async()=>{
  owner('github:u1');
  await queueChange('plant-a','viewer-a',saveEntry('n1',0,'first'));
  let pending=toArray(await pendingFor('plant-a'));
  assert.equal(pending.length,1);assert.equal(pending[0].Change.tag,0);assert.equal(pending[0].Change.fields[0].Text,'first');
  assert.deepEqual(toArray(pending[0].Item.Request.Headers),[['X-Contribution-Viewer','viewer-a']]);
  await queueChange('plant-a','viewer-a',saveEntry('n1',0,'second'));
  pending=toArray(await pendingFor('plant-a'));
  assert.equal(pending.length,1,'an edit folds into the unsent create');assert.equal(pending[0].Change.fields[0].Text,'second');
  await queueChange('plant-a','viewer-a',deleteNote('n1',0));
  assert.equal(toArray(await pendingFor('plant-a')).length,0,'deleting an unsent create cancels both');
  await queueChange('plant-a','viewer-a',saveEntry('n2',3,'edit'));
  await queueChange('plant-a','viewer-a',deleteNote('n2',3));
  pending=toArray(await pendingFor('plant-a'));
  assert.equal(pending.length,1);assert.equal(pending[0].Change.tag,2,'deleting a server note replaces the pending edit');
  await queueChange('plant-a','viewer-a',selectHero('p1'));await queueChange('plant-a','viewer-a',selectHero('p2'));
  await queueChange('plant-b','viewer-a',selectHero('p9'));
  const heroes=toArray(await pendingFor('plant-a')).filter(p=>p.Change.tag===5);
  assert.equal(heroes.length,1);assert.equal(heroes[0].Change.fields[0].Id,'p2','the latest hero choice for a plant wins');
  assert.equal(toArray(await pendingFor('plant-b')).length,1);
  owner('github:u2');assert.equal(toArray(await pendingFor('plant-a')).length,0,'another identity sees nothing');owner('github:u1');
  await queueUpload('plant-a','ph1','viewer-a','IMAGE','THUMB',1200,800);
  assert.equal(toArray(await store.Keys('media/outbox/ph1')).length,2,'the prepared pair is kept');
  const upload=toArray(await pendingFor('plant-a')).find(p=>p.Change.tag===6);
  assert.deepEqual(upload.Change.fields,['plant-a','ph1',1200,800]);
  assert.deepEqual(toArray(upload.Item.Attachments),['media/outbox/ph1/image','media/outbox/ph1/thumbnail']);
  await queueChange('plant-a','viewer-a',deletePhoto('ph1',0));
  assert.equal(toArray(await store.Keys('media/outbox/ph1')).length,0,'deleting an unsent upload forgets its bytes');
  assert.equal(toArray(await pendingFor('plant-a')).filter(p=>p.Change.tag===6).length,0);
});

test('a delivered upload stays on the phone under the identity that queued it, even if another account is current by then',async()=>{
  owner('github:u4');
  await queueUpload('plant-e','ph9','viewer-a','IMG','THUMB',10,10);
  const [pending]=toArray(await pendingFor('plant-e'));
  owner('github:u5');
  await keepDelivered(pending.Item);
  assert.deepEqual(toArray(await keptPrivateMedia(store,'github:u4')).sort(),['/api/plants/personal-media/ph9/image','/api/plants/personal-media/ph9/thumbnail']);
  assert.deepEqual(toArray(await keptPrivateMedia(store,'github:u5')),[],'nothing of one person\'s lands under another');
  owner('github:u1');
});

test('a photograph edit queued behind its upload is sent with the revision the upload created, read from the delivered snapshot',async()=>{
  owner('github:u6');
  await queueUpload('plant-g','ph2','viewer-a','IMG','THUMB',10,10);
  await queueChange('plant-g','viewer-a',new PersonalChange(2,['ph2',0,'Captioned','Me',true]));
  const [upload,details]=toArray(await pendingFor('plant-g'));
  assert.equal(details.Change.tag,3);assert.equal(details.Change.fields[0].Revision,0);
  assert.equal(JSON.parse((await prepare(details.Item)).Body).revision,0,'before the upload lands the request goes as queued');
  const delivered={personal:{anonymous:false,viewerToken:'viewer-a',notes:[],photos:[{id:'ph2',image:'/api/plants/personal-media/ph2/image',thumbnail:'/api/plants/personal-media/ph2/thumbnail',caption:'',photographer:'',width:10,height:10,offered:false,revision:1,publicPhotoId:''}],heroPhotoId:'',noteCapacity:{used:0,limit:5},photoCapacity:{used:1,limit:5}}};
  await rememberDelivered(upload.Item,new Response(200,empty(),JSON.stringify(delivered)));
  const stored=toArray(await cachedResponses(store,'github:u6','/api/plants/v2/personal/plant-g'));
  assert.equal(stored.length,1,'the delivery answer became the owner\'s saved notebook');
  const request=await prepare(details.Item);
  assert.equal(JSON.parse(request.Body).revision,1);assert.equal(request.Path,'/api/plants/v2/photos/update');
  assert.deepEqual(toArray(request.Headers),[['X-Contribution-Viewer','viewer-a']]);
  assert.equal(await supersedes('plant-g',new PersonalChange(2,['ph2',0,'Again','',false])),true,'another edit of the same photograph joins the queue');
  assert.equal(await supersedes('plant-g',new PersonalChange(2,['other',0,'','',false])),false);
  owner('github:u1');
});

test('fold rules are explicit',()=>{
  const keep=0,replace=1,cancel=2;
  assert.equal(fold(entry('a',0,'x'),entry('a',0,'y')).tag,replace);
  assert.equal(fold(entry('a',0,'x'),new Change(2,[{PlantId:'plant-c',Id:'a',Revision:0}])).tag,cancel);
  assert.equal(fold(entry('a',4,'x'),new Change(2,[{PlantId:'plant-c',Id:'a',Revision:4}])).tag,replace);
  assert.equal(fold(entry('a',0,'x'),entry('b',0,'y')).tag,keep);
  assert.equal(fold(new Change(6,['plant-c','p',1,1]),new Change(4,[{PlantId:'plant-c',Id:'p',Revision:0}])).tag,cancel);
  assert.equal(fold(new Change(5,[new SelectHero_Request('plant-c','p1')]),new Change(5,[new SelectHero_Request('plant-c','p2')])).tag,replace);
  assert.equal(fold(new Change(5,[new SelectHero_Request('plant-c','p1')]),new Change(5,[new SelectHero_Request('plant-d','p2')])).tag,keep);
  assert.ok(Fold);
});

test('the overlay shows queued changes over the snapshot and moves the local limits with them',()=>{
  const data=snapshot([note('n-server','server')],[photo('p-server')]);
  const pending=ofArray([
    new Pending(item('i1'),entry('n-new',0,'new text')),
    new Pending(item('i2'),entry('n-server',2,'edited')),
    new Pending(item('i3'),new Change(6,['plant-c','ph2',1600,1200])),
    new Pending(item('i4'),new Change(5,[new SelectHero_Request('plant-c','ph2')])),
    new Pending(item('i5'),new Change(3,[new UpdatePhoto_Request('plant-c','p-server',2,'Captioned','Me',true)])),
  ]);
  const shown=overlay(data,pending);
  assert.deepEqual(shown.Notes.map(n=>[n.Id,n.Text,n.Revision]),[['n-new','new text',0],['n-server','edited',2]]);
  assert.equal(shown.Photos.length,2);
  const added=shown.Photos.find(p=>p.Id==='ph2');
  assert.equal(added.Image.fields[0],'outbox/ph2/image');assert.equal(added.Width,1600);assert.equal(added.Revision,0);
  assert.equal(shown.Photos.find(p=>p.Id==='p-server').Caption,'Captioned');
  assert.equal(shown.HeroPhotoId,'ph2');
  assert.equal(shown.NoteCapacity.Used,2);assert.equal(shown.PhotoCapacity.Used,2);
  assert.equal(pendingState(pending,'n-new').Id,'i1');assert.equal(pendingState(pending,'nowhere'),undefined);
  assert.equal(data.Notes.length,1,'the snapshot itself is untouched');
});

test('replay decisions: delivered, retried while away or failing, parked with the server\'s words',()=>{
  const any=item('i');
  assert.equal(decide(any,ok(200,'{}')).tag,0);
  assert.equal(decide(any,ok(503,'busy')).tag,1);
  const parked=decide(any,ok(409,'{"error":"You can keep 5 notes per species."}'));
  assert.equal(parked.tag,2);assert.equal(parked.fields[0],'You can keep 5 notes per species.');
  assert.equal(decide(any,failed('Failed to fetch')).tag,1);
  const refused=decide(any,failed(sessionChangedMessage));
  assert.equal(refused.tag,2,'the session lock\'s refusal is not the network being away');
});

test('reconcile finishes parked changes the snapshot already shows; retry sends the person\'s version over the server revision',async()=>{
  owner('github:u3');
  await queueChange('plant-d','viewer-a',saveEntry('n5',0,'landed'));
  await queueChange('plant-d','viewer-a',saveEntry('n6',0,'mine'));
  let pending=toArray(await pendingFor('plant-d'));
  for(const p of pending)await park(p.Item.Id,'This item changed. Refresh and try again.');
  pending=toArray(await pendingFor('plant-d'));assert.ok(pending.every(p=>p.Item.Parked));
  await reconcile(snapshot([note('n5','landed',1),note('n6','theirs',4)],[]),ofArray(pending));
  pending=toArray(await pendingFor('plant-d'));
  assert.deepEqual(pending.map(p=>p.Change.fields[0].Id),['n6'],'the create that landed is finished; the real conflict stays');
  await retry(snapshot([note('n6','theirs',4)],[]),pending[0].Item.Id);
  pending=toArray(await pendingFor('plant-d'));
  assert.equal(pending.length,1,'no network here: the item stays queued for the next flush');
  assert.equal(pending[0].Item.Parked,undefined);
  assert.equal(pending[0].Change.fields[0].Revision,4,'resent as an edit over the server\'s current revision');
  assert.equal(pending[0].Change.fields[0].Text,'mine');
  assert.deepEqual(toArray(pending[0].Item.Request.Headers),[['X-Contribution-Viewer','viewer-a']]);
});
