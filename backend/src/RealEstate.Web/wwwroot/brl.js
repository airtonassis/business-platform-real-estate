// Decimal strings only: monetary values never pass through binary floating point.
export function parseBrl(text){
    const value=text.trim();if(!value)return '';
    if(!/^(?:\d+|\d{1,3}(?:\.\d{3})+)(?:,\d{0,2})?$/.test(value))return null;
    const [whole,fraction='']=value.replaceAll('.','').split(',');
    return whole.replace(/^0+(?=\d)/,'')+'.'+fraction.padEnd(2,'0');
}
export function displayBrl(canonical){
    if(!canonical)return '';
    const [whole,fraction]=canonical.split('.');
    return whole.replace(/\B(?=(\d{3})+(?!\d))/g,'.')+','+fraction;
}
if(typeof document!=='undefined'){
    const activate=root=>root.querySelectorAll('[data-brl-input]').forEach(e=>{if(!e.closest('fieldset:disabled'))e.disabled=false;});
    activate(document);
    new MutationObserver(records=>{for(const r of records)for(const n of r.addedNodes)if(n.nodeType===1)activate(n);}).observe(document.body,{childList:true,subtree:true});
    document.addEventListener('input',event=>{
        const e=event.target;if(!e.matches('[data-brl-input]'))return;
        const value=parseBrl(e.value);let message=value===null?'Informe um valor em reais, como 1.234,50, com até duas casas decimais.':'';
        if(value&&value!==null){const cents=BigInt(value.replace('.',''));const minimum=BigInt((e.dataset.brlMin??'0').split('.').map((v,i)=>i?v.padEnd(2,'0'):v).join('')+(e.dataset.brlMin?.includes('.')?'':'00'));if(cents<minimum)message='Informe um valor maior ou igual a '+e.dataset.brlMin.replace('.',',')+'.';}
        e.setCustomValidity(message);e.dataset.brlEdited='true';
        if(!message)e.nextElementSibling.value=value;
    });
    document.addEventListener('focusout',event=>{const e=event.target;if(e.matches('[data-brl-input]')&&e.dataset.brlEdited&&e.validity.valid)e.value=displayBrl(e.nextElementSibling.value);});
}
