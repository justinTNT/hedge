import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { glossaryMatcher, referenceMatcher, annotate, usageCitations } from '../dist/client/Core/Glossary.js';
import { GlossaryEntry, ReferenceEntry } from '../dist/client/Models/Api.js';
import { ofArray } from '../dist/client/fable_modules/fable-library-js.4.29.0/List.js';
const term=(name,aliases=[])=>new GlossaryEntry(name,name,ofArray(aliases),'Definition of '+name,'','Source');
const terms=ofArray([term('Glaucous'),term('Phyllode',['Phyllodes']),term('Compound leaf',['Compound leaves']),term('Flower',['Flowers']),term('Female flower',['Female flowers']),term('Nut',['Nuts']),term('Bi-pinnate',['bipinnate'])]);
test('definitions preserve text, cover repeated/plural/case variants and choose whole longest terms',()=>{
  const original='GLAUCOUS phyllodes; glaucous compound leaves; female flowers, flowers, coconut, nuts and bi–pinnate.';
  const parts=[...annotate(glossaryMatcher(terms),original)];
  assert.equal(parts.map(p=>p.Text).join(''),original);
  assert.deepEqual(parts.filter(p=>p.Note).map(p=>[p.Text,p.Note.Heading]),[['GLAUCOUS','Glaucous'],['phyllodes','Phyllode'],['glaucous','Glaucous'],['compound leaves','Compound leaf'],['female flowers','Female flower'],['flowers','Flower'],['nuts','Nut'],['bi–pinnate','Bi-pinnate']]);
});
test('numbered citations preserve punctuation and expose unresolved references without guessing',()=>{
  const references=ofArray([new ReferenceEntry('usage-1','usage',1,'Full citation',ofArray([]),'Source')]);
  const original='Flowers (1, 22); 1–2cm, (1981) and 22 leaves.';
  const parts=[...usageCitations(glossaryMatcher(terms),references,original)];
  assert.equal(parts.map(p=>p.Text).join(''),original);
  assert.deepEqual(parts.filter(p=>p.Note).map(p=>p.Text),['Flowers','1','22']);
  assert.equal(parts.find(p=>p.Text==='1').Note.Body,'Full citation');
  assert.match(parts.find(p=>p.Text==='22').Note.Body,/missing/);
});
test('named citations use explicit reference aliases',()=>{
  const refs=ofArray([new ReferenceEntry('flora','bibliography',0,'Full Flora NT entry',ofArray(['Flora NT']),'Source')]);
  const parts=[...annotate(referenceMatcher(refs),'Flora NT. Unknown source.')];
  assert.equal(parts.filter(p=>p.Note).length,1);assert.equal(parts[0].Note.Body,'Full Flora NT entry');
});
test('real glossary annotates every account without changing any original text',()=>{
  let data;try{data=JSON.parse(readFileSync(new URL('../data/catalogue-source.json',import.meta.url),'utf8'))}catch{return}
  const matcher=glossaryMatcher(ofArray(data.glossary.map(g=>new GlossaryEntry(g.id,g.term,ofArray(g.aliases?g.aliases.split('|'):[]),g.definition,g.illustration,g.source_label))));
  let occurrences=0;
  for(const plant of data.plants)for(const field of ['features','habit','bark','leaves','phyllodes','flowers','fruit','flowering','fruiting','habitat','cultivation','traditional_uses','notes','distribution']){
    const parts=[...annotate(matcher,plant[field])];
    assert.equal(parts.map(p=>p.Text).join(''),plant[field]);occurrences+=parts.filter(p=>p.Note).length;
  }
  assert.ok(occurrences>0);console.log(`Glossary occurrences across account prose: ${occurrences}`);
});
