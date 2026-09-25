import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync,writeFileSync} from 'node:fs';
const app=new URL('../../apps/articles/dist/boundary-admin/App.js',import.meta.url);
const compiled=readFileSync(app,'utf8');
// Exercise the compiled state machine without booting React into a browser DOM.
const bootstrap=/^ProgramModule_run\(Program_withReactSynchronous.*\);$/m;
assert.ok(bootstrap.test(compiled));
const fixture=new URL('App.state-fixture.js',app);writeFileSync(fixture,compiled.replace(bootstrap,''));
const storage=new Map();
globalThis.localStorage={getItem:k=>storage.get(k)||null,setItem:(k,v)=>storage.set(k,v),removeItem:k=>storage.delete(k)};
globalThis.window={location:{pathname:'/admin',hash:'',search:''},dispatchEvent(){},addEventListener(){},removeEventListener(){}};
const {init,update,Msg,Msg_$reflection}=await import(fixture.href);
const msg=(name,...fields)=>new Msg(new Msg(0,[]).cases().indexOf(name),fields);
const result=value=>({tag:0,fields:[value]});
test('applied owner-key change clears private admin state and rejects every older completion',()=>{
  storage.set('adminKey','first');let [model]=init();
  model.Types=['old-types'];model.Records=['old-records'];model.EditRecord={secret:true};
  const epoch=model.CredentialEpoch,seq=model.FormSeq;
  storage.set('adminKey','second');[model]=update(msg('CredentialChanged'),model);
  assert.equal(model.Key,'second');assert.equal(model.Types,undefined);assert.equal(model.Records,undefined);assert.equal(model.EditRecord,undefined);assert.ok(model.FormSeq>seq);
  for(const completion of ['GotTypes','GotRecords','GotEditRecord','GotSave','GotDelete']) {
    const [next,commands]=update(msg('CredentialResult',epoch,msg(completion,result(['old-private-data']))),model);
    assert.equal(next,model);assert.equal([...commands].length,0);
  }
  const [late]=update(msg('ImageUploaded',seq,'picture','old-private-image'),model);
  assert.equal(late,model);
});
test('a changed stored key rejects a completion even before its notification is dispatched',()=>{
  storage.set('adminKey','first');const [model]=init();storage.delete('adminKey');
  const [next]=update(msg('CredentialResult',model.CredentialEpoch,msg('GotTypes',result(['old']))),model);
  assert.equal(next,model);assert.equal(next.Types,undefined);
});
test('editing a draft key does not apply it; submitting an empty key clears owner access',()=>{
  storage.set('adminKey','owner');let [model]=init();
  [model]=update(msg('KeyChanged','draft'),model);assert.equal(model.Key,'owner');assert.equal(storage.get('adminKey'),'owner');
  model.Types=['confirmed'];[model]=update(msg('KeyChanged',''),model);[model]=update(msg('SubmitKey'),model);
  assert.equal(model.Key,'');assert.equal(model.Types,undefined);assert.equal(storage.has('adminKey'),false);
});
test('storage failures do not confirm access and invalidate in-flight results',()=>{
  storage.set('adminKey','owner');let [model]=init();model.KeyDraft='new';model.Types=['confirmed'];
  const previous=localStorage.setItem;localStorage.setItem=()=>{throw Error('blocked')};
  try {
    const epoch=model.CredentialEpoch;[model]=update(msg('SubmitKey'),model);
    assert.equal(model.Types,undefined);assert.equal(model.EditRecord,undefined);assert.ok(model.Error.includes('could not be saved'));assert.ok(model.CredentialEpoch>epoch);
  }finally{localStorage.setItem=previous}
});
