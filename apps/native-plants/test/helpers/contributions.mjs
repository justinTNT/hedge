import assert from 'node:assert/strict';
import {DatabaseSync} from 'node:sqlite';
import {readFileSync} from 'node:fs';
import worker from '../../dist/server/Worker.js';
const schema=readFileSync(new URL('../../schema.sql',import.meta.url),'utf8');
// Small JPEG marker fixture: parser tests deliberately exercise dimensions/metadata without a decoder.
export const jpeg=(w=300,h=220)=>new Uint8Array([255,216,255,225,0,12,...new TextEncoder().encode('GPS secret'),255,192,0,11,8,h>>8,h&255,w>>8,w&255,1,1,17,0,255,218,0,8,1,1,0,0,63,0,17,255,0,23,255,217]);

export function fixture() {
  const db=new DatabaseSync(':memory:');db.exec('PRAGMA foreign_keys=ON');db.exec(schema);
  let beforeRun=null, beforeFirst=null, beforePut=null, failPut=0, puts=0, failDelete=0, deletes=0;
  const objects=new Map();
  const statement=(sql,values=[])=>({
    bind(...args){return statement(sql,args)},
    async first(){if(beforeFirst)await beforeFirst(sql,values);return db.prepare(sql).get(...values)},
    async all(){return {results:db.prepare(sql).all(...values),meta:{},success:true}},
    async run(){if(beforeRun)await beforeRun(sql,values);const s=db.prepare(sql);if(s.columns().length)return {results:s.all(...values),meta:{},success:true};const result=s.run(...values);return {results:[],meta:{changes:Number(result.changes)},success:true}}
  });
  const env={DB:{prepare:statement,async batch(stmts){db.exec('BEGIN');try{const results=[];for(const s of stmts)results.push(await s.run());db.exec('COMMIT');return results}catch(e){db.exec('ROLLBACK');throw e}}},
    ENVIRONMENT:'development',CONTRIBUTIONS_REQUIRE_LOGIN:'false',ADMIN_KEY:'owner-fixture',GUEST_SECRET:'signed-session-secret-more-than-thirty-two-chars',OAUTH_SECRET:'separate-oauth-secret-more-than-thirty-two-chars',GOOGLE_CLIENT_ID:'fixture',GOOGLE_CLIENT_SECRET:'fixture',
    BLOBS:{async put(key,value){if(beforePut)await beforePut(key,value);puts++;if(puts===failPut)throw new Error('R2 unavailable');objects.set(key,new Uint8Array(await new Response(value).arrayBuffer()));return {key}},async get(key){const value=objects.get(key);return value?{body:value,size:value.length,httpMetadata:{contentType:'image/jpeg'}}:null},async delete(key){deletes++;if(deletes===failDelete)throw new Error('R2 delete unavailable');objects.delete(key)}},
    ASSETS:{async fetch(){return new Response('Missing',{status:404})}}};
  for(const id of ['plant-a','plant-b']) {
    const cols=db.prepare('PRAGMA table_info(plants)').all().filter(c=>c.notnull||c.pk);
    const values=cols.map(c=>c.name==='id'?id:c.name==='slug'?id:c.type==='INTEGER'?1:c.name==='scientific_name'?id:'');
    db.prepare('INSERT INTO plants ('+cols.map(c=>c.name).join(',')+') VALUES ('+cols.map(()=>'?').join(',')+')').run(...values);
  }
  const call=(path,options={})=>worker.fetch(new Request('http://plants.test'+path,options),env,{waitUntil(){}});
  const get=(path,cookie,extra={})=>call(path,{headers:{Cookie:cookie,...extra}});
  const personalPath=plant=>'/api/plants/personal/'+plant;
  const personal=async(cookie,plant='plant-a')=>get(personalPath(plant),cookie);
  const viewer=async(cookie,plant='plant-a')=>(await (await personal(cookie,plant)).json()).ViewerToken;
  async function bootstrap(){return (await call('/api/auth/me')).headers.get('set-cookie').split(';')[0]}
  async function post(cookie,data,plant='plant-a',headers={}) {
    return call(personalPath(plant),{method:'POST',headers:{Cookie:cookie,Origin:'http://plants.test','Content-Type':'application/json','X-Contribution-Viewer':await viewer(cookie,plant),...headers},body:JSON.stringify(data)});
  }
  async function upload(cookie,id='photo-a',plant='plant-a',image=jpeg(),thumb=jpeg()) {
    const form=new FormData();form.append('image',new Blob([image],{type:'image/jpeg'}),'image.jpg');form.append('thumbnail',new Blob([thumb],{type:'image/jpeg'}),'thumbnail.jpg');
    return call(personalPath(plant)+'/photos/'+id,{method:'POST',headers:{Cookie:cookie,Origin:'http://plants.test','X-Contribution-Viewer':await viewer(cookie,plant)},body:form});
  }
  const review=(data,cookie='',key=env.ADMIN_KEY)=>call('/api/plants/review',data?{method:'POST',headers:{Origin:'http://plants.test','Content-Type':'application/json',Cookie:cookie,'X-Admin-Key':key},body:JSON.stringify(data)}:{headers:{Cookie:cookie,'X-Admin-Key':key}});
  async function login(cookie,account='botanist') {
    const start=await get('/api/auth/google/login?returnTo=%2Fplants%2Fplant-a',cookie);
    const state=new URL(start.headers.get('location')).searchParams.get('state');
    const original=globalThis.fetch;
    globalThis.fetch=async(url)=>String(url).includes('/token')?Response.json({access_token:'verified'}):Response.json({id:account,name:'Botanist',picture:'',email:''});
    try {
      const response=await get('/api/auth/google/callback?code=fixture&state='+encodeURIComponent(state),cookie);
      assert.equal(response.status,302,await response.clone().text());
      return response.headers.get('set-cookie')?.split(';')[0]||cookie;
    } finally {globalThis.fetch=original}
  }
  return {db,env,call,get,personal,post,upload,review,bootstrap,login,objects,viewer,setBeforeRun:fn=>beforeRun=fn,setBeforeFirst:fn=>beforeFirst=fn,setBeforePut:fn=>beforePut=fn,failNextPut:n=>failPut=puts+n,failNextDelete:n=>failDelete=deletes+n};
}
