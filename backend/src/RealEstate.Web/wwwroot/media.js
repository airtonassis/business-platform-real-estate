'use strict';
import {offerSessionRecovery,prepareSessionRecovery} from './session-recovery.js';
const gallery=document.querySelector('[data-media-gallery]');
for(const field of document.querySelectorAll('[data-image-description]'))field.addEventListener('input',()=>{
    field.setCustomValidity(field.value!==field.dataset.originalDescription&&field.value.length>200?'Use até 200 caracteres na descrição.':'');
});
if(gallery){
    const copying=gallery.dataset.propertyCopy==='true',propertyForm=document.querySelector('#property-form');
    const stage=(name,value)=>{const h=document.createElement('input');h.type='hidden';h.name=name;h.value=value;propertyForm.append(h);};
    const lightbox=gallery.querySelector('[data-media-lightbox]');
    for(const button of gallery.querySelectorAll('[data-media-enlarge]'))button.addEventListener('click',()=>{
        const image=lightbox.querySelector('img'),source=button.querySelector('img');
        image.src=source.src;image.alt=source.alt;
        lightbox.querySelector('p').textContent=button.dataset.description||'Sem descrição.';
        lightbox.showModal();
    });
    lightbox.addEventListener('click',event=>{if(event.target===lightbox)lightbox.close();});
    const status=gallery.querySelector('#media-status'),upload=gallery.querySelector('[data-media-upload]');let dirty=false,busy=false;
    document.querySelector('#property-form')?.addEventListener('input',()=>{dirty=true;});
    document.querySelector('#property-form')?.addEventListener('change',()=>{dirty=true;});
    for(const field of gallery.querySelectorAll('[data-image-description]'))field.addEventListener('input',()=>{dirty=true;});
    const messages={'Media.VersionConflict':'A galeria mudou. Reabra o imóvel antes de tentar novamente.','Media.TooLarge':'Limite de arquivo, lote ou cota excedido.','Media.InvalidContent':'Arquivo inválido. Confira formato, conteúdo e tamanho.','Media.InvalidOrder':'A ordem enviada não corresponde à galeria atual.','Access.Forbidden':'Você não possui permissão para esta operação.'};
    async function saveDraftForUpload(){
        const form=document.querySelector('#property-form');
        await prepareSessionRecovery(form);
        if(!form.reportValidity())throw Error('Revise os campos indicados. Seus dados e arquivos continuam nesta tela.');
        status.textContent='Salvando o rascunho antes de enviar as fotos…';
        let response;
        try{const values=new URLSearchParams(new FormData(form));values.set('Step','9');response=await fetch(form.action,{method:'POST',body:values,credentials:'same-origin'});}
        catch{throw Error('Não foi possível confirmar o rascunho. Confira a lista de imóveis antes de tentar novamente. Seus campos e arquivos continuam selecionados.');}
        const doc=new DOMParser().parseFromString(await response.text(),'text/html');
        if(!response.ok){
            const message=response.status===401?'Seu acesso precisa ser retomado. Seus campos e arquivos continuam selecionados.':doc.querySelector('[role=alert]')?.textContent.trim()||'Não foi possível salvar o rascunho. Seus campos e arquivos continuam selecionados.';
            const reference=response.headers.get('X-Request-ID');
            const error=Error(message+(reference?' Referência: '+reference+'.':''));error.authenticationRequired=response.status===401;throw error;
        }
        const path=new URL(response.url).pathname,savedGallery=doc.querySelector('[data-media-gallery]'),version=doc.querySelector('[name=Version]')?.value;
        if(!/^\/properties\/[0-9a-f-]{36}$/i.test(path)||!savedGallery?.dataset.revision||!version)
            throw Error('Não foi possível confirmar o rascunho. Seus campos e arquivos continuam selecionados. Confira a lista de imóveis antes de tentar novamente.');
        // Adopt the actual persisted UUID/revision immediately, including if the subsequent upload fails.
        form.action=path+'?step=9';upload.action=path+'?handler=Upload';form.elements.namedItem('Version').value=version;
        form.elements.namedItem('Input.Code').value=doc.querySelector('[name="Input.Code"]')?.value??'';
        const code=document.querySelector('#commercial-code');if(code)code.value=doc.querySelector('#commercial-code')?.value??'';
        const help=document.querySelector('#commercial-code-help');if(help)help.textContent=doc.querySelector('#commercial-code-help')?.textContent??'';
        const token=doc.querySelector('#property-form [name=__RequestVerificationToken]').value;
        for(const field of document.querySelectorAll('[name=__RequestVerificationToken]'))field.value=token;
        gallery.dataset.revision=savedGallery.dataset.revision;gallery.dataset.newProperty='false';dirty=false;
        history.replaceState(null,'',path+'?step=9#gallery');status.textContent='Rascunho salvo. Enviando mídias…';
        return {path,token};
    }
    async function send(url,body,token){
        if(copying){
            if(body instanceof FormData){status.textContent='Arquivos selecionados. Serão enviados ao salvar o novo imóvel.';return;}
            const id=body.get('mediaId');
            if(url.includes('Remove')){stage('CopyExcluded',id);gallery.querySelector('[data-media-id="'+id+'"]')?.remove();}
            if(url.includes('Cover')){
                propertyForm.elements.namedItem('CopyCover').value=id;
                gallery.querySelectorAll('[data-cover]').forEach(e=>e.remove());
                const badge=document.createElement('span');badge.className='badge';badge.dataset.cover='';badge.textContent='Capa';
                gallery.querySelector('[data-media-id="'+id+'"] p')?.append(badge);
            }
            if(url.includes('Reorder')){
                propertyForm.querySelectorAll('[name=CopyOrder]').forEach(e=>e.remove());
                for(const value of body.getAll('order')){stage('CopyOrder',value);gallery.querySelector('.media-grid')?.append(gallery.querySelector('[data-media-id="'+value+'"]'));}
            }
            status.textContent='Alteração preparada. Salve o novo imóvel para confirmar.';return;
        }
        const creating=gallery.dataset.newProperty==='true';let created=false;
        if(busy)return;if(!creating&&dirty&&!confirm('Há alterações não salvas no cadastro. Continuar recarregará a página e descartará essas alterações.'))return;
        busy=true;gallery.setAttribute('aria-busy','true');status.textContent='Processando mídias…';
        try{
            if(!creating){await prepareSessionRecovery(document.querySelector('#property-form'));token=document.querySelector('#property-form [name=__RequestVerificationToken]').value;}
            if(creating){const saved=await saveDraftForUpload();created=true;url=saved.path+'?handler=Upload';token=saved.token;}
            const response=await fetch(url,{method:'POST',body,credentials:'same-origin',headers:{RequestVerificationToken:token,'X-Media-Revision':gallery.dataset.revision}});const result=await response.json().catch(()=>({}));
            if(!response.ok||result.ok!==true){const error=Error(response.status===401?'Seu acesso precisa ser retomado. Nenhum arquivo foi confirmado.':messages[result.code]??(response.status===413?messages['Media.TooLarge']:'Não foi possível concluir. Confira a galeria antes de tentar novamente; seus dados continuam nesta tela.'));error.authenticationRequired=response.status===401;throw error;}
            // A fragment-only assignment does not reload the persisted gallery.
            history.replaceState(null,'',location.pathname+'?step=9#gallery');
            location.reload();
        }catch(error){status.textContent=(created?'O rascunho foi salvo; as mídias ainda não foram confirmadas. ':'')+error.message;if(error.authenticationRequired)offerSessionRecovery(status);busy=false;gallery.removeAttribute('aria-busy');}
    }
    gallery.querySelector('#media-camera')?.addEventListener('change',event=>{
        if(!event.target.files.length)return;
        const transfer=new DataTransfer();for(const file of event.target.files)transfer.items.add(file);
        upload.querySelector('#media-files').files=transfer.files;
        status.textContent='Foto selecionada. Envie as mídias para confirmar a persistência.';
    });
    upload?.addEventListener('submit',event=>{event.preventDefault();const input=upload.querySelector('[type=file]'),files=[...input.files];if(files.length>20||files.reduce((n,f)=>n+f.size,0)>100*1024*1024||files.some(f=>f.size>(f.type==='video/mp4'?50:10)*1024*1024)){status.textContent=messages['Media.TooLarge'];return;}const body=new FormData();for(const file of files)body.append('files',file);void send(upload.action,body,upload.querySelector('[name=__RequestVerificationToken]').value);});
    for(const form of gallery.querySelectorAll('[data-media-action]'))form.addEventListener('submit',event=>{event.preventDefault();if(form.hasAttribute('data-media-remove')&&!confirm('Remover esta mídia do imóvel?'))return;void send(form.action,new URLSearchParams(new FormData(form)),form.querySelector('[name=__RequestVerificationToken]').value);});
    for(const button of gallery.querySelectorAll('[data-media-move]'))button.addEventListener('click',()=>{const cards=[...gallery.querySelectorAll('[data-media-id]')],index=cards.indexOf(button.closest('[data-media-id]')),next=index+Number(button.dataset.mediaMove);if(next<0||next>=cards.length)return;[cards[index],cards[next]]=[cards[next],cards[index]];const body=new URLSearchParams({mediaRevision:gallery.dataset.revision});for(const card of cards)body.append('order',card.dataset.mediaId);const token=gallery.querySelector('[name=__RequestVerificationToken]').value;void send(location.pathname+'?handler=Reorder',body,token);});
}
