import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
const base=new URL('../backend/src/RealEstate.Web/wwwroot/',import.meta.url);
const postal='data:text/javascript;base64,'+Buffer.from(await fs.readFile(new URL('address-editor.js',base),'utf8')).toString('base64');
const code=(await fs.readFile(new URL('owner-contact.js',base),'utf8')).replace('./address-editor.js',postal);
const {ownerContact,documentMask}=await import('data:text/javascript;base64,'+Buffer.from(code).toString('base64'));
test('owner masks and CEP assistance preserve manual entry and allow unavailable provider',async()=>{
 assert.equal(documentMask('52998224725','PF'),'529.982.247-25');assert.equal(documentMask('11222333000181','PJ'),'11.222.333/0001-81');
 const elements=new Map();const get=selector=>{if(!elements.has(selector))elements.set(selector,{value:'',textContent:'',listeners:{},addEventListener(event,fn){this.listeners[event]=fn;}});return elements.get(selector);};
 const root={querySelector(){return {querySelector:get};}};const a=k=>get(`[data-address="${k}"]`),c=k=>get(`[data-contact="${k}"]`);
 const original=globalThis.fetch;
 try{
  globalThis.fetch=async()=>({ok:true,json:async()=>({cep:'01001-000',logradouro:'Praça da Sé',bairro:'Sé',localidade:'São Paulo',uf:'SP'})});
  const editor=ownerContact(root);editor.load(null);a('PostalCode').value='01001000';a('Neighborhood').value='Manual neighborhood';a('Neighborhood').listeners.input();await a('PostalCode').listeners.blur();
  assert.equal(a('Street').value,'Praça da Sé');assert.equal(a('State').value,'SP');assert.equal(a('Neighborhood').value,'Manual neighborhood');
  editor.load({phone:'11999990000',address:{postalCode:'01001000',number:'s/n'}});assert.equal(c('phone').value,'(11) 99999-0000');assert.equal(a('PostalCode').value,'01001-000');assert.equal(editor.read().address.number,'s/n');
  // A new lookup captures the unavailable transport, without touching any property fields.
  globalThis.fetch=async()=>{throw Error('Offline test');};const offline=ownerContact(root);offline.load(null);a('PostalCode').value='01001000';await a('PostalCode').listeners.blur();
  assert.match(get('[data-owner-cep-status]').textContent,/manualmente/);a('Street').value='Manual street';assert.equal(offline.read().address.street,'Manual street');
 }finally{globalThis.fetch=original;}
});
