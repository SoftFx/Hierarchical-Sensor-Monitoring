export class MutationObserverService {
    initialValues: { [key: string]: string } = {};
    form: HTMLFormElement | null = null;
    constructor() {
    }
    
    public addFormToObserve(id: string) {
        this.form = document.getElementById(id) as HTMLFormElement;
        this.initialValues = this.getFormValues();

        // Подписка на событие изменения формы
        this.form.addEventListener('change', (event) => {
            if (event.target) {
                this.checkChanges(event.target as Element);
            }
        });
    }

    getFormValues(): { [key: string]: string } {
        const values: { [key: string]: string } = {};
        const elements = this.form.querySelectorAll('select, input, textarea, .filter-option-inner-inner');

        elements.forEach((element: HTMLElement) => {
            if (element.id) {
                if (element instanceof HTMLInputElement || element instanceof HTMLSelectElement || element instanceof HTMLTextAreaElement)
                    values[element.id] = element.value;
                else 
                    values[element.id] = element.textContent;
            }
        })

        return values;
    }

    checkChanges(element: Element): void {
        const currentValue = element instanceof HTMLInputElement || element instanceof HTMLSelectElement || element instanceof HTMLTextAreaElement ? element.value : '';
        const initialValue = this.initialValues[element.id || element.getAttribute('name')]; // Use id or name

        if (currentValue !== initialValue) {
            console.log(`Изменение: ${element.tagName} (id: ${element.id || 'no id'}, name: ${element.getAttribute('name') || 'no name'})`);
            console.log(`${initialValue} -> ${currentValue}`);
        }
    }
}