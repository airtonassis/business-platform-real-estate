// Native selects remain the single submitted value and emit the existing adaptation events.
for(const select of document.querySelectorAll('#property-form select[name="Input.Purpose"],#property-form select[name="Input.IsInCondominium"],#property-form select.capture-select')){
    if(select.disabled||select.closest('fieldset[disabled]'))continue;
    if(select.options.length>(select.name==='Input.Purpose'?8:6)||select.name.endsWith('ResponsibleSubject'))continue;
    const label=select.closest('.form-field')?.querySelector('label');if(!label)continue;
    const group=document.createElement('div');group.className='compact-choices';group.setAttribute('role','radiogroup');group.setAttribute('aria-label',label.textContent.trim());
    const buttons=[...select.options].map(option=>{const b=document.createElement('button');b.type='button';b.textContent=option.textContent;b.setAttribute('role','radio');b.dataset.value=option.value;
        b.addEventListener('click',()=>{select.value=option.value;select.dispatchEvent(new Event('change',{bubbles:true}));});group.append(b);return b;});
    function update(){for(const b of buttons){const checked=b.dataset.value===select.value;b.setAttribute('aria-checked',String(checked));b.tabIndex=checked?0:-1;}}
    group.addEventListener('keydown',event=>{if(!['ArrowRight','ArrowDown','ArrowLeft','ArrowUp','Home','End'].includes(event.key))return;event.preventDefault();let i=buttons.indexOf(document.activeElement);i=event.key==='Home'?0:event.key==='End'?buttons.length-1:(i+(event.key==='ArrowRight'||event.key==='ArrowDown'?1:-1)+buttons.length)%buttons.length;buttons[i].click();buttons[i].focus();});
    select.hidden=true;select.after(group);select.addEventListener('change',update);update();
}
