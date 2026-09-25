import { hasPhotoCredit } from '../dist/client/Core/Catalogue.js';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { init, update, Msg, Page, readQuery, queryUrl, canEnlargePhoto, sectionId } from '../dist/client/App.js';
import { PlantCard, PlantDetail, GetCatalogue_Response, GetPlant_Response, Photo } from '../dist/client/Models/Api.js';
import { FSharpResult$2 as Result } from '../dist/client/fable_modules/fable-library-js.4.29.0/Result.js';
import { empty, ofArray } from '../dist/client/fable_modules/fable-library-js.4.29.0/List.js';

globalThis.window = {location:{pathname:'/plants',search:''}};
globalThis.document = {title:''};
const ok = value => new Result(0,[value]);
const failed = new Result(1,['Unavailable']);
const list = empty();
const card = new PlantCard('stable-id','acacia-example','Acacia example','Example wattle','Fabaceae','Acacia',list,list,'',list,list,list,list,false,'',undefined,list);
const account = new PlantDetail(card,list,list,list);
const catalogue = new GetCatalogue_Response(ofArray([card]),list,list,list,list,list,list,'revision-a',15,list,list,list);
function model() {
  const [m] = init();
  m.Data = catalogue;
  m.Loading = false;
  return m;
}

test('superseded catalogue and detail requests cannot replace the current view',()=>{
  const m=model(); m.Request=4; m.DetailRequest=7; m.Page=new Page(3,[card.Id]);
  assert.equal(update(new Msg(0,[3,ok(catalogue)]),m)[0],m);
  assert.equal(update(new Msg(1,[6,card.Id,ok(new GetPlant_Response(account))]),m)[0],m);
  assert.equal(update(new Msg(1,[7,'a-different-id',ok(new GetPlant_Response(account))]),m)[0],m);
  assert.equal(update(new Msg(1,[7,card.Id,ok(new GetPlant_Response(account))]),m)[0].Plant,account);
});

test('a failed revision check clears content and rejects an already pending detail',()=>{
  const m=model(); m.Page=new Page(3,[card.Id]); m.Plant=account; m.Polling=true;
  const [cleared]=update(new Msg(2,[failed]),m);
  assert.equal(cleared.Data,undefined); assert.equal(cleared.Plant,undefined);
  const [late]=update(new Msg(1,[m.DetailRequest,card.Id,ok(new GetPlant_Response(account))]),cleared);
  assert.equal(late.Plant,undefined);
  const [retry,commands]=update(new Msg(5,[]),late);
  assert.equal(retry.Loading,true); assert.ok([...commands].length>0);
});

test('keyboard suggestions open a stable account link; escape restores plain search',()=>{
  let m=model();
  [m]=update(new Msg(6,['wattle']),m);
  [m]=update(new Msg(8,['ArrowDown']),m);
  assert.equal(m.Active,0);
  let commands=update(new Msg(9,[]),m)[1];
  const dispatched=[];
  for(const command of commands)command(msg=>dispatched.push(msg));
  assert.equal(dispatched[0].fields[0],'/plants/stable-id/acacia-example');
  [m]=update(new Msg(8,['Escape']),m);
  commands=update(new Msg(9,[]),m)[1]; dispatched.length=0;
  for(const command of commands)command(msg=>dispatched.push(msg));
  assert.equal(dispatched[0].fields[0],'/plants?q=wattle');
});

test('shared filter URLs round-trip encoded names and grouped facets',()=>{
  window.location.search='?q=red%20%26%20yellow&family=Fabaceae&form=Tree%7CShrub&endemic=true';
  const query=readQuery();
  assert.equal(query.Q,'red & yellow'); assert.equal(query.Form,'Tree|Shrub');
  assert.equal(queryUrl(query),'/plants?q=red%20%26%20yellow&family=Fabaceae&form=Tree%7CShrub&endemic=true');
  window.location.search='';
});

test('photographer filter URLs round-trip names and combine with botanical filters',()=>{
  window.location.search='?family=Fabaceae&photographer=Jeremy%20Russell-Smith';
  const query=readQuery();
  assert.equal(query.Photographer,'Jeremy Russell-Smith');
  assert.equal(queryUrl(query),'/plants?family=Fabaceae&photographer=Jeremy%20Russell-Smith');
  window.location.search='';
});

test('either dimension below 200 prevents photograph preview',()=>{
  for(const [w,h,expected] of [[199,500,false],[500,199,false],[200,200,true],[0,0,false]])
    assert.equal(canEnlargePhoto(w,h),expected);
  const photo=new Photo('photo','/full.webp','/thumb.webp','Leaves','');
  const zoom=new Msg(14,[photo]);
  let m=model();assert.equal(update(zoom,m)[0].Zoom,undefined);
  [m]=update(new Msg(15,[photo.Image,199,500]),m);
  assert.equal(update(zoom,m)[0].Zoom,undefined);
  [m]=update(new Msg(15,[photo.Image,200,200]),m);
  assert.equal(update(zoom,m)[0].Zoom,photo);
});

test('unknown photo credits stay silent',()=>{
  for(const credit of ['', ' ', 'Photographer not recorded', ' photographer NOT recorded '])assert.equal(hasPhotoCredit(credit),false);
  assert.equal(hasPhotoCredit('Kym Brennan'),true);
});

test('fragment navigation preserves the loaded account; a different page still changes route',()=>{
  const m=model(); m.Page=new Page(3,[card.Id]); m.Plant=account;
  window.location.pathname='/plants/'+card.Id+'/acacia-example';
  window.location.hash='#section-references';
  const [same,commands]=update(new Msg(4,[]),m);
  assert.equal(same,m);
  assert.equal(same.Plant,account);
  assert.equal([...commands].length,0);
  window.location.pathname='/taxonomy';
  const [changed]=update(new Msg(4,[]),m);
  assert.equal(changed.Page.tag,2);
  assert.equal(changed.Plant,undefined);
  window.location.pathname='/plants'; window.location.hash='';
});

test('section anchors use readable IDs without spaces or URL-encoding artifacts',()=>{
  assert.equal(sectionId('References'),'section-references');
  assert.equal(sectionId('Growing this plant'),'section-growing-this-plant');
  assert.equal(sectionId('Aboriginal uses · source account'),'section-aboriginal-uses-source-account');
});
