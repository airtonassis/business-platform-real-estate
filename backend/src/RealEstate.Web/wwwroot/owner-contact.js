import {createPostalLookup,automaticPostalFields} from './address-editor.js';
export function documentMask(value,kind){const d=value.replace(/\D/g,'').slice(0,kind==='PJ'?14:11);return kind==='PJ'?d.replace(/^(\d{2})(\d)/,'$1.$2').replace(/^(\d{2})\.(\d{3})(\d)/,'$1.$2.$3').replace(/(\d{3})(\d)/,'$1/$2').replace(/(\d{4})(\d{1,2})$/,'$1-$2'):d.replace(/^(\d{3})(\d)/,'$1.$2').replace(/^(\d{3})\.(\d{3})(\d)/,'$1.$2.$3').replace(/(\d{3})(\d{1,2})$/,'$1-$2');}
export function ownerContact(root){
 const box=root.querySelector('[data-owner-contact]'),contact=k=>box.querySelector(`[data-contact="${k}"]`),address=k=>box.querySelector(`[data-address="${k}"]`),keys=['PostalCode','Number','Street','Complement','Neighborhood','Municipality','State'];
 const lookup=createPostalLookup(),edited=new Set();let base={},revision=0;
 for(const key of keys)address(key).addEventListener('input',()=>edited.add(key));
 const phoneMask=value=>{const d=value.replace(/\D/g,'');return d.length===10||d.length===11?d.replace(/^(\d{2})(\d{4,5})(\d{4})$/,'($1) $2-$3'):value;};
 contact('phone').addEventListener('blur',()=>contact('phone').value=phoneMask(contact('phone').value));
 address('PostalCode').addEventListener('blur',async()=>{
  const input=address('PostalCode'),digits=input.value.replace(/\D/g,'');if(digits.length===8)input.value=digits.replace(/^(\d{5})(\d{3})$/,'$1-$2');
  const ticket=++revision,cep=input.value,status=box.querySelector('[data-owner-cep-status]');status.textContent='';
  const result=await lookup.search(cep);if(ticket!==revision||input.value!==cep)return;
  if(result.state==='suggestion'){
   const current=Object.fromEntries(keys.map(k=>[k,address(k).value]));
   for(const [key,value] of Object.entries(automaticPostalFields(current,result.fields,edited)))if(keys.includes(key))address(key).value=value;
  }else if(['not-found','unavailable'].includes(result.state))status.textContent='Não foi possível consultar este CEP. Preencha o endereço manualmente.';
 });
 return {
  kind(kind){box.querySelector('[data-owner-trade]').hidden=kind!=='PJ';},
  load(details){revision++;lookup.invalidate();edited.clear();base=details??{};for(const k of ['tradeName','phone','email'])contact(k).value=base[k]??'';contact('phone').value=phoneMask(contact('phone').value);for(const k of keys)address(k).value=base.address?.[k[0].toLowerCase()+k.slice(1)]??'';address('PostalCode').value=address('PostalCode').value.replace(/^(\d{5})(\d{3})$/,'$1-$2');box.querySelector('[data-owner-cep-status]').textContent='';},
  read(){return {...base,...Object.fromEntries(['tradeName','phone','email'].map(k=>[k,contact(k).value])),address:{...base.address,...Object.fromEntries(keys.map(k=>[k[0].toLowerCase()+k.slice(1),address(k).value||null]))}};}
 };
}
