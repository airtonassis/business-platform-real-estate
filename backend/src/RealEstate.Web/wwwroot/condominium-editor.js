export function updateLinkedCondominium(profile,editor,form){
    const named=name=>form.elements.namedItem('Input.Capture.Condominium.'+name);
    named('Id').value=profile?.id??'';named('ObservedVersion').value=profile?.version??'';
    editor.dataset.shared=JSON.stringify(profile?.data.characteristics??[]);
    editor.dataset.sharedName=profile?.data.name??'';
    editor.querySelector('[data-condominium-current]').textContent=profile?.data.name??'Não informado';
    const provenance=editor.querySelector('[data-condominium-provenance]');
    provenance.textContent=profile?'Origem compartilhada: '+profile.id+' · versão '+profile.version:'';
    provenance.hidden=!profile;
    if(!profile)named('Inherit').value='false'; // Explicit unlink only; individual data/overrides stay intact.
    editor.dispatchEvent(new Event('condominium:changed',{bubbles:true}));
    named('Id').dispatchEvent(new Event('change',{bubbles:true}));
}
const editor=document.querySelector('[data-condominium-editor]'),form=document.querySelector('#property-form');
if(editor){
    const field=name=>editor.querySelector(`[data-condominium-${name}]`),status=editor.querySelector('[role=status]');let busy=false;
    const link=profile=>{updateLinkedCondominium(profile,editor,form);status.textContent='Condomínio selecionado. Salve o rascunho para manter o vínculo.';};
    async function send(handler,data){const response=await fetch(location.pathname+'?handler='+handler,{method:'POST',body:new URLSearchParams({__RequestVerificationToken:form.elements.namedItem('__RequestVerificationToken').value,...data})});if(!response.ok)throw Error('Não foi possível concluir. Confira os dados e a permissão de cadastro.');return response.json();}
    function results(profiles){field('results').replaceChildren();for(const p of profiles){const button=document.createElement('button');button.type='button';button.className='button secondary';button.textContent=p.data.name;button.addEventListener('click',()=>link(p));field('results').append(button);}}
    field('clear').addEventListener('click',()=>{updateLinkedCondominium(null,editor,form);status.textContent='Vínculo removido do formulário. Seleções individuais preservadas. Salve para manter a alteração.';});
    field('search').addEventListener('click',async()=>{if(busy)return;busy=true;try{const profiles=await send('SearchCondominiums',{query:field('query').value});results(profiles);status.textContent=profiles.length?'Selecione um condomínio.':'Nenhum condomínio encontrado.';}catch(e){status.textContent=e.message;}finally{busy=false;}});
    field('create')?.addEventListener('click',async()=>{if(busy)return;busy=true;try{
        const name=field('name').value.trim();const profiles=await send('SearchCondominiums',{query:name});
        if(profiles.length){results(profiles);status.textContent='Confira os cadastros encontrados e reutilize o condomínio correspondente. Se forem distintos, diferencie o nome do novo cadastro.';return;}
        const profile=await send('CreateCondominium',{condominiumName:name,kind:field('kind').value,towers:field('towers').value,floors:field('floors').value,notes:field('notes').value});link(profile);
    }catch(e){status.textContent=e.message;}finally{busy=false;}});
}
