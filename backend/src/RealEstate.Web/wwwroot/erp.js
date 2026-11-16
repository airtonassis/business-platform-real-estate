'use strict';
if(location.pathname.startsWith('/commercial/customers/')&&!new URLSearchParams(location.search).has('Panel')){
    const legacyPanel={'#presented-properties':'properties','#property-search':'properties','#customer-history':'history','#customer-agenda':'agenda','#customer-data':'data'}[location.hash];
    if(legacyPanel){const query=new URLSearchParams(location.search);query.set('Panel',legacyPanel);location.replace(location.pathname+'?'+query.toString()+location.hash);}
}
const appointmentForm=document.querySelector('form[data-appointment-unit]');
if(appointmentForm){
    const property=appointmentForm.querySelector('[name="AppointmentPropertyId"]');
    const location=appointmentForm.querySelector('[name="AppointmentLocation"]');
    const updateKind=()=>{
        const kind=appointmentForm.querySelector('[name="AppointmentKind"]:checked')?.value;
        if(!kind)return;
        if(property){property.required=kind==='Visita ao imóvel';property.closest('label').hidden=kind==='Atendimento na imobiliária';}
        if(kind==='Atendimento na imobiliária'){if(property)property.value='';location.value=appointmentForm.dataset.appointmentUnit;location.readOnly=true;}
        else if(location.readOnly){location.value='';location.readOnly=false;}
        location.placeholder=kind==='Visita de captação'?'Endereço do imóvel a captar':'Local da visita';
    };
    appointmentForm.addEventListener('change',event=>{if(event.target.name==='AppointmentKind')updateKind()});
    updateKind();
}
document.querySelector('[data-agenda-new]')?.addEventListener('click',()=>{
    const panel=document.querySelector('#agenda-new');if(!panel)return;
    panel.open=true;panel.scrollIntoView({behavior:'smooth',block:'start'});panel.querySelector('summary')?.focus();
});
document.querySelector('#search:not([data-server-search])')?.addEventListener('input',event=>{
    let count=0;
    for(const row of document.querySelectorAll('[data-property]')){
        row.hidden=!row.dataset.search.toLocaleLowerCase('pt-BR').includes(event.target.value.toLocaleLowerCase('pt-BR'));
        if(!row.hidden)count++;
    }
    const empty=document.querySelector('#no-matches');if(empty)empty.hidden=count>0;
    const status=document.querySelector('#search-status');
    if(status)status.textContent=`${count} ${count===1?'imóvel encontrado':'imóveis encontrados'}.`;
});
const forms=document.querySelectorAll('form[data-submit-feedback]');
for(const form of forms)form.addEventListener('submit',event=>{
    if(event.defaultPrevented)return;
    const button=event.submitter??form.querySelector('button[type=submit]');
    if(!button||button.disabled){event.preventDefault();return;}
    button.dataset.originalLabel??=button.textContent;
    button.disabled=true;button.textContent=button.dataset.pendingLabel??'Enviando…';
    form.setAttribute('aria-busy','true');
    const status=form.querySelector('.submit-status');if(status)status.textContent=button.textContent;
});
addEventListener('pageshow',()=>{
    for(const form of forms){
        form.removeAttribute('aria-busy');
        for(const button of form.querySelectorAll('button[data-original-label]')){
            button.textContent=button.dataset.originalLabel;button.disabled=false;
        }
        const status=form.querySelector('.submit-status');if(status)status.textContent='';
    }
});
document.querySelector('[data-error-focus]')?.focus();
