import {applicable} from './adaptation.js';
const form = document.querySelector('#property-form');
if (form) {
    // These descriptors and vocabularies are emitted by the same server presentation metadata.
    const rules = JSON.parse(form.dataset.adaptation);
    const field = name => form.querySelector(`[name="Input.${name}"]`);
    const showCategory = (number, show) => {
        document.querySelector(`#cad-${number}`).hidden = !show;
        document.querySelector(`[data-category-link="${number}"]`).hidden = !show;
    };
    function update() {
        const read=name=>field(name)?.value;
        const rural = applicable(rules,'rural',read);
        const purpose = field('Purpose').value;
        const sale = applicable(rules,'sale',read);
        const rental = applicable(rules,'rental',read);
        // Hidden controls remain enabled and keep their values during progressive saves.
        for (const name of rules.urbanAddressFields) {
            form.querySelector(`[name="Input.Address.${name}"]`)?.closest('.form-field').toggleAttribute('hidden', rural);
        }
        form.querySelector('label[for="Input_Address_LegacyText"]').textContent = rural ? 'Referência de acesso rural' : 'Endereço livre / referência de acesso rural';
        const inapplicable = form.querySelector('[data-inapplicable] ul');
        inapplicable.replaceChildren();
        for (const category of rules.categories) {
            const show = applicable(rules,category.requirement,read);
            showCategory(category.id, show);
            if (!show) {
                const pending = category.requirement === 'condominium' ? !field('IsInCondominium').value : !purpose;
                const entry = document.createElement('li');
                const reason = category.requirement === 'later' ? 'fora da captação inicial'
                    : category.requirement === 'integrated' ? 'prevista em Valores e condições; sem etapa própria'
                    : pending ? 'aguardando definição' : 'não aplicável neste contexto';
                entry.textContent = `${category.name} — ${reason}`;
                inapplicable.append(entry);
            }
        }
        form.querySelector('[data-inapplicable]').hidden = !inapplicable.children.length;
        form.querySelector('[data-purpose-guidance]').textContent = sale && rental
            ? 'Venda e locação têm valores independentes. Preencha o que souber.'
            : sale ? 'Valores e condições de venda. O rascunho pode ser salvo sem preço.'
            : rental ? 'Valores e condições de locação. O rascunho pode ser salvo sem preço.' : 'As condições acompanham a finalidade do imóvel.';
        form.querySelector('#adaptive-status').textContent = `${rural ? 'Imóvel rural' : field('Type').value ? 'Imóvel urbano' : 'Selecione o tipo de imóvel'}${purpose ? ' · ' + purpose : ''}${field('IsInCondominium').value === 'true' ? ' · Em condomínio' : ''}`;
        updateSummary();
        syncNavigation();
    }
    for (const name of ['Type', 'Purpose', 'IsInCondominium']) field(name).addEventListener('change', update);
    const sections = [...form.querySelectorAll('.cad-section')];
    const links = [...form.querySelectorAll('.cad-categories a')];
    const next = form.querySelector('[data-cad-next]');
    const back = form.querySelector('[data-cad-back]');
    const picker = document.createElement('select');
    picker.id = 'cad-category-picker';
    const pickerLabel = document.createElement('label');
    pickerLabel.htmlFor = picker.id;
    pickerLabel.textContent = 'Categoria do cadastro';
    pickerLabel.className = 'cad-picker-label';
    form.querySelector('.cad-categories').prepend(pickerLabel, picker);
    let active = 'cad-' + (form.querySelector('[name="Step"]')?.value ?? '1');
    function selectSection(id, focus = true) {
        const target = sections.find(section => section.id === id && !section.hidden);
        if (!target) return;
        active = id;
        const stepField = form.querySelector('[name="Step"]');
        if (stepField) stepField.value = id.slice('cad-'.length);
        for (const section of sections) {
            section.classList.toggle('cad-active', section === target);
            section.open = section === target;
        }
        for (const link of links) {
            const selected = link.hash === '#' + id;
            link.classList.toggle('cad-current', selected);
            if (selected) link.setAttribute('aria-current', 'step');
            else link.removeAttribute('aria-current');
        }
        picker.value = id;
        next.hidden = target === sections.filter(s => !s.hidden).at(-1);
        back.hidden = target === sections.filter(s => !s.hidden)[0];
        form.querySelector('[data-location-tools]').hidden = id !== 'cad-2';
        const gallery = document.querySelector('#gallery');
        if (gallery) gallery.hidden = id !== 'cad-9';
        if (focus) target.querySelector('summary').focus();
    }
    function syncNavigation() {
        picker.replaceChildren(...sections.filter(s => !s.hidden).map(section => {
            const option = document.createElement('option');
            option.value = section.id;
            const category = rules.categories.find(c => 'cad-' + c.id === section.id);
            option.textContent = `${category.name} — ${category.availability}`;
            return option;
        }));
        const previousIndex = sections.findIndex(s => s.id === active);
        const closest = sections.filter(s => !s.hidden).sort((a,b) =>
            Math.abs(sections.indexOf(a) - previousIndex) - Math.abs(sections.indexOf(b) - previousIndex))[0];
        selectSection(closest.id, false);
    }
    links.forEach(link => {
        link.addEventListener('click', event => {
            event.preventDefault();
            selectSection(link.hash.slice(1));
        });
    });
    picker.addEventListener('change', () => selectSection(picker.value));
    next.addEventListener('click', () => {
        const visible = sections.filter(s => !s.hidden);
        const following = visible[visible.findIndex(s => s.id === active) + 1];
        if (following) selectSection(following.id);
    });
    back.addEventListener('click', () => {
        const visible = sections.filter(s => !s.hidden);
        const previous = visible[visible.findIndex(s => s.id === active) - 1];
        if (previous) selectSection(previous.id);
    });
    function updateSummary() {
        document.querySelector('[data-summary-type]').textContent = field('Type').value || 'Novo imóvel';
        const category = document.querySelector('[data-summary-category]');
        category.textContent = field('Category').value;
        category.hidden = !category.textContent;
        const owner = document.querySelector('[data-summary-owner]');
        const ownerField = field('OwnerId');
        owner.hidden = !ownerField.value;
        owner.textContent = ownerField.value ? 'Proprietário: ' + ownerField.selectedOptions[0].textContent : '';
        form.querySelector('[data-owner-reference]').textContent = ownerField.value
            ? owner.textContent + '. Vínculo selecionado na Identificação.'
            : 'O proprietário pode ser selecionado na Identificação. O rascunho pode ser salvo sem esse vínculo.';
    }
    for (const name of ['Category', 'OwnerId']) field(name).addEventListener('change', updateSummary);
    form.addEventListener('invalid', event => {
        const section = event.target.closest('.cad-section');
        if (section && !section.hidden) selectSection(section.id, false);
    }, true);
    form.classList.add('cad-enhanced');
    update();
}
