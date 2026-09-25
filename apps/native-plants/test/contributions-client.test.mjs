import { test } from 'node:test';
import assert from 'node:assert/strict';
import { empty as blank, enter, update, Msg, compose, view, uploadTile, galleryFeedback, photoEditor } from '../dist/client/Personal.js';
import { Personal, Photo as PersonalPhoto, Note, Capacity } from '../dist/client/Models/Contributions.js';
import { PlantCard, PlantDetail, Photo } from '../dist/client/Models/Api.js';
import { Msg as AppMsg, Page, init, update as updateApp, detail, changeRoute } from '../dist/client/App.js';
import { Msg as AuthMsg } from '../dist/client/Auth.js';
import { FSharpResult$2 as Result } from '../dist/client/fable_modules/fable-library-js.4.29.0/Result.js';
import { empty, ofArray } from '../dist/client/fable_modules/fable-library-js.4.29.0/List.js';
const ok=value=>new Result(0,[value]);
const own=(id='own',publicId='')=>new PersonalPhoto(id,'/api/image/'+id,'/api/thumb/'+id,'My flowers','',300,220,false,1,publicId);
const state=(token='viewer-a',photos=[own()],hero='own')=>new Personal(false,token,[],photos,hero,new Capacity(0,5),new Capacity(photos.length,5));

test('private loads and saves cannot repopulate a notebook after navigation or clearing',()=>{
  let [model]=enter('plant-a',3);const [other]=enter('plant-b',4);
  assert.equal(update(new Msg(1,[3,ok(state())]),other)[0],other);
  const cleared=blank(4);
  assert.equal(update(new Msg(18,[3,'Saved',ok(state())]),cleared)[0],cleared);
});

test('an owner change clears private drafts while a same-owner refresh preserves them',()=>{
  let [model]=enter('plant-a',2);[model]=update(new Msg(1,[2,ok(state())]),model);
  [model]=update(new Msg(2,[]),model);[model]=update(new Msg(4,['unfinished']),model);
  const draft=model.Draft;
  [model]=update(new Msg(1,[2,ok(state())]),model);assert.deepEqual(model.Draft,draft);
  [model]=update(new Msg(1,[2,ok(state('viewer-b'))]),model);assert.equal(model.Draft,undefined);
});

test('private gallery appends all own photos, selects a personal hero and deduplicates promoted photos',()=>{
  const original=new Photo('site','/site','/site-thumb','Site selection','');
  const promoted=new Photo('public-own','/public','/public-thumb','Selected photo','');
  const list=empty();
  const card=new PlantCard('plant-a','plant-a','Plant A','','Family','Genus',list,list,'',list,list,list,list,false,'',original,list);
  const plant=new PlantDetail(card,list,ofArray([original,promoted]),list);
  const selected=compose(state('viewer',[own('own','public-own'),own('extra')]),plant);
  assert.equal(selected.Card.Photo.Id,'public-own');assert.deepEqual([...selected.Photos].map(p=>p.Id),['site','public-own','personal-extra']);
  assert.equal(plant.Card.Photo.Id,'site');assert.equal([...plant.Photos].length,2);
  assert.equal(compose(state('viewer',[],'deleted'),plant).Card.Photo.Id,'site');
});

test('logout clears personal data, drafts and private lightboxes in the app model',()=>{
  globalThis.window={location:{pathname:'/plants/plant-a',search:''}};globalThis.document={title:''};
  let [model]=init();model.Page=new Page(3,['plant-a']);model.Personal={...blank(4),PlantId:'plant-a',Data:state()};model.Zoom=new Photo('private','/private','/private','','');
  [model]=updateApp(new AppMsg(16,[new AuthMsg(5,[])]),model);
  assert.equal(model.Personal.Data,undefined);assert.equal(model.Personal.PlantId,'');assert.equal(model.Zoom,undefined);
  [model]=updateApp(new AppMsg(17,[new Msg(18,[4,'Saved',ok(state())])]),model);
  assert.equal(model.Personal.Data,undefined);
});


test('a hero change does not discard an unfinished note; a successful note save clears only that draft',()=>{
  let [model]=enter('plant-a',2);[model]=update(new Msg(1,[2,ok(state())]),model);
  [model]=update(new Msg(2,[]),model);[model]=update(new Msg(4,['unfinished']),model);
  const draft=model.Draft;
  [model]=update(new Msg(16,['own']),model);
  [model]=update(new Msg(18,[model.Epoch,'Hero selected',ok(state())]),model);assert.deepEqual(model.Draft,draft);
  [model]=update(new Msg(7,[]),model);
  [model]=update(new Msg(18,[model.Epoch,'Note saved',ok(state())]),model);assert.equal(model.Draft,undefined);
});


test('anonymous session refresh, route changes and logout never load or reopen a notebook',async()=>{
  const {IdentityData,GuestSessionData,SessionReadiness}=await import('../dist/client/packages/hedge/src/Client/GuestSession.js');
  globalThis.window={location:{pathname:'/plants/plant-a',search:''}};globalThis.document={title:''};
  let [model]=init();
  const session=identity=>new SessionReadiness(true,new GuestSessionData('guest','','','','',identity));
  let commands;
  [model,commands]=updateApp(new AppMsg(16,[new AuthMsg(1,[model.Auth.Request,session(undefined)])]),model);
  assert.equal(model.Personal.PlantId,'');
  // Permission discovery may run for an owner key; no private-notebook request may run.
  { const messages=[];for(const command of commands)command(msg=>messages.push(msg));
    assert.deepEqual(messages.map(msg=>[msg.tag,msg.fields[0].tag]),[[19,0]]); }
  [model]=changeRoute(model);assert.equal(model.Personal.PlantId,'');assert.equal(model.Personal.Loading,false);
  const before=model;
  [model,commands]=updateApp(new AppMsg(17,[new Msg(2,[])]),model);
  assert.equal(model,before);assert.equal([...commands].length,0);
  const google=new IdentityData('google-id','google','Researcher','');
  [model,commands]=updateApp(new AppMsg(16,[new AuthMsg(1,[model.Auth.Request,session(google)])]),model);
  assert.equal(model.Personal.PlantId,'plant-a');assert.equal(model.Personal.Loading,true);assert.ok([...commands].length>0);
  [model]=updateApp(new AppMsg(16,[new AuthMsg(5,[])]),model);
  [model,commands]=updateApp(new AppMsg(16,[new AuthMsg(6,[true])]),model);
  assert.equal(model.Personal.PlantId,'');
  // Permission discovery may run for an owner key; no private-notebook request may run.
  { const messages=[];for(const command of commands)command(msg=>messages.push(msg));
    assert.deepEqual(messages.map(msg=>[msg.tag,msg.fields[0].tag]),[[19,0]]); }
});

test('anonymous species pages render only the curated gallery; verified users get their notebook and personal photos',async()=>{
  const {renderToStaticMarkup}=await import('react-dom/server');
  const {IdentityData}=await import('../dist/client/packages/hedge/src/Client/GuestSession.js');
  globalThis.window={location:{pathname:'/plants/plant-a',search:''}};globalThis.document={title:''};
  const sitePhoto=new Photo('site','/site','/site-thumb','Site selection','');
  const list=empty();
  const card=new PlantCard('plant-a','plant-a','Plant A','','Family','Genus',list,list,'',list,list,list,list,false,'',sitePhoto,list);
  const plant=new PlantDetail(card,list,ofArray([sitePhoto]),list);
  let [model]=init();model.Personal={...blank(1),PlantId:'plant-a',Data:state()};
  const anon=renderToStaticMarkup(detail(model,plant,()=>{}));
  for(const hidden of ['personal-content','personal-photo-controls','/api/image/own','Add a note','Add a photograph','Your field notebook'])assert.ok(!anon.includes(hidden),hidden);
  assert.ok(anon.includes('/site'));
  model.Auth={...model.Auth,Account:new IdentityData('google-id','google','Researcher',''),Loading:false};
  const signedIn=renderToStaticMarkup(detail(model,plant,()=>{}));
  for(const visible of ['personal-content','personal-photo-controls','/api/image/own','Add a note','Add a photograph'])assert.ok(signedIn.includes(visible),visible);
  assert.ok(!signedIn.includes('without logging in'));
  assert.ok(signedIn.indexOf('photo-upload-slot')<signedIn.indexOf('personal-content'));
  const notebook=renderToStaticMarkup(view(model.Personal,()=>{},()=>{}));
  assert.doesNotMatch(notebook,/type="file"|Add a photograph|photos per species/);
  const noted={...model.Personal,Data:{...model.Personal.Data,Notes:[new Note('note','A field observation',false,1,false,1)]}};
  const noteHtml=renderToStaticMarkup(view(noted,()=>{},()=>{}));
  assert.match(noteHtml,/aria-label="Edit note"/);assert.match(noteHtml,/aria-label="Delete note"/);
  assert.doesNotMatch(noteHtml,/>Edit note<|>Delete note</);
  const [editing]=update(new Msg(9,[own()]),model.Personal);
  assert.match(renderToStaticMarkup(photoEditor(editing,()=>{})),/id="photo-editor"/);
  assert.doesNotMatch(renderToStaticMarkup(view(editing,()=>{},()=>{})),/id="photo-editor"/);
});


test('limits remain quiet until an addition is attempted, and follow server capacity after an admin change',async()=>{
  const {renderToStaticMarkup}=await import('react-dom/server');
  const data=state('viewer',Array.from({length:4},(_,i)=>own('photo-'+i)));
  data.Notes=[new Note('existing','Keep this note',false,1,false,1)];
  data.NoteCapacity=new Capacity(5,5);data.PhotoCapacity=new Capacity(5,5);
  let [model]=enter('plant-a',1);[model]=update(new Msg(1,[1,ok(data)]),model);
  const before=renderToStaticMarkup(view(model,()=>{},()=>{}));
  assert.doesNotMatch(before,/5\/5|Delete a note to add|contribution-capacity|type="file"/);
  assert.doesNotMatch(before,/<button[^>]*disabled[^>]*>Add a note<\/button>/);
  let commands;
  [model,commands]=update(new Msg(2,[]),model);
  assert.equal(model.Draft,undefined);assert.equal([...commands].length,0);
  assert.match(renderToStaticMarkup(view(model,()=>{},()=>{})),/5 notes per species.*Delete a note/);
  [model,commands]=update(new Msg(19,[]),model); // camera-plus at capacity
  assert.equal([...commands].length,0);
  assert.match(renderToStaticMarkup(galleryFeedback(model)),/5 photos per species.*Delete one/);
  assert.doesNotMatch(renderToStaticMarkup(view(model,()=>{},()=>{})),/5 photos per species/);
  [model,commands]=update(new Msg(17,[{}]),model); // capacity still guards late picker results
  assert.equal(model.Busy,false);assert.equal([...commands].length,0);
  const available={...data,NoteCapacity:new Capacity(4,5),PhotoCapacity:new Capacity(4,5)};
  [model]=update(new Msg(1,[1,ok(available)]),model);
  assert.ok(update(new Msg(2,[]),model)[0].Draft);
  let picked=0;
  globalThis.document={getElementById:id=>{assert.equal(id,'photo-upload-plant-a');return {click(){picked++}}}};
  const tile=uploadTile(model,()=>{});
  tile.props.children[0].props.onClick();assert.equal(picked,1);
});

test('full notebooks still allow editing old notes while preserving a new draft that loses its last slot',()=>{
  let [model]=enter('plant-a',1);[model]=update(new Msg(1,[1,ok(state())]),model);
  [model]=update(new Msg(2,[]),model);[model]=update(new Msg(4,['Unfinished note']),model);
  const full={...model.Data,NoteCapacity:new Capacity(5,5)};
  [model]=update(new Msg(1,[1,ok(full)]),model);
  let commands;[model,commands]=update(new Msg(7,[]),model);
  assert.equal(model.Busy,false);assert.equal(model.Draft.Text,'Unfinished note');assert.equal([...commands].length,0);
  [model]=update(new Msg(3,[new Note('existing','Still editable',false,1,false,1)]),model);
  [model,commands]=update(new Msg(7,[]),model);
  assert.equal(model.Busy,true);assert.equal(model.PendingAction,'saveNote');assert.ok([...commands].length>0);
});
