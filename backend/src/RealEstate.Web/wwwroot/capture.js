import {applicable,evaluateExpression} from './adaptation.js';
export const normalizeChoice = value => value.normalize('NFD').replace(/\p{M}/gu, '').trim().toLocaleUpperCase('pt-BR');
const form = document.querySelector('#property-form');
if (form) {
    const rules = JSON.parse(form.dataset.adaptation);
    const value = path => form.elements.namedItem('Input.' + path)?.value;
    function update() {
        for (const element of form.querySelectorAll('[data-capture-when]')) element.hidden = !applicable(rules,element.dataset.captureWhen,value);
        for (const choice of form.querySelectorAll('.capture-choice')) {
            const collective=choice.closest('[data-choice-group]').dataset.choiceGroup.startsWith('collective-');
            const inherit=collective&&value('Capture.Condominium.Inherit')==='true';
            const local=choice.querySelector('input[type="checkbox"]').checked;
            const override=choice.querySelector('[name$=".SharedOverride"]');
            const editor=form.querySelector('[data-condominium-editor]');
            const base=JSON.parse(value('Capture.Condominium.Id')?editor?.dataset.shared??'[]':'[]').find(s=>s.groupId===choice.closest('[data-choice-group]').dataset.choiceGroup&&s.optionId===choice.dataset.choice);
            const selected=evaluateExpression(rules.collectiveSelection,key=>({inherit:String(inherit),override:override?.value??'',local:String(local),shared:String(base?.selected===true)})[key]);
            if(override){override.closest('label').hidden=!inherit;choice.querySelector('.capture-chip').hidden=inherit;
                const origin=inherit&&base?' · '+editor.dataset.sharedName+' · versão '+value('Capture.Condominium.ObservedVersion'):'';
                const mode=override.value==='true'?'Exceção individual: incluir':override.value==='false'?'Exceção individual: excluir':base?.selected&&!local?'Herdado do condomínio':local?'Seleção individual preservada':'Não selecionado';
                choice.querySelector('[data-inherited-state]').textContent=(selected?'Disponível · ':'Não disponível · ')+mode+origin;
                choice.querySelector('[data-inherited-detail]').textContent=inherit&&base?.detail?'Detalhe do condomínio: '+base.detail:'';}
            choice.querySelector('.capture-choice-detail').hidden = !selected;
        }
    }
    form.addEventListener('change', event => {
        const group = event.target.closest('[data-choice-group="guarantees"]');
        if (group && event.target.matches('input[type="checkbox"]') && event.target.checked) {
            const none = event.target.closest('[data-choice]').dataset.choice === 'none';
            for (const choice of group.querySelectorAll('[data-choice]')) {
                if (none || choice.dataset.choice === 'none') {
                    const checkbox = choice.querySelector('input[type="checkbox"]');
                    if (checkbox !== event.target) checkbox.checked = false;
                }
            }
        }
        update();
    });
    form.addEventListener('condominium:changed',()=>{
        for(const feature of JSON.parse(form.querySelector('[data-condominium-editor]').dataset.shared??'[]')){
            const group=[...form.querySelectorAll('[data-choice-group]')].find(g=>g.dataset.choiceGroup===feature.groupId);
            if(!group||[...group.querySelectorAll('[data-choice]')].some(c=>c.dataset.choice===feature.optionId))continue;
            const choice=group.querySelector('.capture-choice').cloneNode(true),index=form.querySelectorAll('.capture-choice').length;
            choice.dataset.choice=feature.optionId;
            for(const field of choice.querySelectorAll('input,select,textarea')){
                field.name=field.name.replace(/Selections\[\d+\]/,`Selections[${index}]`);
                if(field.name.endsWith('.GroupId'))field.value=feature.groupId;
                else if(field.name.endsWith('.OptionId'))field.value=feature.optionId;
                else if(field.name.endsWith('.Label'))field.value=feature.label;
                else if(field.type==='checkbox')field.checked=false;
                else if(!field.name.endsWith('.Selected'))field.value='';
            }
            choice.querySelector('.capture-chip span').textContent=feature.label;
            choice.querySelector('.capture-inheritance').firstChild.textContent=feature.label+' — exceção no imóvel';
            choice.querySelector('.capture-choice-detail > label').firstChild.textContent='Detalhes de '+feature.label;
            group.querySelector('.capture-choices').append(choice);
        }
        update();
    });
    for(const button of form.querySelectorAll('[data-use-responsible]'))button.addEventListener('click',()=>{
        const field=form.elements.namedItem('Input.Capture.Acquisition.ResponsibleSubject');
        if([...field.options].some(option=>option.value===button.dataset.useResponsible)){
            field.value=button.dataset.useResponsible;field.dispatchEvent(new Event('change',{bubbles:true}));
        }
    });
    for (const group of form.querySelectorAll('[data-choice-group]')) {
        group.querySelector('[data-choice-add]').addEventListener('click', () => {
            group.querySelector('[data-choice-new]').hidden = false;
            group.querySelector('[data-choice-label]').focus();
        });
        group.querySelector('[data-choice-confirm]').addEventListener('click', () => {
            const input = group.querySelector('[data-choice-label]');
            const label = input.value.trim();
            const message = group.querySelector('[data-choice-message]');
            const choices = [...group.querySelectorAll('.capture-choice')];
            if (!label) {message.textContent = 'Informe o nome da opção.'; return;}
            if (choices.some(c => normalizeChoice(c.querySelector('input[name$=".Label"]').value) === normalizeChoice(label))) {
                message.textContent = 'Esta opção já existe neste grupo.'; return;
            }
            if (form.querySelectorAll('.capture-choice').length >= 300) {message.textContent = 'Limite de opções atingido.'; return;}
            const choice = choices[0].cloneNode(true);
            const index = form.querySelectorAll('.capture-choice').length;
            const id = 'custom:' + crypto.randomUUID();
            choice.dataset.choice = id;
            for (const field of choice.querySelectorAll('input,textarea,select')) {
                field.name = field.name.replace(/Selections\[\d+\]/, `Selections[${index}]`);
                if (field.name.endsWith('.GroupId')) field.value = group.dataset.choiceGroup;
                else if (field.name.endsWith('.OptionId')) field.value = id;
                else if (field.name.endsWith('.Label')) field.value = label;
                else if (field.type === 'checkbox') field.checked = true;
                else if (!field.name.endsWith('.Selected')) field.value = '';
            }
            choice.querySelector('.capture-chip span').textContent = label;
            choice.querySelector('.capture-choice-detail > label').firstChild.textContent = 'Detalhes de ' + label;
            group.querySelector('.capture-choices').append(choice);
            input.value = '';group.querySelector('[data-choice-new]').hidden = true;message.textContent = '';
            choice.querySelector('input[type="checkbox"]').dispatchEvent(new Event('change', {bubbles:true}));
            choice.querySelector('input[type="checkbox"]').focus();
        });
    }
    update();
}
