// Keep valid edits on screen when a save is denied or a dependency becomes unavailable.
import {offerSessionRecovery,prepareSessionRecovery} from './session-recovery.js';
const form=document.querySelector('#property-form');
if(form){let saving=false;form.addEventListener('submit',async event=>{
    event.preventDefault();if(saving)return;saving=true;
    const button=event.submitter,original=button?.dataset.originalLabel??button?.textContent;
    if(button){button.disabled=true;button.textContent='Salvando…';}form.setAttribute('aria-busy','true');
    let alert=document.querySelector('#save-feedback');
    if(!alert){alert=document.createElement('div');alert.id='save-feedback';alert.className='error';alert.setAttribute('role','alert');alert.tabIndex=-1;form.before(alert);}
    alert.hidden=true;
    try{
        await prepareSessionRecovery(form);
        let body=new URLSearchParams(new FormData(form));
        if(form.dataset.propertyCopy==='true'){body=new FormData(form);for(const file of document.querySelector('#media-files')?.files??[])body.append('files',file);}
        const response=await fetch(button?.getAttribute('formaction')??form.action,{method:'POST',body,credentials:'same-origin'});
        if(response.ok){location.assign(response.url);return;}
        const doc=new DOMParser().parseFromString(await response.text(),'text/html');
        alert.textContent=response.status===401?'Seu acesso precisa ser retomado. Seus dados continuam nesta tela.':doc.querySelector('[role=alert]')?.textContent.trim()??'Não foi possível salvar agora. Seus dados continuam nesta tela; tente novamente.';
        const reference=response.headers.get('X-Request-ID');
        if(reference)alert.textContent+=' Referência: '+reference+'.';
        if(response.status===401)offerSessionRecovery(alert);
    }catch(error){alert.textContent=error.sessionRecovery?error.message:'Não foi possível confirmar o salvamento. Seus dados continuam nesta tela. Confira a lista de imóveis antes de tentar novamente.';if(error.authenticationRequired)offerSessionRecovery(alert);}
    finally{saving=false;form.removeAttribute('aria-busy');if(button){button.disabled=false;button.textContent=original;}if(alert.textContent){alert.hidden=false;alert.focus();}}
});}
