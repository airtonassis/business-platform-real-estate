import {ownerContact,documentMask} from './owner-contact.js';
const form = document.querySelector('#property-form');
const editor = document.querySelector('[data-owner-editor]');
if (editor) {
    const status = editor.querySelector('[data-owner-status]');
    const field = name => editor.querySelector(`[data-owner-${name}]`);
    let busy = false;
    const details=editor.querySelector('[data-owner-new]'),contacts=details?ownerContact(details):null;
    const labels=()=>{if(!contacts)return;contacts.kind(field('kind').value);editor.querySelector('[data-owner-name-label]').textContent=field('kind').value==='PJ'?'Razão social':'Nome completo';editor.querySelector('[data-owner-document-label]').textContent=field('kind').value==='PJ'?'CNPJ':'CPF';};
    field('kind')?.addEventListener('change',labels);labels();
    field('document')?.addEventListener('blur',()=>field('document').value=documentMask(field('document').value,field('kind').value));
    field('cancel')?.addEventListener('click',()=>{details.open=false;});
    field('query').addEventListener('blur',()=>{const d=field('query').value.replace(/\D/g,'');if([11,14].includes(d.length))field('search').click();});
    field('query').addEventListener('keydown',e=>{if(e.key==='Enter'){e.preventDefault();field('search').click();}});
    async function send(handler, body) {
        const response = await fetch(location.pathname + '?handler=' + handler, {method:'POST',body:new URLSearchParams({
            __RequestVerificationToken:form.elements.namedItem('__RequestVerificationToken').value,...body})});
        if (!response.ok) {
            const error = await response.json().catch(()=>({}));
            throw Error(error.code === 'Owner.DuplicateTaxId' ? 'CPF/CNPJ já cadastrado nesta empresa. Busque e reutilize o proprietário.'
                : error.code === 'Owner.ContactInvalid' ? 'Confira o e-mail e os limites dos campos de contato.'
                : error.code === 'Owner.TaxIdInvalid' ? 'Confira o tipo de pessoa e o CPF/CNPJ informado.'
                : response.status === 401 ? 'Sua sessão expirou. Entre novamente pelo Portal AraIa antes de continuar. Os campos informados continuam nesta tela.'
                : response.status === 403 ? 'Sem permissão para esta operação.' : 'Não foi possível concluir. Revise os dados e tente novamente.');
        }
        return response.json();
    }
    function option(owner) {
        const select = form.elements.namedItem('Input.OwnerId');
        let item = [...select.options].find(o=>o.value===owner.id);
        if (!item) {item=new Option(owner.name,owner.id);select.add(item);}
        return item;
    }
    field('search').addEventListener('click', async()=>{
        if (busy) return;busy=true;status.textContent='Buscando proprietários…';
        try {
            const owners=await send('SearchOwners',{query:field('query').value});
            const list=field('results');list.replaceChildren();
            for(const owner of owners){option(owner);const button=document.createElement('button');button.type='button';button.className='button secondary';
                button.textContent=[owner.name,owner.personKind,owner.document].filter(Boolean).join(' · ');
                button.addEventListener('click',()=>{const select=form.elements.namedItem('Input.OwnerId');select.value=owner.id;select.dispatchEvent(new Event('change',{bubbles:true}));status.textContent='Proprietário selecionado. Salve o rascunho para manter o vínculo.';});list.append(button);}
            status.textContent=owners.length ? 'Selecione um resultado.' : 'Nenhum proprietário encontrado.';
            if(!owners.length&&details){
                const query=field('query').value,digits=query.replace(/\D/g,'');
                if([11,14].includes(digits.length)){field('kind').value=digits.length===14?'PJ':'PF';field('document').value=documentMask(digits,field('kind').value);labels();details.open=true;field('name').focus();status.textContent='Complete os dados do novo proprietário.';}
            }
        } catch(error){status.textContent=error.message;} finally{busy=false;}
    });
    field('create')?.addEventListener('click',async()=>{
        if(busy)return;busy=true;status.textContent='Salvando proprietário…';
        try {
            const owner=await send('CreateOwner',{ownerName:field('name').value,personKind:field('kind').value,taxId:field('document').value,ownerDetails:JSON.stringify(contacts.read())});
            const select=form.elements.namedItem('Input.OwnerId');option(owner);select.value=owner.id;select.dispatchEvent(new Event('change',{bubbles:true}));
            details.open=false;field('name').value='';field('document').value='';contacts.load(null);status.textContent='Proprietário cadastrado e selecionado. Salve o rascunho para manter o vínculo.';
        }catch(error){status.textContent=error.message;}finally{busy=false;}
    });
}
