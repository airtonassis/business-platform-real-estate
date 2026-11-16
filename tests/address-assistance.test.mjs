import {test} from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
const path=new URL('../backend/src/RealEstate.Web/wwwroot/address-editor.js',import.meta.url);
const source=fs.existsSync(path)?fs.readFileSync(path,'utf8'):'';
const module=await import('data:text/javascript;base64,'+Buffer.from(source).toString('base64'));
const sample={cep:'01001-000',logradouro:'Praça da Sé',bairro:'Sé',localidade:'São Paulo',uf:'SP',ibge:'3550308'};
const response=(data=sample)=>({ok:true,json:async()=>data});
test('lookup normalizes CEP and returns optional fields',async()=>{
    assert.equal(typeof module.createPostalLookup,'function');
    let seen;const lookup=module.createPostalLookup({fetcher:async url=>{seen=url;return response();}});
    const result=await lookup.search('01001-000');
    assert.equal(seen,'https://viacep.com.br/ws/01001000/json/');
    assert.equal(result.state,'suggestion');assert.equal(result.fields.MunicipalityIbgeCode,'3550308');
    assert.equal(result.fields.Street,'Praça da Sé');
});
test('automatic fill touches only blank fields not manually edited',()=>{
    assert.equal(typeof module.automaticPostalFields,'function');
    assert.deepEqual(module.automaticPostalFields({Street:'',Neighborhood:'Manual',Municipality:'',State:''},{Street:'Praça da Sé',Neighborhood:'Sé',Municipality:'São Paulo',State:'SP'},new Set(['Municipality'])),{Street:'Praça da Sé',State:'SP'});
});
test('invalid CEP never calls external service',async()=>{
    let calls=0;const lookup=module.createPostalLookup({fetcher:async()=>{calls++;return response();}});
    assert.equal((await lookup.search('0100A000')).state,'manual');assert.equal(calls,0);
});
test('network failure and malformed response preserve manual operation',async()=>{
    for(const fetcher of [async()=>{throw Error('offline');},async()=>response({erro:true}),async()=>response({cep:'99999-999',logradouro:'wrong'}),async()=>response([]),async()=>({ok:false})]){
        const result=await module.createPostalLookup({fetcher}).search('01001000');
        assert.ok(['unavailable','not-found'].includes(result.state));assert.equal(result.fields,undefined);
    }
});
test('timeout releases waiting even when transport ignores abort',async()=>{
    const lookup=module.createPostalLookup({fetcher:()=>new Promise(()=>{}),timeoutMs:10});
    assert.equal((await lookup.search('01001000')).state,'unavailable');
});
test('late response is discarded after manual edit or newer CEP',async()=>{
    let resolve;const lookup=module.createPostalLookup({fetcher:()=>new Promise(r=>{resolve=r;})});
    const old=lookup.search('01001000');lookup.invalidate();resolve(response());
    assert.equal((await old).state,'stale');
    const old2=lookup.search('01001000');const resolveOld=resolve;
    const next=lookup.search('01002000');resolve(response({...sample,cep:'01002-000',logradouro:'Novo'}));
    assert.equal((await next).fields.Street,'Novo');resolveOld(response());assert.equal((await old2).state,'stale');
});
test('maps uses valid coordinate pair first, safe encoded address otherwise',()=>{
    assert.equal(module.mapsUrl({Latitude:'-23.5',Longitude:'-46.6'}),'https://www.google.com/maps/search/?api=1&query=-23.5%2C-46.6');
    const url=new URL(module.mapsUrl({Street:'Rua <teste> & outra',Number:'',WithoutNumber:'true',Municipality:'São Paulo',State:'SP'}));
    assert.equal(url.origin,'https://www.google.com');assert.equal(url.searchParams.get('query'),'Rua <teste> & outra, S/N, São Paulo, SP');
    assert.equal(module.mapsUrl({Latitude:'91',Longitude:'0'}),null);
    assert.equal(module.mapsUrl({}),null);
    assert.equal(module.mapsUrl({Municipality:'São Paulo',Latitude:'',Longitude:''}).includes('0%2C0'),false);
});
