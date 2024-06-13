export class MutationObserverService {
    alertNames = ["Chats", "Schedule"]
    
    initialValues: { [key: string]: string } = {};
    newValue: { [key: string]: string } = {};
    form: HTMLFormElement | null = null;
    
    oldVals: { [key: string]: string } = {};
    newVals: { [key: string]: string } = {};
    
    constructor() {
    }
    
    public addFormToObserve(id: string) {
        this.form = document.getElementById(id) as HTMLFormElement;
        this.initialValues = this.getFormValues();
        this.newValue = structuredClone(this.initialValues);
        // Подписка на событие изменения формы
        this.form.addEventListener('change', (event) => {
            if (event.target) {
                this.checkChanges(event.target as Element);
            }
        });
    }

    getFormValues(): { [key: string]: string } {
        const values: { [key: string]: string } = {};
        const elements = this.form.querySelectorAll('select, input, textarea');

        elements.forEach((element: HTMLElement) => {
            let closest = element.closest('.dataAlertRow');
            
            if (closest !== null){
                let currentValue = '';
                if (element instanceof HTMLSelectElement){
                    let arr = Array.from(element.selectedOptions);
                    for (let i of arr){
                        currentValue += "," + i.value
                    }
                }
                else if (element instanceof HTMLInputElement || element instanceof HTMLTextAreaElement)
                    currentValue = element.value;
                
                values[closest.id + element.getAttribute('name')]  = currentValue;
            }
            else if (element instanceof HTMLSelectElement){
                let currentValue = '';
                if (element instanceof HTMLSelectElement){
                    let arr = Array.from(element.selectedOptions);
                    for (let i of arr){
                        currentValue += "," + i.value
                    }
                }
                values[element.id || element.getAttribute('name')] = currentValue;
            }
            else if (element instanceof HTMLInputElement || element instanceof HTMLTextAreaElement) {
                values[element.id || element.getAttribute('name')] = element.value;
            }
        })

        return values;
    }

    checkChanges(element: Element): void {
        let currentValue = '';
        let initialValue = '';
        let id = '';

        let closest = element.closest('.dataAlertRow');
        if (closest !== null){
            id = closest.id + element.getAttribute('name');
            if (element instanceof HTMLSelectElement){
                let arr = Array.from(element.selectedOptions);
                for (let i of arr){
                    currentValue += "," + i.value
                }
            }
            else if (element instanceof HTMLInputElement || element instanceof HTMLTextAreaElement)
                currentValue = element.value;
        }
        else {
            id = element.id || element.getAttribute('name');
            if (element instanceof HTMLSelectElement){
                let arr = Array.from(element.selectedOptions);
                for (let i of arr){
                    currentValue += "," + i.value
                }
            }
            else
                currentValue = element instanceof HTMLInputElement || element instanceof HTMLTextAreaElement ? element.value : "";
        }

        initialValue = this.initialValues[id];
        console.log(element);

        if (currentValue !== initialValue) {
            console.log(`Изменение: ${element.tagName} (id: ${element.id || 'no id'}, name: ${element.getAttribute('name') || 'no name'})`);
            console.log(`${initialValue} -> ${currentValue}`);
        }
        
        this.newValue[id] = currentValue;
    }
    public check(){
        console.log(this.newValue)
        console.log(this.initialValues)
        
        console.log(this.newValue === this.initialValues);
        
        for(let i of Object.entries(this.newValue)){
            if (this.newValue[i[0]] !== this.initialValues[i[0]])
            {
                console.log(this.newValue[i[0]])
                console.log(this.initialValues[i[0]])
                console.log("no")
                return;
            }
        }
        
        console.log("yes")
    }
}