// Descriptive search (NativePlants.Descriptions): the acceptance examples from notes/NATIVE-PLANTS-keyword-search.md,
// run against the compiled pure module.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { tokens, search, fragments, canonical, searchable, weight } from '../dist/client/Core/Descriptions.js';
import { Section } from '../dist/client/Models/Api.js';
import { ofArray, toArray } from '../dist/client/fable_modules/fable-library-js.4.29.0/List.js';

const account=(id,...sections)=>[id,ofArray(sections.map(([label,text])=>new Section(label,text)))];
const accounts=ofArray([
  account('corymbia-foelscheana',['Recognise this plant','colourful bark, scaly and mostly red-grey on lower trunk, smooth and white above'],['Habitat','Open woodland on sandy soils.']),
  account('eucalyptus-miniata',['Bark','Rough, dark bark on the lower trunk.'],['Flowers','Large orange flowers in the dry season.']),
  account('mixed-notes',['Notes','Gardeners prize the colourful bark in winter.'],['References','colourful bark is discussed in Brock (2022).']),
  account('scattered',['Leaves','Bark of the twigs is colourful when young, the leaves grey.']),
  account('red-white',['Bark','Red bark, flaking.'],['Flowers','White flowers in clusters.']),
]);
const ids=matches=>toArray(matches).map(m=>m.PlantId);

test('query words are canonical, distinct, bounded and free of stop words',()=>{
  assert.deepEqual([...tokens('The colourful Bark, with bark!')],['colourful','bark']);
  assert.deepEqual([...tokens('Colorful bark')],['colourful','bark'],'the American spelling folds to the account\'s');
  assert.deepEqual([...tokens('flowers leaves')],['flower','leaf']);
  assert.deepEqual([...tokens('   ...  ')],[]);
  assert.equal(tokens('a b c d e f g h i j k').length,8);
  assert.equal(canonical('Gray'),'grey');assert.equal(canonical('Colors'),'colour');assert.equal(canonical('grass'),'grass');
  assert.equal(searchable('References'),false);assert.equal(searchable('Bark'),true);
  assert.ok(weight('Bark')<weight('Habitat'));assert.ok(weight('Habitat')<weight('Notes'));
});

test('"colourful bark" leads with the recognition text, phrase before scattered words, recognition before notes, bibliography ignored',()=>{
  const matches=toArray(search('colourful bark',accounts));
  assert.deepEqual(matches.map(m=>m.PlantId),['corymbia-foelscheana','mixed-notes','scattered']);
  assert.equal(matches[0].Label,'Recognise this plant');assert.equal(matches[0].Phrase,true);
  assert.match(matches[0].Excerpt,/^colourful bark, scaly/);
  assert.equal(matches[1].Label,'Notes','the Notes section matches; the References section never does');
  assert.equal(matches[2].Phrase,false);
  assert.deepEqual(ids(search('colorful bark',accounts)),['corymbia-foelscheana','mixed-notes','scattered'],'spelling equivalence');
});

test('words are matched whole, and never assembled across sections',()=>{
  assert.deepEqual(ids(search('red flowers',accounts)),[],'red bark plus white flowers is not red flowers');
  assert.deepEqual(ids(search('red bark',accounts)),['red-white','corymbia-foelscheana'],'the phrase first, then the account with both words in one section');
  assert.deepEqual(ids(search('grey',accounts)),['corymbia-foelscheana','scattered'],'"red-grey" splits at the hyphen; "greyish" would not match');
  assert.deepEqual(ids(search('colour',accounts)),[],'colour does not match colourful');
  assert.deepEqual(ids(search('',accounts)),[]);
});

test('excerpts keep their context and highlight the matched words as runs of text',()=>{
  const [first]=toArray(search('orange flowers',accounts));
  assert.equal(first.PlantId,'eucalyptus-miniata');assert.equal(first.Label,'Flowers');
  const runs=toArray(fragments(tokens('orange flowers'),first.Excerpt));
  assert.deepEqual(runs.filter(([,hit])=>hit).map(([text])=>text),['orange','flowers']);
  assert.equal(runs.map(([text])=>text).join(''),first.Excerpt,'the runs reassemble the excerpt exactly');
  const long='Word '.repeat(40)+'colourful bark here '+'tail '.repeat(40);
  const [m]=toArray(search('colourful bark',ofArray([account('long',['Bark',long])])));
  assert.ok(m.Excerpt.startsWith('… ') && m.Excerpt.endsWith(' …'));assert.ok(m.Excerpt.length<long.length);
});
