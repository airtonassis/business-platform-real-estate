// Same cadastro, same editors. Only the existing announcement contract is writable.
const form=document.querySelector('#property-form');
for(const element of form?.querySelectorAll('input,select,textarea,button')??[]){
 const name=element.name??'';
 const commercial=name.startsWith('Input.Capture.Values.')||name==='Input.Purpose';
 if(name.startsWith('Input.')&&!commercial)element.disabled=true;
 if(element.tagName==='BUTTON'&&element.closest('.cad-section')&&!element.closest('#cad-6'))element.disabled=true;
}
for(const field of form?.querySelectorAll('.form-field')??[]){
 if(!field.querySelector('[name="Input.Purpose"]')&&field.querySelector('[name^="Input."]:disabled')){
  field.querySelectorAll('input,select,textarea,button').forEach(e=>e.disabled=true);
 }
}
