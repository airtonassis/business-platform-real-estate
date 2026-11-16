import {ownerContact,documentMask} from './owner-contact.js';
import {updateLinkedCondominium} from './condominium-editor.js';
const dialog=document.querySelector('#shared-profile-dialog'),property=document.querySelector('#property-form');
if(dialog&&property){
    const form=dialog.querySelector('form'),status=form.querySelector('[role=status]');let type,id,version,busy=false;
    const named=name=>form.elements.namedItem(name);
    const contacts=ownerContact(form);named('personKind').addEventListener('change',()=>contacts.kind(named('personKind').value));named('taxId').addEventListener('blur',()=>named('taxId').value=documentMask(named('taxId').value,named('personKind').value));
    async function send(handler,data){const r=await fetch(location.pathname+'?handler='+handler,{method:'POST',body:new URLSearchParams({__RequestVerificationToken:property.elements.namedItem('__RequestVerificationToken').value,...data})});
        if(!r.ok){const body=await r.json().catch(()=>({}));throw Error(r.status===412?'O cadastro mudou. Reabra a edição antes de salvar.':r.status===403?'Sem permissão para editar este cadastro.':body.code==='Owner.DuplicateTaxId'?'CPF/CNPJ já utilizado nesta empresa.':'Revise os dados e tente novamente.');}return r.json();}
    const normalize=value=>value.normalize('NFD').replace(/\p{M}/gu,'').trim().toUpperCase();
    function custom(group,option){const label=document.createElement('label');label.className='capture-chip';label.dataset.sharedChoice=option.optionId;const checkbox=document.createElement('input');checkbox.type='checkbox';checkbox.value=option.optionId;const span=document.createElement('span');span.textContent=option.label;const detail=document.createElement('input');detail.dataset.sharedDetail='';detail.maxLength=2000;detail.setAttribute('aria-label','Detalhes de '+option.label);label.append(checkbox,span,detail);group.querySelector('[data-shared-choices]').append(label);return label;}
    for(const group of form.querySelectorAll('[data-shared-group]')){
        group.querySelector('[data-shared-other]').onclick=()=>{group.querySelector('[data-shared-new]').hidden=false;group.querySelector('[data-shared-label]').focus();};
        group.querySelector('[data-shared-add]').onclick=()=>{const input=group.querySelector('[data-shared-label]'),label=input.value.trim();if(!label)return;
            if([...group.querySelectorAll('.capture-chip span')].some(s=>normalize(s.textContent)===normalize(label))){status.textContent='Esta opção já existe neste grupo.';return;}
            const choice=custom(group,{optionId:'custom:'+crypto.randomUUID(),label});choice.querySelector('[type=checkbox]').checked=true;input.value='';group.querySelector('[data-shared-new]').hidden=true;};
    }
    for(const button of document.querySelectorAll('[data-edit-shared]'))button.onclick=async()=>{
        if(busy)return;type=button.dataset.editShared;id=property.elements.namedItem(type==='Owner'?'Input.OwnerId':'Input.Capture.Condominium.Id').value;if(!id)return;
        busy=true;try{const profile=await send('Read'+type+'Profile',{profileId:id});version=profile.version;
            form.reset();status.textContent='';form.querySelector('[data-profile-owner]').hidden=type!=='Owner';form.querySelector('[data-profile-condominium]').hidden=type!=='Condominium';
            for(const e of form.querySelectorAll('[data-profile-owner] input,[data-profile-owner] select'))e.disabled=type!=='Owner';
            for(const e of form.querySelectorAll('[data-profile-condominium] input,[data-profile-condominium] select,[data-profile-condominium] textarea'))e.disabled=type!=='Condominium';
            const data=profile.data??profile;named('name').value=data.name;
            if(type==='Owner'){named('personKind').value=data.personKind;named('taxId').value=documentMask(data.taxId,data.personKind);contacts.load(data.details);contacts.kind(data.personKind);}
            else {for(const key of ['kind','towers','floors','notes'])named(key).value=data[key]??'';
                form.querySelectorAll('[data-shared-choice^="custom:"]').forEach(e=>e.remove());
                for(const feature of data.characteristics??[]){const group=[...form.querySelectorAll('[data-shared-group]')].find(g=>g.dataset.sharedGroup===feature.groupId);if(!group)continue;
                    const choice=[...group.querySelectorAll('[data-shared-choice]')].find(c=>c.dataset.sharedChoice===feature.optionId)??custom(group,feature);choice.querySelector('[type=checkbox]').checked=feature.selected;choice.querySelector('[data-shared-detail]').value=feature.detail??'';}
            }
            const history=form.querySelector('[data-profile-history]');history.replaceChildren();for(const revision of profile.history??[]){const p=document.createElement('p');p.textContent=`Versão ${revision.version} · ${revision.actor} · ${new Date(revision.at).toLocaleString()}`;const snapshot=document.createElement('pre');let data=JSON.parse(revision.data);if(data.taxId)data.taxId='•••'+data.taxId.slice(-4);snapshot.textContent=Object.entries({Nome:data.name,Pessoa:data.personKind,Documento:data.taxId,Fantasia:data.details?.tradeName,Telefone:data.details?.phone,Email:data.details?.email,Endereço:data.details?.address?Object.values(data.details.address).filter(v=>v!==null&&v!==undefined&&v!=='').join(', '):undefined,Tipo:data.kind,Torres:data.towers,Pavimentos:data.floors,Observa\u00e7\u00f5es:data.notes,Caracter\u00edsticas:data.characteristics?.filter(c=>c.selected).map(c=>c.label+(c.detail?' ('+c.detail+')':'')).join(', ')}).filter(([,v])=>v!==undefined&&v!==null).map(([k,v])=>k+': '+v).join('\n');const details=document.createElement('details');const summary=document.createElement('summary');summary.textContent='Dados desta versão';details.append(summary,snapshot);history.append(p,details);}
            dialog.showModal();
        }catch(error){button.parentElement.querySelector('[role=status]').textContent=error.message;}finally{busy=false;}};
    form.querySelector('[data-shared-cancel]').onclick=()=>dialog.close();
    form.onsubmit=async event=>{event.preventDefault();if(busy)return;busy=true;status.textContent='Salvando…';
        try{let command={profileId:id,profileVersion:String(version)};
            if(type==='Owner')Object.assign(command,{ownerName:named('name').value,personKind:named('personKind').value,taxId:named('taxId').value,ownerDetails:JSON.stringify(contacts.read())});
            else {const characteristics=[];for(const group of form.querySelectorAll('[data-shared-group]'))for(const choice of group.querySelectorAll('[data-shared-choice]')){const selected=choice.querySelector('[type=checkbox]').checked,detail=choice.querySelector('[data-shared-detail]').value||null;
                if(selected||detail||choice.dataset.sharedChoice.startsWith('custom:'))characteristics.push({groupId:group.dataset.sharedGroup,optionId:choice.dataset.sharedChoice,label:choice.querySelector('span').textContent,selected,detail});}
                command.profileData=JSON.stringify({name:named('name').value,kind:named('kind').value,towers:named('towers').value?Number(named('towers').value):null,floors:named('floors').value?Number(named('floors').value):null,notes:named('notes').value||null,characteristics});}
            const profile=await send('Update'+type+'Profile',command);
            if(type==='Owner'){const field=property.elements.namedItem('Input.OwnerId');const option=[...field.options].find(o=>o.value===id);if(option)option.textContent=profile.name;field.dispatchEvent(new Event('change',{bubbles:true}));}
            else {updateLinkedCondominium(profile,document.querySelector('[data-condominium-editor]'),property);}
            dialog.close();
        }catch(error){status.textContent=error.message;}finally{busy=false;}};
}
