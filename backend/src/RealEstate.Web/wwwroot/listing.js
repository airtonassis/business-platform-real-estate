// Reusable page-local selection; no bulk mutation and no persistent selection across queries.
for(const image of document.querySelectorAll('img.property-cover')){
    const fallback=()=>{const placeholder=document.createElement('span');placeholder.className='property-photo-placeholder';placeholder.setAttribute('role','img');placeholder.setAttribute('aria-label','Foto principal indisponível');placeholder.textContent='Sem foto';image.replaceWith(placeholder);};
    image.addEventListener('error',fallback,{once:true});if(image.complete&&!image.naturalWidth)fallback();
}
for(const listing of document.querySelectorAll('[data-listing]')){
    const all=listing.querySelector('[data-select-page]'),rows=[...listing.querySelectorAll('[data-select-row]')],status=listing.querySelector('[data-selection-status]');
    if(!all)continue;
    function update(){const count=rows.filter(e=>e.checked).length;all.checked=count>0&&count===rows.length;all.indeterminate=count>0&&count<rows.length;status.textContent=`${count} ${count===1?'imóvel selecionado':'imóveis selecionados'} nesta página.`;}
    all.addEventListener('change',()=>{for(const row of rows)row.checked=all.checked;update();});
    for(const row of rows)row.addEventListener('change',update);
    listing.querySelector('[data-clear-selection]')?.addEventListener('click',()=>{for(const row of rows)row.checked=false;update();});
    update();
}
